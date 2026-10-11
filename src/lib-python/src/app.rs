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
    /// On Windows the path arrives as `--veloapp-fileassoc <path>` on the command line and the callback runs
    /// during run(), after the regular startup logic. On macOS files arrive as Apple Events, which requires a
    /// Cocoa (NSApplication) run loop: the callback may then run at any time, on the main thread, and replaces the
    /// UI framework's own open-file handling. The launch is never lost to an update: when run() restarts the app to
    /// apply a pending update the file(s) are forwarded to the new version, which invokes the callback once per
    /// file (on macOS run() first waits up to about 2 seconds for the launch Apple Event, only in that case).
    /// Not supported on Linux.
    pub fn on_file_assoc(mut slf: PyRefMut<Self>, callback: Py<PyAny>) -> PyRefMut<Self> {
        slf.file_assoc_hook = Some(callback);
        slf
    }

    /// This hook is triggered when the operating system opens this app to handle a URL whose scheme was
    /// registered with `vpk pack --urlProtocols`. The callback receives the full URL (eg. `myapp://open?id=1`).
    /// On Windows the URL arrives as `--veloapp-urlproto <url>` on the command line and the callback runs
    /// during run(), after the regular startup logic. On macOS URLs arrive as Apple Events, which requires a
    /// Cocoa (NSApplication) run loop: the callback may then run at any time, on the main thread, and replaces the
    /// UI framework's own open-URL handling. The launch is never lost to an update: when run() restarts the app to
    /// apply a pending update the URL is forwarded to the new version, which invokes the callback (on macOS run()
    /// first waits up to about 2 seconds for the launch Apple Event, only in that case). Not supported on Linux.
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

        // Association hooks must be 'static (on macOS they are invoked later, from the Cocoa run loop), so they
        // get their own reference to the Python callable.
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
