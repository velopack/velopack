use std::sync::mpsc;
use std::thread;

use pyo3::prelude::*;

use velopack::{UpdateCheck, UpdateInfo, UpdateManager as VelopackUpdateManagerRust};
use velopack::sources::AutoSource;

use crate::exceptions::VelopackError;

#[pyclass(name = "UpdateManager")]
pub struct UpdateManagerWrapper {
    inner: VelopackUpdateManagerRust,
    updates: UpdateInfo,
}

#[pymethods]
impl UpdateManagerWrapper {
    // for new, just take in a string, which is the source
#[new]
pub fn new(source: String) -> PyResult<Self> {
    let source = AutoSource::new(&source);
    // set myinner to a new VelopackUpdateManager with the source
    let inner = VelopackUpdateManagerRust::new(source, None, None)
        .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to create UpdateManager: {}", e)))?;
    Ok(UpdateManagerWrapper {
        inner,
        updates: UpdateInfo::default(),
}
    )
}


// check_for_updates return a bool indicating if updates are available
    /// This method checks for updates and returns true if updates are available, false otherwise.
    pub fn check_for_updates(&mut self) -> PyResult<bool> {
        match self.inner.check_for_updates() {
            Ok(UpdateCheck::UpdateAvailable(updates)) => {
                self.updates = updates;
                Ok(true)
            }
            Ok(_) => {
                self.updates = UpdateInfo::default();
                Ok(false)
            }
            Err(e) => Err(PyErr::new::<VelopackError, _>(format!("Failed to check for updates: {}", e)))
        }
    }

    #[pyo3(signature = (progress_callback = None))]
    pub fn download_updates(&mut self, progress_callback: Option<PyObject>) -> PyResult<()> {
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
            let result = self.inner.download_updates(&self.updates, Some(sender))
                .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to download updates: {}", e)));
            
            // Wait for the progress thread to finish
            let _ = progress_thread.join();
            
            result.map(|_| ())
        } else {
            // No progress callback provided
            self.inner.download_updates(&self.updates, None)
                .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to download updates: {}", e)))
                .map(|_| ())
        }
    }

pub fn apply_updates_and_restart(&mut self) -> PyResult<()> {
    self.inner.apply_updates_and_restart(&self.updates)
        .map_err(|e| PyErr::new::<VelopackError, _>(format!("Failed to apply updates and restart: {}", e)))
        .map(|_| ())
}


}

