mod apply;
pub use apply::*;

mod start;
pub use start::*;

#[cfg(target_os = "windows")]
mod uninstall;
#[cfg(target_os = "windows")]
pub use uninstall::*;
