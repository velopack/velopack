use anyhow::Result;
use pyo3::prelude::*;
use std::sync::mpsc;
use std::thread;

use velopack::{UpdateCheck, UpdateInfo, UpdateManager as VelopackUpdateManagerRust, VelopackAsset};

use crate::{sources::PySourceArg, types::*, PyUpdateInfoOrAsset};

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "UpdateManager")]
pub struct UpdateManagerWrapper {
    inner: VelopackUpdateManagerRust,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
#[pymethods]
impl UpdateManagerWrapper {
    #[new]
    #[pyo3(signature = (source, options = None, locator = None))]
    pub fn new(source: PySourceArg, options: Option<PyUpdateOptions>, locator: Option<PyVelopackLocatorConfig>) -> Result<Self> {
        let source = source.into_source();
        let inner = VelopackUpdateManagerRust::new_boxed(source, options.map(Into::into), locator.map(Into::into))?;
        Ok(UpdateManagerWrapper { inner })
    }

    pub fn get_current_version(&self) -> String {
        self.inner.get_current_version_as_string()
    }

    pub fn get_app_id(&self) -> String {
        self.inner.get_app_id().to_string()
    }

    pub fn get_is_portable(&self) -> bool {
        self.inner.get_is_portable()
    }

    pub fn get_update_pending_restart(&self) -> Option<PyVelopackAsset> {
        let pending = self.inner.get_update_pending_restart();
        pending.map(Into::into)
    }

    pub fn check_for_updates(&mut self, py: Python) -> Result<Option<PyUpdateInfo>> {
        // Release GIL during network operation
        let update_check = py.detach(|| self.inner.check_for_updates())?;
        match update_check {
            UpdateCheck::UpdateAvailable(updates) => {
                let py_updates = PyUpdateInfo::from(*updates);
                Ok(Some(py_updates))
            }
            UpdateCheck::NoUpdateAvailable => Ok(None),
            UpdateCheck::RemoteIsEmpty => Ok(None),
        }
    }

    #[pyo3(signature = (update_info, progress_callback = None))]
    pub fn download_updates(&mut self, py: Python, update_info: PyUpdateInfo, progress_callback: Option<Py<PyAny>>) -> Result<()> {
        let rust_update_info: UpdateInfo = update_info.into();
        let inner = self.inner.clone();

        let (sender, receiver) = if progress_callback.is_some() {
            let (s, r) = mpsc::channel::<i16>();
            (Some(s), Some(r))
        } else {
            (None, None)
        };

        py.detach(|| {
            let handle = thread::spawn(move || inner.download_updates(&rust_update_info, sender));

            if let (Some(callback), Some(receiver)) = (progress_callback, receiver) {
                while let Ok(progress) = receiver.recv() {
                    Python::try_attach(|py| {
                        if let Err(e) = callback.call1(py, (progress,)) {
                            eprintln!("Progress callback error: {}", e);
                        }
                    });
                }
            }

            handle
                .join()
                .unwrap_or_else(|_| Err(std::io::Error::other("Download thread panicked").into()))
        })?;

        Ok(())
    }

    pub fn apply_updates_and_restart(&mut self, update: PyUpdateInfoOrAsset) -> Result<()> {
        let asset: VelopackAsset = update.into_asset();
        self.inner.apply_updates_and_restart(&asset)?;
        Ok(())
    }

    pub fn apply_updates_and_restart_with_args(&mut self, update: PyUpdateInfoOrAsset, restart_args: Vec<String>) -> Result<()> {
        let asset: VelopackAsset = update.into_asset();
        self.inner.apply_updates_and_restart_with_args(&asset, restart_args)?;
        Ok(())
    }

    pub fn apply_updates_and_exit(&mut self, update: PyUpdateInfoOrAsset) -> Result<()> {
        let asset: VelopackAsset = update.into_asset();
        self.inner.apply_updates_and_exit(&asset)?;
        Ok(())
    }

    #[pyo3(signature = (update, silent = false, restart = true, restart_args = None))]
    pub fn wait_exit_then_apply_updates(
        &mut self,
        update: PyUpdateInfoOrAsset,
        silent: bool,
        restart: bool,
        restart_args: Option<Vec<String>>,
    ) -> Result<()> {
        let asset: VelopackAsset = update.into_asset();
        let args = restart_args.unwrap_or_default();
        self.inner.wait_exit_then_apply_updates(&asset, silent, restart, args)?;
        Ok(())
    }
}
