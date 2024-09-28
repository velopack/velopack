use std::path::Path;

fn main() {
    let manifest_dir = std::env::var("CARGO_MANIFEST_DIR").unwrap();
    let releases_dir = Path::new(&manifest_dir).join("releases");
    println!("cargo:rustc-env=RELEASES_DIR={}", releases_dir.to_string_lossy());
}