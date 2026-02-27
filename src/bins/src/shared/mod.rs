pub mod cli_host;
pub mod fastzip;
pub mod localization;
pub mod progress;
pub mod runtime_arch;

mod dialogs_common;
mod dialogs_const;

pub mod dialogs {
    pub use super::dialogs_common::*;
    pub use super::dialogs_const::*;
}

mod util_common;
pub use util_common::*;

#[cfg(target_os = "windows")]
mod util_windows;
#[cfg(target_os = "windows")]
pub use util_windows::*;

#[cfg(target_os = "macos")]
mod util_osx;
#[cfg(target_os = "macos")]
pub use util_osx::*;

#[cfg(target_os = "linux")]
mod util_linux;
#[cfg(target_os = "linux")]
pub use util_linux::*;
