pub mod download;
pub mod prerequisite;
pub mod runtimes;

mod self_delete;
mod shortcuts;
pub mod splash;
mod util;

pub use self_delete::*;
pub use shortcuts::*;
pub use util::*;
