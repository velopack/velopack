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
//! When `VelopackApp::run` is about to restart the process to apply a pending update, the launch event has not
//! been delivered yet (no run loop has run), so `drain_launch_events` briefly takes over `NSApplication` itself:
//! it finishes launching, pumps events until the launch event (`oapp` / `odoc` / `GURL`) has arrived and returns
//! the collected values instead of invoking the hooks, so they can be forwarded to the restarted app.
//!
//! This talks to the Objective-C runtime through a few hand-declared FFI entry points; only Foundation is linked
//! (the notification name is matched by string value). AppKit is loaded at runtime, only by `drain_launch_events`.

#![allow(non_camel_case_types)]

use std::ffi::{c_char, c_void, CStr, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use crate::app::{Association, AssociationHook};
use crate::constants::{HOOK_CLI_FILE_ASSOC, HOOK_CLI_URL_PROTO};

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

const APPKIT_PATH: &[u8] = b"/System/Library/Frameworks/AppKit.framework/AppKit\0";

const fn fourcc(code: &[u8; 4]) -> u32 {
    ((code[0] as u32) << 24) | ((code[1] as u32) << 16) | ((code[2] as u32) << 8) | (code[3] as u32)
}

const K_INTERNET_EVENT_CLASS: u32 = fourcc(b"GURL");
const K_AE_GET_URL: u32 = fourcc(b"GURL");
const K_CORE_EVENT_CLASS: u32 = fourcc(b"aevt");
const K_AE_OPEN_DOCUMENTS: u32 = fourcc(b"odoc");
const K_AE_OPEN_APPLICATION: u32 = fourcc(b"oapp");
const K_AE_REOPEN_APPLICATION: u32 = fourcc(b"rapp");
const KEY_DIRECT_OBJECT: u32 = fourcc(b"----");
/// `NSEventMaskAny`
const NS_EVENT_MASK_ANY: usize = usize::MAX;
/// How long each `nextEventMatchingMask:` call blocks while draining; the loop checks for the launch event in between.
const DRAIN_PUMP_INTERVAL_SECS: f64 = 0.05;

const SEL_GET_URL: &[u8] = b"velopackHandleGetURLEvent:withReplyEvent:\0";
const SEL_OPEN_DOCUMENTS: &[u8] = b"velopackHandleOpenDocumentsEvent:withReplyEvent:\0";
const SEL_OPEN_APPLICATION: &[u8] = b"velopackHandleOpenApplicationEvent:withReplyEvent:\0";
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
/// `Some` while `drain_launch_events` runs: received values are buffered here instead of being passed to the hooks.
static COLLECTING: Mutex<Option<Vec<Association>>> = Mutex::new(None);
/// Set once any launch event (`oapp`, `rapp`, `odoc`, `GURL`) has been received while collecting.
static LAUNCH_SEEN: AtomicBool = AtomicBool::new(false);

fn hooks() -> std::sync::MutexGuard<'static, Hooks> {
    HOOKS.lock().unwrap_or_else(|e| e.into_inner())
}

fn collecting() -> std::sync::MutexGuard<'static, Option<Vec<Association>>> {
    COLLECTING.lock().unwrap_or_else(|e| e.into_inner())
}

/// Registers the given hooks and installs the Apple Event handlers that feed them. Safe to call more than once;
/// the latest hooks win. Must be called on the thread that will run the Cocoa run loop (normally the main thread).
pub fn install(file_hook: Option<AssociationHook>, url_hook: Option<AssociationHook>) {
    {
        let mut h = hooks();
        h.file = file_hook;
        h.url = url_hook;
    }

    let Some(target) = target() else { return };
    unsafe { install_event_handlers(target) };
}

/// Pumps the Cocoa event loop until the launch Apple Event has been delivered (or `timeout` elapses) and returns
/// the files / URLs it asked us to open, as `[flag, value]` associations, without invoking the hooks. Only for use
/// right before the process exits to apply an update: it finishes launching `NSApplication` on behalf of the app.
/// Requires `install` to have been called first with the hooks whose events should be collected, and must run on
/// the main thread (AppKit aborts the process with `NSInternalInconsistencyException` otherwise); on any other
/// thread it logs a warning and returns nothing.
///
/// Known limitation: if the update is then not applied after all (the apply failed), the process carries on with
/// `NSApplication` already finish-launched and our `oapp` / `rapp` handlers still installed; the caller delivers
/// the collected values to the hooks itself. The process is deliberately not relaunched to undo this, since that
/// could loop forever on a persistent apply failure.
pub fn drain_launch_events(timeout: Duration) -> Vec<Association> {
    if unsafe { libc::pthread_main_np() } == 0 {
        warn!("VelopackApp: Not on the main thread, a launch Apple Event cannot be collected before updating.");
        return Vec::new();
    }
    let Some(target) = target() else { return Vec::new() };

    LAUNCH_SEEN.store(false, Ordering::SeqCst);
    *collecting() = Some(Vec::new());
    let started = Instant::now();

    unsafe {
        if libc::dlopen(APPKIT_PATH.as_ptr() as *const c_char, libc::RTLD_LAZY).is_null() {
            warn!("VelopackApp: Unable to load AppKit, a launch Apple Event cannot be collected before updating.");
            return collecting().take().unwrap_or_default();
        }

        let pool = objc_autoreleasePoolPush();
        let app = msg_send_id(class(b"NSApplication\0"), sel(b"sharedApplication\0"));
        if app.is_null() {
            warn!("VelopackApp: NSApplication is unavailable, a launch Apple Event cannot be collected before updating.");
            objc_autoreleasePoolPop(pool);
            return collecting().take().unwrap_or_default();
        }

        // Collect mode also claims the plain open-application event, so a launch without anything to open ends the
        // wait too. Install before and after finishLaunching: AppKit replaces the handlers inside it (and our
        // WillFinishLaunching observer reinstalls them), but a framework may have already finished launching.
        install_event_handlers(target);
        msg_send_void(app, sel(b"finishLaunching\0"));
        install_event_handlers(target);

        let mode = nsstring("kCFRunLoopDefaultMode"); // NSDefaultRunLoopMode, compared by value
        let next_event = sel(b"nextEventMatchingMask:untilDate:inMode:dequeue:\0");
        let send_event = sel(b"sendEvent:\0");
        let until_date = sel(b"dateWithTimeIntervalSinceNow:\0");
        let ns_date = class(b"NSDate\0");
        while !LAUNCH_SEEN.load(Ordering::SeqCst) && started.elapsed() < timeout {
            let inner = objc_autoreleasePoolPush();
            // Apple Events are dispatched (AEProcessAppleEvent) from inside nextEventMatchingMask:, which is also
            // how NSApplication's own run loop receives them.
            let until = msg_send_id_f64(ns_date, until_date, DRAIN_PUMP_INTERVAL_SECS);
            let event = msg_send_next_event(app, next_event, NS_EVENT_MASK_ANY, until, mode, 1);
            if !event.is_null() {
                msg_send_void_id(app, send_event, event);
            }
            objc_autoreleasePoolPop(inner);
        }
        objc_autoreleasePoolPop(pool);
    }

    let collected = collecting().take().unwrap_or_default();
    if LAUNCH_SEEN.load(Ordering::SeqCst) {
        info!(
            "VelopackApp: Launch Apple Event received after {:?}, {} value(s) collected.",
            started.elapsed(),
            collected.len()
        );
    } else {
        warn!("VelopackApp: No launch Apple Event was received within {:?}.", timeout);
    }
    collected
}

fn target() -> Option<id> {
    let target = TARGET.get_or_init(|| Target(unsafe { create_target() }));
    if target.0.is_null() {
        warn!("VelopackApp: Unable to create the Apple Event target object, association hooks will not be invoked.");
        return None;
    }
    Some(target.0)
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
msg_send_fn!(msg_send_void, () -> ());
msg_send_fn!(msg_send_isize, () -> isize);
msg_send_fn!(msg_send_utf8, () -> *const c_char);
msg_send_fn!(msg_send_id_u32, (a: u32) -> id);
msg_send_fn!(msg_send_id_isize, (a: isize) -> id);
msg_send_fn!(msg_send_id_f64, (a: f64) -> id);
msg_send_fn!(msg_send_id_cstr, (a: *const c_char) -> id);
msg_send_fn!(msg_send_void_id, (a: id) -> ());
msg_send_fn!(msg_send_bool_sel, (a: SEL) -> BOOL);
msg_send_fn!(msg_send_set_event_handler, (target: id, selector: SEL, event_class: u32, event_id: u32) -> ());
msg_send_fn!(msg_send_add_observer, (observer: id, selector: SEL, name: id, object: id) -> ());
msg_send_fn!(msg_send_next_event, (mask: usize, until: id, mode: id, dequeue: BOOL) -> id);

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
    let open_application: extern "C" fn(id, SEL, id, id) = handle_open_application_event;
    let will_finish_launching: extern "C" fn(id, SEL, id) = application_will_finish_launching;
    let added = class_addMethod(cls, sel(SEL_GET_URL), std::mem::transmute(get_url), event_handler_types) != 0
        && class_addMethod(cls, sel(SEL_OPEN_DOCUMENTS), std::mem::transmute(open_documents), event_handler_types) != 0
        && class_addMethod(cls, sel(SEL_OPEN_APPLICATION), std::mem::transmute(open_application), event_handler_types) != 0
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
    let is_collecting = collecting().is_some();

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
    if is_collecting {
        // only while draining: AppKit's own open/reopen handling is left alone for the app's lifetime
        for event_id in [K_AE_OPEN_APPLICATION, K_AE_REOPEN_APPLICATION] {
            msg_send_set_event_handler(manager, set_handler, target, sel(SEL_OPEN_APPLICATION), K_CORE_EVENT_CLASS, event_id);
        }
    }
    debug!(
        "VelopackApp: Installed Apple Event handlers (files: {}, urls: {}, collecting: {}).",
        has_file, has_url, is_collecting
    );
}

/// Passes a received value to its hook, or buffers it while `drain_launch_events` is collecting.
fn deliver(flag: &'static str, hook: &AssociationHook, value: String) {
    let what = if flag == HOOK_CLI_FILE_ASSOC {
        "File"
    } else {
        "URL"
    };
    // the value (a path or URL, which may carry tokens) is kept out of the persistent log
    info!("VelopackApp: {} association activated ({} chars).", what, value.len());
    if let Some(buffer) = collecting().as_mut() {
        buffer.push((flag, value));
        LAUNCH_SEEN.store(true, Ordering::SeqCst);
        return;
    }
    if catch_unwind(AssertUnwindSafe(|| hook(value))).is_err() {
        error!("VelopackApp: The {} association hook panicked.", what);
    }
}

extern "C" fn application_will_finish_launching(this: id, _sel: SEL, _notification: id) {
    unsafe { install_event_handlers(this) };
}

extern "C" fn handle_open_application_event(_this: id, _sel: SEL, _event: id, _reply: id) {
    debug!("VelopackApp: Received the open-application Apple Event.");
    LAUNCH_SEEN.store(true, Ordering::SeqCst);
}

extern "C" fn handle_get_url_event(_this: id, _sel: SEL, event: id, _reply: id) {
    let Some(hook) = hooks().url.clone() else { return };
    unsafe {
        let pool = objc_autoreleasePoolPush();
        let direct_object = msg_send_id_u32(event, sel(b"paramDescriptorForKeyword:\0"), KEY_DIRECT_OBJECT);
        let url = nsstring_to_string(msg_send_id(direct_object, sel(b"stringValue\0")));
        objc_autoreleasePoolPop(pool);
        match url {
            Some(url) if !url.is_empty() => deliver(HOOK_CLI_URL_PROTO, &hook, url),
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
        deliver(HOOK_CLI_FILE_ASSOC, &hook, path);
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
