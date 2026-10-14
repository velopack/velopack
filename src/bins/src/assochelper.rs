//! macOS file association / URL protocol helper.
//!
//! `vpk pack` ships this binary as a nested app bundle (`{Main}.app/Contents/Helpers/{Name}.app`) whose
//! Info.plist owns the `CFBundleURLTypes` / `CFBundleDocumentTypes` declarations, so Launch Services sends
//! every activation here instead of to the main app. The helper owns its own `NSApplication` (so installing
//! the delegate never conflicts with the main app's UI framework) and collects the opened URLs / files. Each batch
//! goes straight to a running instance of the main app that forwards activations (over the library's IPC, which is
//! then activated with `open`); otherwise the main app is launched with the same `--veloapp-urlproto <url>` /
//! `--veloapp-fileassoc <path>` argv pairs that the Windows registry command uses. It never parses or validates the
//! values; the libraries do.

/// Flattens the activated items into `--veloapp-fileassoc <path>` / `--veloapp-urlproto <url>` argv pairs,
/// in arrival order, skipping empty values.
#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
fn activation_args(items: &[(bool, String)]) -> Vec<String> {
    let mut args = Vec::with_capacity(items.len() * 2);
    for (is_file, value) in items {
        if value.is_empty() {
            continue;
        }
        args.push(
            if *is_file {
                "--veloapp-fileassoc"
            } else {
                "--veloapp-urlproto"
            }
            .to_string(),
        );
        args.push(value.clone());
    }
    args
}

#[cfg(not(target_os = "macos"))]
fn main() {
    eprintln!("assochelper is only supported on macOS");
    std::process::exit(1);
}

#[cfg(target_os = "macos")]
fn main() {
    mac::main();
}

#[cfg(target_os = "macos")]
mod mac {
    use super::activation_args;
    use log::{error, info, warn};
    use objc2::rc::Retained;
    use objc2::runtime::{ProtocolObject, Sel};
    use objc2::{define_class, msg_send, sel, DefinedClass, MainThreadMarker, MainThreadOnly};
    use objc2_app_kit::{NSApplication, NSApplicationDelegate};
    use objc2_foundation::{NSArray, NSNotification, NSObject, NSObjectNSDelayedPerforming, NSObjectProtocol, NSURL};
    use std::cell::{Cell, RefCell};
    use std::path::{Path, PathBuf};
    use velopack::ipc::{self, IpcEndpoint, IpcItem, IpcMessage};
    use velopack::locator::{auto_locate_app_manifest, LocationContext};
    use velopack::logging::{default_logfile_path, init_logging};

    /// Coalesces a burst of `openURLs:` calls into one delivery; also how long the helper waits for more items
    /// after a delivery before it exits.
    const BATCH_DELAY: f64 = 0.25;
    /// A helper that received nothing by then was started by a stray / aborted activation.
    const NO_ACTIVATION_TIMEOUT: f64 = 5.0;

    pub fn main() {
        let main_app = match locate_main_app() {
            Ok(p) => p,
            Err(e) => {
                eprintln!("assochelper: {}", e);
                std::process::exit(1);
            }
        };
        let log_file = default_logfile_path(LocationContext::FromSpecifiedRootDir(main_app.clone(), None));
        init_logging("assoc", Some(&log_file), false, false, None);
        info!("Association helper started for {:?}", main_app);

        // the endpoint a running instance serves on, derived exactly like the library does in the main app
        let endpoint = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(main_app.clone(), None))
            .and_then(|locator| IpcEndpoint::for_app(&locator.get_manifest_id(), &locator.get_root_dir()));
        let endpoint = endpoint
            .map_err(|e| warn!("Activations can not be forwarded to a running instance: {}", e))
            .ok();

        let mtm = MainThreadMarker::new().expect("the association helper must start on the main thread");
        let app = NSApplication::sharedApplication(mtm);
        let delegate = Delegate::new(mtm, main_app, endpoint);
        app.setDelegate(Some(ProtocolObject::from_ref(&*delegate)));
        app.run();
    }

    /// exe -> MacOS -> Contents -> {Helper}.app -> Helpers -> Contents -> {Main}.app
    fn locate_main_app() -> Result<PathBuf, String> {
        let exe = std::env::current_exe().map_err(|e| format!("cannot determine the helper executable path ({})", e))?;
        let layout = || format!("{:?} is not inside {{Main}}.app/Contents/Helpers/{{Helper}}.app/Contents/MacOS", exe);
        let main_app = exe.ancestors().nth(6).ok_or_else(layout)?;
        let helpers_dir = exe.ancestors().nth(4).ok_or_else(layout)?;
        let is_app = main_app.extension().map(|e| e.eq_ignore_ascii_case("app")).unwrap_or(false);
        if !is_app || helpers_dir != main_app.join("Contents").join("Helpers") {
            return Err(layout());
        }
        Ok(main_app.to_path_buf())
    }

    struct Ivars {
        main_app: PathBuf,
        endpoint: Option<IpcEndpoint>,
        /// Flattened `--veloapp-*` argv pairs waiting for the next flush.
        pending: RefCell<Vec<String>>,
        flush_scheduled: Cell<bool>,
        received_any: Cell<bool>,
    }

    define_class!(
        // SAFETY: NSObject has no subclassing requirements and Delegate does not implement Drop.
        #[unsafe(super(NSObject))]
        #[thread_kind = MainThreadOnly]
        #[name = "VelopackAssocHelperDelegate"]
        #[ivars = Ivars]
        struct Delegate;

        unsafe impl NSObjectProtocol for Delegate {}

        unsafe impl NSApplicationDelegate for Delegate {
            #[unsafe(method(applicationDidFinishLaunching:))]
            fn did_finish_launching(&self, _notification: &NSNotification) {
                self.schedule(sel!(velopackNoActivationCheck), NO_ACTIVATION_TIMEOUT);
            }

            /// AppKit routes file opens here as well (and then never calls `application:openFile(s):`).
            #[unsafe(method(application:openURLs:))]
            fn open_urls(&self, _application: &NSApplication, urls: &NSArray<NSURL>) {
                let mut items = Vec::with_capacity(urls.len());
                for url in urls.to_vec() {
                    let is_file = url.isFileURL();
                    let value = if is_file { url.path() } else { url.absoluteString() };
                    items.push((is_file, value.map(|s| s.to_string()).unwrap_or_default()));
                }
                let files = items.iter().filter(|(is_file, v)| *is_file && !v.is_empty()).count();
                let urls = items.iter().filter(|(is_file, v)| !*is_file && !v.is_empty()).count();
                let lengths: Vec<usize> = items.iter().map(|(_, v)| v.chars().count()).collect();
                info!("Received {} file(s) and {} url(s), value lengths {:?}", files, urls, lengths);
                let ivars = self.ivars();
                ivars.received_any.set(true);
                ivars.pending.borrow_mut().extend(activation_args(&items));
                if !ivars.flush_scheduled.replace(true) {
                    self.schedule(sel!(velopackFlush), BATCH_DELAY);
                }
            }
        }

        impl Delegate {
            /// Delivers the batch (blocking the main thread; items arriving meanwhile wait in the event queue and
            /// start the next batch), then exits unless another item arrives within the next batch window.
            #[unsafe(method(velopackFlush))]
            fn flush(&self) {
                let ivars = self.ivars();
                ivars.flush_scheduled.set(false);
                let args = std::mem::take(&mut *ivars.pending.borrow_mut());
                if !args.is_empty() {
                    deliver(&ivars.main_app, ivars.endpoint.as_ref(), &args);
                }
                self.schedule(sel!(velopackExitCheck), BATCH_DELAY);
            }

            #[unsafe(method(velopackExitCheck))]
            fn exit_check(&self) {
                let ivars = self.ivars();
                if ivars.pending.borrow().is_empty() && !ivars.flush_scheduled.get() {
                    info!("Association helper is done, exiting.");
                    NSApplication::sharedApplication(self.mtm()).terminate(None);
                }
            }

            #[unsafe(method(velopackNoActivationCheck))]
            fn no_activation_check(&self) {
                if !self.ivars().received_any.get() {
                    warn!("No activation arrived within {}s, exiting without launching the app.", NO_ACTIVATION_TIMEOUT);
                    NSApplication::sharedApplication(self.mtm()).terminate(None);
                }
            }
        }
    );

    impl Delegate {
        fn new(mtm: MainThreadMarker, main_app: PathBuf, endpoint: Option<IpcEndpoint>) -> Retained<Self> {
            let this = Self::alloc(mtm).set_ivars(Ivars {
                main_app,
                endpoint,
                pending: RefCell::new(Vec::new()),
                flush_scheduled: Cell::new(false),
                received_any: Cell::new(false),
            });
            // SAFETY: NSObject's init takes no arguments and returns an initialized instance.
            unsafe { msg_send![super(this), init] }
        }

        /// Runs `selector` on this object from the main run loop after `delay` seconds.
        fn schedule(&self, selector: Sel, delay: f64) {
            let object: &NSObject = self;
            // SAFETY: the selectors used name argument-less methods defined on this class above.
            unsafe { object.performSelector_withObject_afterDelay(selector, None, delay) };
        }
    }

    /// Hands `args` (never empty) to a running instance that forwards activations and brings it to the front, or
    /// else launches a new instance with them (its own election then forwards them if an instance is starting up).
    fn deliver(main_app: &Path, endpoint: Option<&IpcEndpoint>, args: &[String]) {
        let count = args.len() / 2;
        let mut open = std::process::Command::new("/usr/bin/open");
        match endpoint.map(|ep| forward(ep, args)) {
            Some(Ok(())) => {
                info!("Forwarded {} activation(s) to the running app, activating it.", count);
                open.arg(main_app);
            }
            not_forwarded => {
                let reason = not_forwarded.and_then(Result::err).unwrap_or_else(|| "no IPC endpoint".into());
                info!("Launching the app with {} activation(s) ({}).", count, reason);
                open.arg("-n").arg(main_app).arg("--args").args(args);
            }
        }
        match open.status() {
            Ok(status) if status.success() => {}
            Ok(status) => error!("open exited with {}.", status),
            Err(e) => error!("Failed to run open ({}).", e),
        }
    }

    /// Sends one `association` message with all pairs of `args`; `Ok` only when the running instance acked it.
    fn forward(ep: &IpcEndpoint, args: &[String]) -> Result<(), String> {
        let mut msg = IpcMessage::new(ipc::TYPE_ASSOCIATION);
        msg.items = args
            .chunks(2)
            .map(|pair| IpcItem {
                flag: pair[0].clone(),
                value: pair[1].clone(),
            })
            .collect();
        match ipc::request(ep, &msg, ipc::EXCHANGE_TIMEOUT) {
            Ok(Some(reply)) if reply.is_ack() => Ok(()),
            Ok(Some(reply)) => Err(format!("the running app answered '{}'", reply.message.unwrap_or(reply.message_type))),
            Ok(None) => Err("no running instance forwards activations".into()),
            Err(e) => Err(e.to_string()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::activation_args;

    #[test]
    fn activation_args_keeps_arrival_order_and_kinds() {
        let items = vec![
            (false, "myapp://open?x=1".to_string()),
            (true, "/Users/me/Documents/a b.myext".to_string()),
            (false, "myapp://second".to_string()),
        ];
        assert_eq!(
            activation_args(&items),
            vec![
                "--veloapp-urlproto",
                "myapp://open?x=1",
                "--veloapp-fileassoc",
                "/Users/me/Documents/a b.myext",
                "--veloapp-urlproto",
                "myapp://second",
            ]
        );
    }

    #[test]
    fn activation_args_skips_empty_values() {
        let items = vec![(true, String::new()), (false, "myapp://x".to_string()), (false, String::new())];
        assert_eq!(activation_args(&items), vec!["--veloapp-urlproto", "myapp://x"]);
        assert!(activation_args(&[]).is_empty());
        assert!(activation_args(&[(true, String::new())]).is_empty());
    }
}
