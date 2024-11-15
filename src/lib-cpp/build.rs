extern crate cbindgen;

use std::env;

fn main() {
    let crate_dir = env::var("CARGO_MANIFEST_DIR").unwrap();
    cbindgen::Builder::new()
      .with_crate(crate_dir)
      .with_documentation(true)
      .with_language(cbindgen::Language::C)
      .with_autogen_warning("// === THIS FILE IS AUTO-GENERATED - DO NOT EDIT ===")
      .with_include_guard("VELOPACK_H")
      .with_cpp_compat(true)
      .with_include_version(true)
      .generate()
      .expect("Unable to generate bindings")
      .write_to_file("include/Velopack.h");
}