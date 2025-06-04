use pyo3::prelude::*;
use pyo3::types::PyCFunction;

use velopack::VelopackApp as VelopackAppRust;

/// Python wrapper for VelopackApp with builder pattern
#[pyclass(name = "App")]
pub struct VelopackAppWrapper {
    // We'll store the callbacks as Python objects
    install_hook: Option<Py<PyCFunction>>,
    update_hook: Option<Py<PyCFunction>>,
    obsolete_hook: Option<Py<PyCFunction>>,
    uninstall_hook: Option<Py<PyCFunction>>,
    firstrun_hook: Option<Py<PyCFunction>>,
    restarted_hook: Option<Py<PyCFunction>>,
    auto_apply: bool,
    args: Option<Vec<String>>,
}

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
    pub fn on_first_run(mut slf: PyRefMut<Self>, callback: Py<PyCFunction>) -> PyRefMut<Self> {
        slf.firstrun_hook = Some(callback);
        slf
    }

    /// This hook is triggered when the application is restarted by Velopack after installing updates
    pub fn on_restarted(mut slf: PyRefMut<Self>, callback: Py<PyCFunction>) -> PyRefMut<Self> {
        slf.restarted_hook = Some(callback);
        slf
    }

    /// Fast callback hook for after installation (Windows only)
    pub fn on_after_install_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyCFunction>) -> PyRefMut<Self> {
        slf.install_hook = Some(callback);
        slf
    }

    /// Fast callback hook for after update (Windows only)  
    pub fn on_after_update_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyCFunction>) -> PyRefMut<Self> {
        slf.update_hook = Some(callback);
        slf
    }

    /// Fast callback hook for before update (Windows only)
    pub fn on_before_update_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyCFunction>) -> PyRefMut<Self> {
        slf.obsolete_hook = Some(callback);
        slf
    }

    /// Fast callback hook for before uninstall (Windows only)
    pub fn on_before_uninstall_fast_callback(mut slf: PyRefMut<Self>, callback: Py<PyCFunction>) -> PyRefMut<Self> {
        slf.uninstall_hook = Some(callback);
        slf
    }

    /// Runs the Velopack startup logic
    pub fn run(&mut self, _py: Python) -> PyResult<()> {
        // Create the Rust VelopackApp with our stored configuration
        let mut app = VelopackAppRust::build().set_auto_apply_on_startup(self.auto_apply);

        // Set args if provided
        if let Some(ref args) = self.args {
            app = app.set_args(args.clone());
        }

        // Set up hooks - we need to convert Python callbacks to Rust closures
        if let Some(ref hook) = self.firstrun_hook {
            let hook_clone = hook;
            app = app.on_first_run(move |version| {
                Python::with_gil(|py| {
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
                Python::with_gil(|py| {
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
                    Python::with_gil(|py| {
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
                    Python::with_gil(|py| {
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
                    Python::with_gil(|py| {
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
                    Python::with_gil(|py| {
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
