#[macro_use]
extern crate log;

mod dialogs;
mod locale_constants;
mod locale_strings;
mod localization;
mod types;

pub mod progress;

pub use dialogs::*;
pub use types::*;

// Re-export XDialogBuilder for entry point run_result() pattern
pub use xdialog::XDialogBuilder;

#[cfg(windows)]
pub use xdialog::init_win32_direct;

/// Initialize the localization system. Call once at startup.
pub fn init() {
    localization::init_localization();
}
