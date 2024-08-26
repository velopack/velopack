use std::{env, path::Path};
use locator::VelopackLocator;
use ts_rs::TS;
use velopack::*;

fn main() {
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").unwrap();
    let bindings_dir = Path::new(&manifest_dir).join("..").join("..").join("src").join("bindings");
    UpdateInfo::export_all_to(&bindings_dir).unwrap();
    UpdateOptions::export_all_to(&bindings_dir).unwrap();
    VelopackLocator::export_all_to(&bindings_dir).unwrap();
}