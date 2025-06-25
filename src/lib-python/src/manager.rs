use anyhow::Result;
use pyo3::prelude::*;
use std::sync::mpsc;
use std::thread;

use velopack::sources::AutoSource;
use velopack::{UpdateCheck, UpdateInfo, UpdateManager as VelopackUpdateManagerRust};

use crate::types::*;

#[pyclass(name = "UpdateManager")]
pub struct UpdateManagerWrapper {
    inner: VelopackUpdateManagerRust,
}

#[pymethods]
impl UpdateManagerWrapper {
    #[new]
    #[pyo3(signature = (source, options = None, locator = None))]
    pub fn new(source: String, options: Option<PyUpdateOptions>, locator: Option<PyVelopackLocatorConfig>) -> Result<Self> {
        let source = AutoSource::new(&source);
        // set myinner to a new VelopackUpdateManager with the source
        let inner = VelopackUpdateManagerRust::new(source, options.map(Into::into), locator.map(Into::into))?;
        Ok(UpdateManagerWrapper { inner })
    }

    pub fn check_for_updates(&mut self) -> Result<Option<PyUpdateInfo>> {
        let update_check = self.inner.check_for_updates()?;
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
    pub fn download_updates(&mut self, update_info: &PyUpdateInfo, progress_callback: Option<PyObject>) -> Result<()> {
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
            let result = self.inner.download_updates(&rust_update_info, Some(sender))?;

            // Wait for the progress thread to finish
            let _ = progress_thread.join();
            Ok(result)
        } else {
            // No progress callback provided
            self.inner.download_updates(&rust_update_info, None)?;
            Ok(())
        }
    }

    pub fn apply_updates_and_restart(&mut self, update_info: &PyUpdateInfo) -> Result<()> {
        // Convert PyUpdateInfo back to rust UpdateInfo
        let rust_update_info: UpdateInfo = update_info.clone().into();
        self.inner.apply_updates_and_restart(&rust_update_info)?;
        Ok(())
    }

    pub fn apply_updates_and_restart_with_args(&mut self, update_info: &PyUpdateInfo, restart_args: Vec<String>) -> Result<()> {
        // Convert PyUpdateInfo back to rust UpdateInfo
        let rust_update_info: UpdateInfo = update_info.clone().into();
        self.inner.apply_updates_and_restart_with_args(&rust_update_info, restart_args)?;
        Ok(())
    }

    pub fn apply_updates_and_exit(&mut self, update_info: &PyUpdateInfo) -> Result<()> {
        // Convert PyUpdateInfo back to rust UpdateInfo
        let rust_update_info: UpdateInfo = update_info.clone().into();
        self.inner.apply_updates_and_exit(&rust_update_info)?;
        Ok(())
    }

    #[pyo3(signature = (update_info, silent = false, restart = true, restart_args = None))]
    pub fn wait_exit_then_apply_updates(
        &mut self,
        update_info: &PyUpdateInfo,
        silent: bool,
        restart: bool,
        restart_args: Option<Vec<String>>,
    ) -> Result<()> {
        // Convert PyUpdateInfo back to rust UpdateInfo
        let rust_update_info: UpdateInfo = update_info.clone().into();
        
        // Convert restart_args to the format expected by the Rust function
        let args = restart_args.unwrap_or_default();
        
        self.inner.wait_exit_then_apply_updates(&rust_update_info, silent, restart, args)?;
        Ok(())
    }
}
