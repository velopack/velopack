#[macro_use]
extern crate log;

pub mod backends;
mod dialogs;
pub mod locale;
mod types;

pub mod progress;

pub use backends::{DialogManager, DialogProxy, XDialogError, XDialogIcon, XDialogOptions, XDialogResult};
pub use dialogs::*;
pub use types::*;

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, OnceLock};

static SILENT: AtomicBool = AtomicBool::new(false);
static DIALOG_MANAGER: OnceLock<Mutex<Box<dyn DialogManager + Send>>> = OnceLock::new();

pub fn set_silent(silent: bool) {
    SILENT.store(silent, Ordering::Relaxed);
}

pub fn get_silent() -> bool {
    SILENT.load(Ordering::Relaxed)
}

/// Initialize the localization system and dialog backend. Call once at startup.
pub fn init() {
    locale::init_localization();
    #[cfg(windows)]
    DIALOG_MANAGER.get_or_init(|| Mutex::new(Box::new(backends::taskdialog::TaskDialogManager::new())));
}

pub(crate) fn with_manager<F, R>(f: F) -> Result<R, XDialogError>
where
    F: FnOnce(&mut (dyn DialogManager + Send)) -> Result<R, XDialogError>,
{
    let mgr = DIALOG_MANAGER.get().ok_or(XDialogError::NotInitialized)?;
    let mut guard = mgr.lock().unwrap_or_else(|e| e.into_inner());
    f(&mut **guard)
}
