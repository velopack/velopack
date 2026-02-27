pub mod commands;
pub mod shared;
#[cfg(target_os = "windows")]
pub mod windows;

pub use velopack_dialogs as dialogs;

#[macro_use]
extern crate log;
#[macro_use]
extern crate lazy_static;
