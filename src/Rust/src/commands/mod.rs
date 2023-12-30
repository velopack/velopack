mod apply;
pub use apply::*;

mod patch;
pub use patch::*;

#[cfg(target_os = "windows")]
mod start;
#[cfg(target_os = "windows")]
pub use start::*;

#[cfg(target_os = "windows")]
mod uninstall;
#[cfg(target_os = "windows")]
pub use uninstall::*;
