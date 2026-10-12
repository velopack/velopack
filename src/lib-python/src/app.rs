use pyo3::prelude::*;

use velopack::VelopackApp as VelopackAppRust;

/// Python wrapper for VelopackApp with builder pattern
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "App")]
pub struct VelopackAppWrapper {
    // We'll store the callbacks as Python objects (any callable)
    install_hook: Option<Py<PyAny>>,
    update_hook: Option<Py<PyAny>>,
    obsolete_hook: Option<Py<PyAny>>,
    uninstall_hook: Option<Py<PyAny>>,
    firstrun_hook: Option<Py<PyAny>>,
    restarted_hook: Option<Py<PyAny>>,
    file_assoc_hook: Option<Py<PyAny>>,
    url_proto_hook: Option<Py<PyAny>>,
    auto_apply: bool,
    args: Option<Vec<String>>,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
#[pymethods]
impl VelopackAppWrapper {
    /// Create a new VelopackApp builder
    #[new]
    pub fn new() -> Self {
        VelopackAppWrapper {
            install_hook: None,
            update_hook: None,
            obsolete_hook: None,
            uninstall_hook: None,
            firstrun_hook: None,
            restarted_hook: None,
            file_assoc_hook: None,
            url_proto_hook: None,
            auto_apply: true,
            args: None,
        }
    }

    /// Override the command line arguments used by VelopackApp
    pub fn set_args(mut slf: PyRefMut<Self>, args: Vec<String>) -> PyRefMut<Self> {
        slf.args = Some(args);
        slf
    }

    /// Set whether to automatically apply downloaded updates on startup
    pub fn set_auto_apply_on_startup(mut slf: PyRefMut<Self>, apply: bool) -> PyRefMut<Self> {
        slf.auto_apply = apply;
        slf
    }

    /// This hook is triggered when the application is started for the first time after installation
    pub fn on_first_run(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.firstrun_hook = Some(callback);
        slf
    }

    /// This hook is triggered when the application is restarted by Velopack after installing updates
    pub fn on_restarted(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.restarted_hook = Some(callback);
        slf
    }

    /// Fast callback hook for after installation (Windows only)
    pub fn on_after_install_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.install_hook = Some(callback);
        slf
    }

    /// Fast callback hook for after update (Windows only)
    pub fn on_after_update_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.update_hook = Some(callback);
        slf
    }

    /// Fast callback hook for before update (Windows only)
    pub fn on_before_update_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.obsolete_hook = Some(callback);
        slf
    }

    /// Fast callback hook for before uninstall (Windows only)
    pub fn on_before_uninstall_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.uninstall_hook = Some(callback);
        slf
    }

    /// This hook is triggered when the operating system opens this app to handle a file whose extension was
    /// registered with `vpk pack --fileAssociations`. The callback receives the full path of the file to open.
    ///
    /// On Windows and macOS the path arrives as `--veloapp-fileassoc <path>` on the command line (on macOS the
    /// association helper bundle receives the open-file event and launches the app with it). With the default
    /// `NewInstance` launch mode the callback runs during run(), on the calling thread, after the regular startup
    /// logic (on_first_run/on_restarted); the app then continues starting normally. The launch is never lost to an
    /// update: when run() restarts the app to apply a pending update the file(s) are forwarded to the new version,
    /// which invokes the callback once per file.
    ///
    /// With `vpk pack --associationLaunchMode SingleInstance`, a launch while an instance of the app is already
    /// running hands the path to that instance and the launching process exits with code 0 in native code, inside
    /// run(), before any update is applied (like the fast-exit hooks, `atexit` handlers do not run). In the
    /// receiving instance the callback runs on a background thread (a Velopack IPC thread, holding the GIL;
    /// invocations are serialized; possibly while the main thread is still inside run(), even before
    /// on_first_run/on_restarted, or at any later time), so it must be thread-safe and marshal to the UI thread
    /// itself. Return promptly (post the work to the UI thread rather than blocking on it): the launching process
    /// waits at most 5 seconds for the callback to return, after which it handles the activation itself as a second
    /// instance. The running instance is not brought to the foreground by the OS, so activate your main window from
    /// the callback. If no instance answers within about 5 seconds, the launching process handles the activation
    /// itself, as in `NewInstance` mode. During interpreter shutdown the callback is skipped.
    ///
    /// macOS: the open-file consent (TCC) lands on the helper, not on this process. The first access to a file in a
    /// protected location (Desktop, Documents, Downloads, removable volumes) triggers the "Files and Folders"
    /// prompt, or fails with EPERM if the user denied it earlier; add the `NS*FolderUsageDescription` strings to
    /// your Info.plist. Associations require macOS 10.13+. Not supported on Linux.
    pub fn on_file_assoc(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.file_assoc_hook = Some(callback);
        slf
    }

    /// This hook is triggered when the operating system opens this app to handle a URL whose scheme was
    /// registered with `vpk pack --urlProtocols`. The callback receives the full URL (eg. `myapp://open?id=1`).
    ///
    /// On Windows and macOS the URL arrives as `--veloapp-urlproto <url>` on the command line (on macOS the
    /// association helper bundle receives the open-URL event and launches the app with it). With the default
    /// `NewInstance` launch mode the callback runs during run(), on the calling thread, after the regular startup
    /// logic (on_first_run/on_restarted); the app then continues starting normally. The launch is never lost to an
    /// update: when run() restarts the app to apply a pending update the URL is forwarded to the new version, which
    /// invokes the callback.
    ///
    /// With `vpk pack --associationLaunchMode SingleInstance`, a launch while an instance of the app is already
    /// running hands the URL to that instance and the launching process exits with code 0 in native code, inside
    /// run(), before any update is applied (like the fast-exit hooks, `atexit` handlers do not run). In the
    /// receiving instance the callback runs on a background thread (a Velopack IPC thread, holding the GIL;
    /// invocations are serialized; possibly while the main thread is still inside run(), even before
    /// on_first_run/on_restarted, or at any later time), so it must be thread-safe and marshal to the UI thread
    /// itself. Return promptly (post the work to the UI thread rather than blocking on it): the launching process
    /// waits at most 5 seconds for the callback to return, after which it handles the activation itself as a second
    /// instance. The running instance is not brought to the foreground by the OS, so activate your main window from
    /// the callback. If no instance answers within about 5 seconds, the launching process handles the activation
    /// itself, as in `NewInstance` mode. During interpreter shutdown the callback is skipped.
    ///
    /// Associations require macOS 10.13+. Not supported on Linux.
    pub fn on_url_proto(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.url_proto_hook = Some(callback);
        slf
    }

    /// Runs the Velopack startup logic
    pub fn run(&mut self, py: Python) -> PyResult<()> {
        // Create the Rust VelopackApp with our stored configuration
        let mut app = VelopackAppRust::build().set_auto_apply_on_startup(self.auto_apply);

        // Set args if provided
        if let Some(ref args) = self.args {
            app = app.set_args(args.clone());
        }

        // Association hooks must be Send + Sync + 'static (in SingleInstance mode they are invoked later, from the
        // Velopack IPC thread), so they get their own reference to the Python callable. `try_attach` acquires the
        // GIL from any thread and yields None during interpreter shutdown, in which case the callback is skipped.
        if let Some(ref hook) = self.file_assoc_hook {
            let hook = hook.clone_ref(py);
            app = app.on_file_assoc(move |path| {
                Python::try_attach(|py| {
                    if let Err(e) = hook.call1(py, (path,)) {
                        eprintln!("Error calling file_assoc hook: {:?}", e);
                    }
                });
            });
        }

        if let Some(ref hook) = self.url_proto_hook {
            let hook = hook.clone_ref(py);
            app = app.on_url_proto(move |url| {
                Python::try_attach(|py| {
                    if let Err(e) = hook.call1(py, (url,)) {
                        eprintln!("Error calling url_proto hook: {:?}", e);
                    }
                });
            });
        }

        // Set up hooks - we need to convert Python callbacks to Rust closures
        if let Some(ref hook) = self.firstrun_hook {
            let hook_clone = hook;
            app = app.on_first_run(move |version| {
                Python::try_attach(|py| {
                    let version_str = version.to_string();
                    if let Err(e) = hook_clone.call1(py, (version_str,)) {
                        eprintln!("Error calling first_run hook: {:?}", e);
                    }
                });
            });
        }

        if let Some(ref hook) = self.restarted_hook {
            let hook_clone = hook;
            app = app.on_restarted(move |version| {
                Python::try_attach(|py| {
                    let version_str = version.to_string();
                    if let Err(e) = hook_clone.call1(py, (version_str,)) {
                        eprintln!("Error calling restarted hook: {:?}", e);
                    }
                });
            });
        }

        #[cfg(target_os = "windows")]
        {
            if let Some(ref hook) = self.install_hook {
                let hook_clone = hook;
                app = app.on_after_install_fast_callback(move |version| {
                    Python::try_attach(|py| {
                        let version_str = version.to_string();
                        if let Err(e) = hook_clone.call1(py, (version_str,)) {
                            eprintln!("Error calling install hook: {:?}", e);
                        }
                    });
                });
            }

            if let Some(ref hook) = self.update_hook {
                let hook_clone = hook;
                app = app.on_after_update_fast_callback(move |version| {
                    Python::try_attach(|py| {
                        let version_str = version.to_string();
                        if let Err(e) = hook_clone.call1(py, (version_str,)) {
                            eprintln!("Error calling update hook: {:?}", e);
                        }
                    });
                });
            }

            if let Some(ref hook) = self.obsolete_hook {
                let hook_clone = hook;
                app = app.on_before_update_fast_callback(move |version| {
                    Python::try_attach(|py| {
                        let version_str = version.to_string();
                        if let Err(e) = hook_clone.call1(py, (version_str,)) {
                            eprintln!("Error calling obsolete hook: {:?}", e);
                        }
                    });
                });
            }

            if let Some(ref hook) = self.uninstall_hook {
                let hook_clone = hook;
                app = app.on_before_uninstall_fast_callback(move |version| {
                    Python::try_attach(|py| {
                        let version_str = version.to_string();
                        if let Err(e) = hook_clone.call1(py, (version_str,)) {
                            eprintln!("Error calling uninstall hook: {:?}", e);
                        }
                    });
                });
            }
        }

        // do not Release the GIL before calling the potentially blocking run method
        app.run();

        Ok(())
    }
}
