mod apply;
pub use apply::*;

mod start;
pub use start::*;

mod patch;
pub use patch::*;

mod assemble;
pub use assemble::*;

mod apply_staged;
pub use apply_staged::*;

#[cfg(target_os = "linux")]
mod apply_linux_impl;
#[cfg(target_os = "macos")]
mod apply_osx_impl;
#[cfg(target_os = "windows")]
pub mod apply_staged_windows_impl;
#[cfg(target_os = "windows")]
mod apply_windows_impl;

#[cfg(target_os = "windows")]
mod start_windows_impl;

#[cfg(target_os = "windows")]
mod install;
#[cfg(target_os = "windows")]
pub use install::*;

#[cfg(target_os = "windows")]
mod uninstall;
#[cfg(target_os = "windows")]
pub use uninstall::*;
