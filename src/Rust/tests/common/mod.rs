use std::path::PathBuf;

pub fn find_fixtures() -> PathBuf {
    let mut path = std::env::current_exe().unwrap();
    while !path.join("Velopack.sln").exists() {
        path.pop();
    }
    path.push("test");
    path.push("fixtures");
    path
}