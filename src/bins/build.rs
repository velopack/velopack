#![allow(unused_variables)]

use std::{env, path::Path};

fn main() {
    let version = env!("CARGO_PKG_VERSION");
    let ver = semver::Version::parse(&version).expect("Unable to parse ngbv output as semver version");
    let ver: u64 = ver.major << 48 | ver.minor << 32 | ver.patch << 16;
    let desc = format!("Velopack {}", version);
    println!("cargo:rustc-env=NGBV_VERSION={}", version);

    let target_os = env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    if target_os == "windows" {
        delay_load();
        link_manifest_msvc(Path::new("app.manifest"));
    }
}

fn link_manifest_msvc(manifest_path: &Path) {
    println!("cargo:rustc-link-arg-bins=/MANIFEST:EMBED");
    println!(
        "cargo:rustc-link-arg-bins=/MANIFESTINPUT:{}",
        manifest_path.canonicalize().unwrap().display()
    );
    println!("cargo:rustc-link-arg-bins=/MANIFESTUAC:NO");
}


fn delay_load() {
    let features = env::var("CARGO_CFG_TARGET_FEATURE").unwrap_or_default();
    if features.contains("crt-static") {
        delay_load_exe("update");
        delay_load_exe("setup");
        delay_load_exe("stub");
        println!("cargo:rustc-link-arg=/DEPENDENTLOADFLAG:0x800");
        println!("cargo:rustc-link-arg=/WX");
        println!("cargo:rustc-link-arg=/IGNORE:4099"); // PDB was not found
        println!("cargo:rustc-link-arg=/IGNORE:4199"); // delayload ignored, no imports found
    }
}

// https://github.com/rust-lang/rustup/blob/master/build.rs#L45
fn delay_load_exe(bin_name: &str) {
    // Only search system32 for DLLs
    // This applies to DLLs loaded at load time. However, this setting is ignored
    // before Windows 10 RS1 (aka 1601).
    // https://learn.microsoft.com/en-us/cpp/build/reference/dependentloadflag?view=msvc-170
    println!("cargo:rustc-link-arg-bin={bin_name}=/DEPENDENTLOADFLAG:0x800");

    // Delay load dlls that are not "known DLLs"[1].
    // Known DLLs are always loaded from the system directory whereas other DLLs
    // are loaded from the application directory. By delay loading the latter
    // we can ensure they are instead loaded from the system directory.
    // [1]: https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order#factors-that-affect-searching
    //
    // This will work on all supported Windows versions but it relies on
    // us using `SetDefaultDllDirectories` before any libraries are loaded.
    let delay_load_dlls =
        ["gdi32", "advapi32", "shell32", "ole32", "psapi", "propsys", "secur32", "crypt32", "ws2_32", "oleaut32", "bcrypt", "comctl32"];
    for dll in delay_load_dlls {
        println!("cargo:rustc-link-arg-bin={bin_name}=/delayload:{dll}.dll");
    }

    // When using delayload, it's necessary to also link delayimp.lib
    // https://learn.microsoft.com/en-us/cpp/build/reference/dependentloadflag?view=msvc-170
    println!("cargo:rustc-link-arg-bin={bin_name}=delayimp.lib");

    // Turn linker warnings into errors
    // Rust hides linker warnings meaning mistakes may go unnoticed.
    // Turning them into errors forces them to be displayed (and the build to fail).
    // If we do want to ignore specific warnings then `/IGNORE:` should be used.
    println!("cargo:rustc-link-arg-bin={bin_name}=/WX");
    println!("cargo:rustc-link-arg-bin={bin_name}=/IGNORE:4099"); // PDB was not found
    println!("cargo:rustc-link-arg-bin={bin_name}=/IGNORE:4199"); // delayload ignored, no imports found
}
