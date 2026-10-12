//! macOS file association / URL protocol helper.
//!
//! `vpk pack` ships this binary as a nested app bundle (`{Main}.app/Contents/Helpers/{Name}.app`) whose
//! Info.plist owns the `CFBundleURLTypes` / `CFBundleDocumentTypes` declarations, so Launch Services sends
//! every activation here instead of to the main app. The helper owns its own `NSApplication` (so installing
//! the delegate never conflicts with the main app's UI framework), collects the opened URLs / files, and
//! launches the main app with the same `--veloapp-urlproto <url>` / `--veloapp-fileassoc <path>` argv pairs
//! that the Windows registry command uses. It never parses or validates the values; the libraries do.

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
    use block2::RcBlock;
    use log::{error, info, warn};
    use objc2::rc::Retained;
    use objc2::runtime::{ProtocolObject, Sel};
    use objc2::{available, define_class, msg_send, sel, DefinedClass, MainThreadMarker, MainThreadOnly};
    use objc2_app_kit::{NSApplication, NSApplicationDelegate, NSRunningApplication, NSWorkspace, NSWorkspaceOpenConfiguration};
    use objc2_foundation::{NSArray, NSError, NSNotification, NSObject, NSObjectNSDelayedPerforming, NSObjectProtocol, NSString, NSURL};
    use std::cell::{Cell, RefCell};
    use std::path::{Path, PathBuf};
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;
    use std::time::{Duration, Instant};
    use velopack::locator::LocationContext;
    use velopack::logging::{default_logfile_path, init_logging};

    /// Coalesces a burst of `openURLs:` calls into one launch of the main app.
    const FLUSH_DELAY: f64 = 0.25;
    /// How often the idle check runs.
    const IDLE_INTERVAL: f64 = 0.5;
    /// A helper that received nothing by then was started by a stray / aborted activation.
    const NO_ACTIVATION_TIMEOUT: Duration = Duration::from_secs(5);
    /// The helper never lives longer than this, whatever is still in flight.
    const HARD_TIMEOUT: Duration = Duration::from_secs(30);

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

        let mtm = MainThreadMarker::new().expect("the association helper must start on the main thread");
        let app = NSApplication::sharedApplication(mtm);
        let delegate = Delegate::new(mtm, main_app);
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
        /// Flattened `--veloapp-*` argv pairs waiting for the next flush.
        pending: RefCell<Vec<String>>,
        flush_scheduled: Cell<bool>,
        /// `NSWorkspace` launches whose completion handler has not run yet.
        in_flight: Arc<AtomicUsize>,
        /// True once the main app was launched at least once.
        launched: Cell<bool>,
        started: Instant,
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
                // Never flushes: a flush with zero items is a no-op, so a late openURLs: can not cause a
                // second, plain launch of the main app.
                self.schedule(sel!(velopackIdleCheck), IDLE_INTERVAL);
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
                self.ivars().pending.borrow_mut().extend(activation_args(&items));
                self.schedule_flush();
            }
        }

        impl Delegate {
            #[unsafe(method(velopackFlush))]
            fn flush(&self) {
                let ivars = self.ivars();
                ivars.flush_scheduled.set(false);
                let args = std::mem::take(&mut *ivars.pending.borrow_mut());
                if args.is_empty() {
                    return;
                }
                ivars.launched.set(true);
                launch_main(&ivars.main_app, args, ivars.in_flight.clone());
            }

            #[unsafe(method(velopackIdleCheck))]
            fn idle_check(&self) {
                let ivars = self.ivars();
                let elapsed = ivars.started.elapsed();
                let idle = ivars.pending.borrow().is_empty()
                    && !ivars.flush_scheduled.get()
                    && ivars.in_flight.load(Ordering::SeqCst) == 0;
                let done = if elapsed > HARD_TIMEOUT {
                    warn!("Association helper exceeded {:?}, exiting.", HARD_TIMEOUT);
                    true
                } else if idle && ivars.launched.get() {
                    info!("Association helper is idle, exiting.");
                    true
                } else if idle && elapsed > NO_ACTIVATION_TIMEOUT {
                    warn!("No activation arrived within {:?}, exiting without launching the app.", NO_ACTIVATION_TIMEOUT);
                    true
                } else {
                    false
                };
                if done {
                    NSApplication::sharedApplication(self.mtm()).terminate(None);
                } else {
                    self.schedule(sel!(velopackIdleCheck), IDLE_INTERVAL);
                }
            }
        }
    );

    impl Delegate {
        fn new(mtm: MainThreadMarker, main_app: PathBuf) -> Retained<Self> {
            let this = Self::alloc(mtm).set_ivars(Ivars {
                main_app,
                pending: RefCell::new(Vec::new()),
                flush_scheduled: Cell::new(false),
                in_flight: Arc::new(AtomicUsize::new(0)),
                launched: Cell::new(false),
                started: Instant::now(),
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

        fn schedule_flush(&self) {
            if !self.ivars().flush_scheduled.replace(true) {
                self.schedule(sel!(velopackFlush), FLUSH_DELAY);
            }
        }
    }

    /// Launches the main app with `args` (never empty) as a new instance, so it is its own TCC-responsible
    /// process and the arguments are not dropped when another instance is already running.
    fn launch_main(main_app: &Path, args: Vec<String>, in_flight: Arc<AtomicUsize>) {
        if !available!(macos = 10.15) {
            launch_with_open(main_app, &args);
            return;
        }
        let ns_args: Vec<Retained<NSString>> = args.iter().map(|a| NSString::from_str(a)).collect();
        let config = NSWorkspaceOpenConfiguration::configuration();
        config.setCreatesNewApplicationInstance(true);
        config.setArguments(&NSArray::from_retained_slice(&ns_args));
        config.setActivates(true);
        let url = NSURL::fileURLWithPath(&NSString::from_str(&main_app.to_string_lossy()));

        in_flight.fetch_add(1, Ordering::SeqCst);
        let main_app = main_app.to_path_buf();
        let in_flight = in_flight.clone();
        // May run on any thread: it touches nothing but its captures and the in_flight counter.
        let completion = RcBlock::new(move |_app: *mut NSRunningApplication, error: *mut NSError| {
            if error.is_null() {
                info!("Launched the app with {} argument(s) via NSWorkspace.", args.len());
            } else {
                // SAFETY: AppKit passes a valid NSError for the duration of the callback.
                let error = unsafe { &*error };
                warn!(
                    "NSWorkspace could not launch the app ({} {}), falling back to open.",
                    error.domain(),
                    error.code()
                );
                launch_with_open(&main_app, &args);
            }
            in_flight.fetch_sub(1, Ordering::SeqCst);
        });
        NSWorkspace::sharedWorkspace().openApplicationAtURL_configuration_completionHandler(&url, &config, Some(&completion));
    }

    /// Fallback for macOS 10.13 / 10.14 (no `NSWorkspaceOpenConfiguration`) or a failed `NSWorkspace` launch.
    fn launch_with_open(main_app: &Path, args: &[String]) {
        match std::process::Command::new("/usr/bin/open")
            .arg("-n")
            .arg(main_app)
            .arg("--args")
            .args(args)
            .spawn()
        {
            Ok(_) => info!("Launched the app with {} argument(s) via open.", args.len()),
            Err(e) => error!("Failed to launch the app via open ({}).", e),
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
