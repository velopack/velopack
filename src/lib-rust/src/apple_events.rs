//! Receives the Apple Events that macOS (LaunchServices) sends when this app is asked to open a URL scheme or a
//! document type it is registered for, and forwards them to the `VelopackApp` association hooks.
//!
//! macOS does not pass the opened file / URL on the command line; `kAEGetURL` ('GURL') and `kAEOpenDocuments`
//! ('odoc') Apple Events are delivered to the process instead, and are dispatched on the main thread by the
//! application's Cocoa run loop. AppKit installs its own handlers for these events inside
//! `-[NSApplication finishLaunching]`, overwriting any registered earlier, so besides installing ours immediately
//! we also observe `NSApplicationWillFinishLaunchingNotification` (posted from inside `finishLaunching`, after
//! AppKit's handlers are in place but before the launch events are dispatched) and install them again.
//!
//! This talks to the Objective-C runtime through a few hand-declared FFI entry points; only Foundation is needed
//! (the notification name is matched by string value, so AppKit is not linked).

#![allow(non_camel_case_types)]

use std::ffi::{c_char, c_void, CStr, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::{Mutex, OnceLock};

use crate::app::AssociationHook;

type id = *mut c_void;
type SEL = *mut c_void;
type Class = *mut c_void;
type IMP = unsafe extern "C" fn();
/// `BOOL` is `signed char` on x86_64 and `bool` on arm64; both come back as a 0/1 byte in the return register.
type BOOL = i8;

#[link(name = "objc", kind = "dylib")]
extern "C" {
    fn objc_getClass(name: *const c_char) -> Class;
    fn sel_registerName(name: *const c_char) -> SEL;
    fn objc_msgSend();
    fn objc_allocateClassPair(superclass: Class, name: *const c_char, extra_bytes: usize) -> Class;
    fn objc_registerClassPair(cls: Class);
    fn class_addMethod(cls: Class, name: SEL, imp: IMP, types: *const c_char) -> BOOL;
    fn objc_autoreleasePoolPush() -> *mut c_void;
    fn objc_autoreleasePoolPop(pool: *mut c_void);
}

#[link(name = "Foundation", kind = "framework")]
extern "C" {}

const fn fourcc(code: &[u8; 4]) -> u32 {
    ((code[0] as u32) << 24) | ((code[1] as u32) << 16) | ((code[2] as u32) << 8) | (code[3] as u32)
}

const K_INTERNET_EVENT_CLASS: u32 = fourcc(b"GURL");
const K_AE_GET_URL: u32 = fourcc(b"GURL");
const K_CORE_EVENT_CLASS: u32 = fourcc(b"aevt");
const K_AE_OPEN_DOCUMENTS: u32 = fourcc(b"odoc");
const KEY_DIRECT_OBJECT: u32 = fourcc(b"----");

const SEL_GET_URL: &[u8] = b"velopackHandleGetURLEvent:withReplyEvent:\0";
const SEL_OPEN_DOCUMENTS: &[u8] = b"velopackHandleOpenDocumentsEvent:withReplyEvent:\0";
const SEL_WILL_FINISH_LAUNCHING: &[u8] = b"velopackApplicationWillFinishLaunching:\0";

#[derive(Default)]
struct Hooks {
    file: Option<AssociationHook>,
    url: Option<AssociationHook>,
}

/// The Objective-C object whose methods receive the events. Created once, retained for the life of the process.
struct Target(id);
unsafe impl Send for Target {}
unsafe impl Sync for Target {}

static HOOKS: Mutex<Hooks> = Mutex::new(Hooks { file: None, url: None });
static TARGET: OnceLock<Target> = OnceLock::new();

fn hooks() -> std::sync::MutexGuard<'static, Hooks> {
    HOOKS.lock().unwrap_or_else(|e| e.into_inner())
}

/// Registers the given hooks and installs the Apple Event handlers that feed them. Safe to call more than once;
/// the latest hooks win. Must be called on the thread that will run the Cocoa run loop (normally the main thread).
pub fn install(file_hook: Option<AssociationHook>, url_hook: Option<AssociationHook>) {
    {
        let mut h = hooks();
        h.file = file_hook;
        h.url = url_hook;
    }

    let target = TARGET.get_or_init(|| Target(unsafe { create_target() }));
    if target.0.is_null() {
        warn!("VelopackApp: Unable to create the Apple Event target object, association hooks will not be invoked.");
        return;
    }

    unsafe { install_event_handlers(target.0) };
}

unsafe fn sel(name: &[u8]) -> SEL {
    debug_assert!(name.ends_with(b"\0"));
    sel_registerName(name.as_ptr() as *const c_char)
}

unsafe fn class(name: &[u8]) -> Class {
    debug_assert!(name.ends_with(b"\0"));
    objc_getClass(name.as_ptr() as *const c_char)
}

// objc_msgSend is declared without a prototype and cast to the exact signature of each message, which is how
// every Objective-C binding calls it (the variadic and non-variadic conventions agree for pointer/integer args).
macro_rules! msg_send_fn {
    ($name:ident, ($($arg:ident: $ty:ty),*) -> $ret:ty) => {
        unsafe fn $name(receiver: id, selector: SEL, $($arg: $ty),*) -> $ret {
            let f: unsafe extern "C" fn(id, SEL, $($ty),*) -> $ret = std::mem::transmute(objc_msgSend as unsafe extern "C" fn());
            f(receiver, selector, $($arg),*)
        }
    };
}

msg_send_fn!(msg_send_id, () -> id);
msg_send_fn!(msg_send_isize, () -> isize);
msg_send_fn!(msg_send_utf8, () -> *const c_char);
msg_send_fn!(msg_send_id_u32, (a: u32) -> id);
msg_send_fn!(msg_send_id_isize, (a: isize) -> id);
msg_send_fn!(msg_send_id_cstr, (a: *const c_char) -> id);
msg_send_fn!(msg_send_bool_sel, (a: SEL) -> BOOL);
msg_send_fn!(msg_send_set_event_handler, (target: id, selector: SEL, event_class: u32, event_id: u32) -> ());
msg_send_fn!(msg_send_add_observer, (observer: id, selector: SEL, name: id, object: id) -> ());

/// `[[NSString alloc] initWithUTF8String:]` — owned (not autoreleased) so it can be created outside a pool.
unsafe fn nsstring(text: &str) -> id {
    let c = match CString::new(text) {
        Ok(c) => c,
        Err(_) => return std::ptr::null_mut(),
    };
    let alloc = msg_send_id(class(b"NSString\0"), sel(b"alloc\0"));
    msg_send_id_cstr(alloc, sel(b"initWithUTF8String:\0"), c.as_ptr())
}

unsafe fn nsstring_to_string(nsstring: id) -> Option<String> {
    if nsstring.is_null() {
        return None;
    }
    let utf8 = msg_send_utf8(nsstring, sel(b"UTF8String\0"));
    if utf8.is_null() {
        return None;
    }
    Some(CStr::from_ptr(utf8).to_string_lossy().into_owned())
}

unsafe fn create_target() -> id {
    let superclass = class(b"NSObject\0");
    if superclass.is_null() {
        return std::ptr::null_mut();
    }

    // Namespaced by this module's load address so two copies of the library in one process never collide.
    let class_name = CString::new(format!("VelopackAssociationTarget_{:x}", create_target as *const () as usize)).unwrap();
    let cls = objc_allocateClassPair(superclass, class_name.as_ptr(), 0);
    if cls.is_null() {
        return std::ptr::null_mut();
    }

    let event_handler_types = b"v@:@@\0".as_ptr() as *const c_char;
    let notification_types = b"v@:@\0".as_ptr() as *const c_char;
    let get_url: extern "C" fn(id, SEL, id, id) = handle_get_url_event;
    let open_documents: extern "C" fn(id, SEL, id, id) = handle_open_documents_event;
    let will_finish_launching: extern "C" fn(id, SEL, id) = application_will_finish_launching;
    let added = class_addMethod(cls, sel(SEL_GET_URL), std::mem::transmute(get_url), event_handler_types) != 0
        && class_addMethod(cls, sel(SEL_OPEN_DOCUMENTS), std::mem::transmute(open_documents), event_handler_types) != 0
        && class_addMethod(
            cls,
            sel(SEL_WILL_FINISH_LAUNCHING),
            std::mem::transmute(will_finish_launching),
            notification_types,
        ) != 0;
    objc_registerClassPair(cls);
    if !added {
        return std::ptr::null_mut();
    }

    let target = msg_send_id(msg_send_id(cls, sel(b"alloc\0")), sel(b"init\0"));
    if target.is_null() {
        return std::ptr::null_mut();
    }

    // AppKit replaces the handlers in finishLaunching; this notification is posted right after it has done so.
    let center = msg_send_id(class(b"NSNotificationCenter\0"), sel(b"defaultCenter\0"));
    let name = nsstring("NSApplicationWillFinishLaunchingNotification");
    if !center.is_null() && !name.is_null() {
        msg_send_add_observer(
            center,
            sel(b"addObserver:selector:name:object:\0"),
            target,
            sel(SEL_WILL_FINISH_LAUNCHING),
            name,
            std::ptr::null_mut(),
        );
    }

    target
}

unsafe fn install_event_handlers(target: id) {
    let (has_file, has_url) = {
        let h = hooks();
        (h.file.is_some(), h.url.is_some())
    };

    let manager = msg_send_id(class(b"NSAppleEventManager\0"), sel(b"sharedAppleEventManager\0"));
    if manager.is_null() {
        warn!("VelopackApp: NSAppleEventManager is unavailable, association hooks will not be invoked.");
        return;
    }

    let set_handler = sel(b"setEventHandler:andSelector:forEventClass:andEventID:\0");
    if has_url {
        msg_send_set_event_handler(manager, set_handler, target, sel(SEL_GET_URL), K_INTERNET_EVENT_CLASS, K_AE_GET_URL);
    }
    if has_file {
        msg_send_set_event_handler(
            manager,
            set_handler,
            target,
            sel(SEL_OPEN_DOCUMENTS),
            K_CORE_EVENT_CLASS,
            K_AE_OPEN_DOCUMENTS,
        );
    }
    debug!("VelopackApp: Installed Apple Event handlers (files: {}, urls: {}).", has_file, has_url);
}

fn invoke(hook: &AssociationHook, what: &str, value: String) {
    // the value (a path or URL, which may carry tokens) is kept out of the persistent log
    info!("VelopackApp: {} association activated ({} chars).", what, value.len());
    if catch_unwind(AssertUnwindSafe(|| hook(value))).is_err() {
        error!("VelopackApp: The {} association hook panicked.", what);
    }
}

extern "C" fn application_will_finish_launching(this: id, _sel: SEL, _notification: id) {
    unsafe { install_event_handlers(this) };
}

extern "C" fn handle_get_url_event(_this: id, _sel: SEL, event: id, _reply: id) {
    let Some(hook) = hooks().url.clone() else { return };
    unsafe {
        let pool = objc_autoreleasePoolPush();
        let direct_object = msg_send_id_u32(event, sel(b"paramDescriptorForKeyword:\0"), KEY_DIRECT_OBJECT);
        let url = nsstring_to_string(msg_send_id(direct_object, sel(b"stringValue\0")));
        objc_autoreleasePoolPop(pool);
        match url {
            Some(url) if !url.is_empty() => invoke(&hook, "URL", url),
            _ => warn!("VelopackApp: Received a GetURL Apple Event without a URL."),
        }
    }
}

extern "C" fn handle_open_documents_event(_this: id, _sel: SEL, event: id, _reply: id) {
    let Some(hook) = hooks().file.clone() else { return };
    let paths = unsafe {
        let pool = objc_autoreleasePoolPush();
        let direct_object = msg_send_id_u32(event, sel(b"paramDescriptorForKeyword:\0"), KEY_DIRECT_OBJECT);
        let mut paths = Vec::new();
        if !direct_object.is_null() {
            // The direct object is normally a list of file descriptors, but tolerate a single bare descriptor.
            let count = msg_send_isize(direct_object, sel(b"numberOfItems\0"));
            if count <= 0 {
                paths.extend(descriptor_to_path(direct_object));
            }
            for index in 1..=count.max(0) {
                let item = msg_send_id_isize(direct_object, sel(b"descriptorAtIndex:\0"), index);
                paths.extend(descriptor_to_path(item));
            }
        }
        objc_autoreleasePoolPop(pool);
        paths
    };
    if paths.is_empty() {
        warn!("VelopackApp: Received an OpenDocuments Apple Event without any file.");
    }
    for path in paths {
        invoke(&hook, "File", path);
    }
}

/// Converts one item of an OpenDocuments list (an alias, bookmark or file URL descriptor) to a POSIX path.
unsafe fn descriptor_to_path(descriptor: id) -> Option<String> {
    if descriptor.is_null() {
        return None;
    }
    // -fileURLValue (10.11+) coerces to typeFileURL and returns an NSURL, whatever the original descriptor type.
    let file_url_value = sel(b"fileURLValue\0");
    if msg_send_bool_sel(descriptor, sel(b"respondsToSelector:\0"), file_url_value) != 0 {
        let url = msg_send_id(descriptor, file_url_value);
        if let Some(path) = nsstring_to_string(msg_send_id(url, sel(b"path\0"))) {
            return Some(path);
        }
    }
    // Fallback: the textual form, converting a file:// URL to a path when that is what we got.
    let text = nsstring_to_string(msg_send_id(descriptor, sel(b"stringValue\0")))?;
    if text.starts_with("file://") {
        if let Some(path) = url::Url::parse(&text).ok().and_then(|u| u.to_file_path().ok()) {
            return Some(path.to_string_lossy().into_owned());
        }
    }
    (!text.is_empty()).then_some(text)
}
