mod apply;
pub use apply::*;

mod start;
pub use start::*;

#[cfg(target_os = "linux")]
mod apply_linux_impl;
#[cfg(target_os = "macos")]
mod apply_osx_impl;
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
