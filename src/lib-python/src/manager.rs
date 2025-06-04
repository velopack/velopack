use pyo3::prelude::*;
use std::sync::mpsc;
use std::thread;

use velopack::sources::AutoSource;
use velopack::{UpdateCheck, UpdateInfo, UpdateManager as VelopackUpdateManagerRust};

use crate::exceptions::VelopackError;
use crate::types::*;

#[pyclass(name = "UpdateManager")]
pub struct UpdateManagerWrapper {
    inner: VelopackUpdateManagerRust,
}

#[pymethods]
impl UpdateManagerWrapper {
    // for new, just take in a string, which is the source
    #[new]
    #[pyo3(signature = (source, options = None, locator = None))]
    pub fn new(source: String, options: Option<PyUpdateOptions>, locator: Option<PyVelopackLocatorConfig>) -> PyResult<Self> {
        let source = AutoSource::new(&source);
        // set myinner to a new VelopackUpdateManager with the source
        let inner = VelopackUpdateManagerRust::new(source, options.map(Into::into), locator.map(Into::into))
            .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to create UpdateManager: {}", e)))?;
        Ok(UpdateManagerWrapper { inner })
    }

    // check_for_updates return update info indicating if updates are available
    /// This method checks for updates and returns update info if updates are available, None otherwise.
    pub fn check_for_updates(&mut self) -> PyResult<Option<PyUpdateInfo>> {
        let update_check =
            self.inner.check_for_updates().map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to check for updates: {}", e)))?;

        match update_check {
            UpdateCheck::UpdateAvailable(updates) => {
                let py_updates = PyUpdateInfo::from(updates);
                Ok(Some(py_updates))
            }
            UpdateCheck::NoUpdateAvailable => Ok(None),
            UpdateCheck::RemoteIsEmpty => Ok(None),
        }
    }

    #[pyo3(signature = (update_info, progress_callback = None))]
    pub fn download_updates(&mut self, update_info: &PyUpdateInfo, progress_callback: Option<PyObject>) -> PyResult<()> {
        // Convert PyUpdateInfo back to rust UpdateInfo
        let rust_update_info: UpdateInfo = update_info.clone().into();

        if let Some(callback) = progress_callback {
            // Create a channel for progress updates
            let (sender, receiver) = mpsc::channel::<i16>();

            // Spawn a thread to handle progress updates
            let progress_thread = thread::spawn(move || {
                Python::with_gil(|py| {
                    while let Ok(progress) = receiver.recv() {
                        if let Err(e) = callback.call1(py, (progress,)) {
                            // Log error but continue - don't break the download
                            eprintln!("Progress callback error: {}", e);
                            break;
                        }
                    }
                });
            });

            // Call download with the sender
            let result = self
                .inner
                .download_updates(&rust_update_info, Some(sender))
                .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to download updates: {}", e)));

            // Wait for the progress thread to finish
            let _ = progress_thread.join();

            result.map(|_| ())
        } else {
            // No progress callback provided
            self.inner
                .download_updates(&rust_update_info, None)
                .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to download updates: {}", e)))
                .map(|_| ())
        }
    }

    pub fn apply_updates_and_restart(&mut self, update_info: &PyUpdateInfo) -> PyResult<()> {
        // Convert PyUpdateInfo back to rust UpdateInfo
        let rust_update_info: UpdateInfo = update_info.clone().into();

        self.inner
            .apply_updates_and_restart(&rust_update_info)
            .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to apply updates and restart: {}", e)))
            .map(|_| ())
    }
}
