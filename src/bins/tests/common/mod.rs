use std::path::PathBuf;

/// The test-only chunk index writer, shared with the lib-rust tests.
#[allow(dead_code, unused_imports)]
#[path = "../../../lib-rust/tests/common/index_writer.rs"]
pub mod index_writer;

pub fn find_fixtures() -> PathBuf {
    let mut path = std::env::current_exe().unwrap();
    while !path.join("Velopack.slnx").exists() {
        path.pop();
    }
    path.push("test");
    path.push("fixtures");
    path
}
