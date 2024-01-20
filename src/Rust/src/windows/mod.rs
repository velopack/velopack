pub mod download;
pub mod prerequisite;
pub mod runtimes;
pub mod splash;
pub mod os_info;

mod self_delete;
mod shortcuts;
mod util;

pub use self_delete::*;
pub use shortcuts::*;
pub use util::*;
