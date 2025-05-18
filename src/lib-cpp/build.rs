extern crate cbindgen;

use std::env;

fn main() {
    let target_os = env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let host_triple = env::var("HOST").unwrap_or_default();

    // Extract host OS from the host triple
    let host_os = if host_triple.contains("linux") {
        "linux"
    } else if host_triple.contains("windows") {
        "windows"
    } else if host_triple.contains("macos") || host_triple.contains("darwin") {
        "macos"
    } else {
        "unknown"
    };

    if host_os == target_os {
        let crate_dir = env::var("CARGO_MANIFEST_DIR").unwrap();
        cbindgen::Builder::new()
            .with_crate(crate_dir)
            .with_documentation(true)
            .with_language(cbindgen::Language::C)
            .with_autogen_warning("/* THIS FILE IS AUTO-GENERATED - DO NOT EDIT */")
            .with_include_guard("VELOPACK_H")
            .with_cpp_compat(true)
            .with_include_version(true)
            .generate()
            .expect("Unable to generate bindings")
            .write_to_file("include/Velopack.h");
    } else {
        println!("cargo:warning=Skipping cbindgen during cross-compilation (host OS: {}, target OS: {})", host_os, target_os);
    }
}
