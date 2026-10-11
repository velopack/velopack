#![allow(unused_variables)]

use std::env;

fn main() {
    #[cfg(target_os = "windows")]
    delay_load();

    let version = env!("CARGO_PKG_VERSION");
    let ver = semver::Version::parse(version).expect("Unable to parse ngbv output as semver version");

    println!("cargo:rustc-env=NGBV_VERSION={}", version);

    #[cfg(target_os = "windows")]
    version_resources(&ver);
}

/// Compiles a version resource for each binary, so per-binary fields like OriginalFilename can be set.
/// AppLocker publisher rules need the OriginalFilename of Update.exe (#729).
#[cfg(target_os = "windows")]
fn version_resources(ver: &semver::Version) {
    let out_dir = env::var("OUT_DIR").unwrap();
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").unwrap();

    // The manifest is embedded by the linker for every executable, including unit and integration test harnesses:
    // without its supportedOS entries Windows reports itself as 6.2, which fails the os_min_version checks.
    println!("cargo:rerun-if-changed=app.manifest");
    println!("cargo:rustc-link-arg=/MANIFEST:EMBED");
    println!("cargo:rustc-link-arg=/MANIFESTUAC:NO");
    println!("cargo:rustc-link-arg=/MANIFESTINPUT:{manifest_dir}\\app.manifest");

    for (bin, original_filename) in [
        ("update", "Update.exe"),
        ("setup", "Setup.exe"),
        ("stub", "stub.exe"),
        ("testapp", "testapp.exe"),
    ] {
        let rc = format!("{out_dir}/{bin}.rc");
        std::fs::write(&rc, version_rc(ver, original_filename)).unwrap();
        embed_resource::compile_for(rc, [bin], embed_resource::NONE).manifest_required().unwrap();
    }
}

#[cfg(target_os = "windows")]
fn version_rc(ver: &semver::Version, original_filename: &str) -> String {
    let version = ver.to_string();
    let (major, minor, patch) = (ver.major, ver.minor, ver.patch);
    let internal_name = original_filename.trim_end_matches(".exe");
    format!(
        r#"#pragma code_page(65001)
1 VERSIONINFO
FILEVERSION {major}, {minor}, {patch}, 0
PRODUCTVERSION {major}, {minor}, {patch}, 0
FILEOS 0x40004
FILETYPE 0x1
FILESUBTYPE 0x0
FILEFLAGSMASK 0x3f
FILEFLAGS 0x0
{{
BLOCK "StringFileInfo"
{{
BLOCK "000004b0"
{{
VALUE "CompanyName", "Velopack"
VALUE "FileDescription", "Velopack {version}"
VALUE "FileVersion", "{version}"
VALUE "InternalName", "{internal_name}"
VALUE "OriginalFilename", "{original_filename}"
VALUE "LegalCopyright", "Caelan Sayler (c) 2023, Velopack Ltd. (c) 2024"
VALUE "ProductName", "Velopack"
VALUE "ProductVersion", "{version}"
}}
}}
BLOCK "VarFileInfo" {{
VALUE "Translation", 0x0, 0x04b0
}}
}}
"#
    )
}

#[cfg(target_os = "windows")]
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
#[cfg(target_os = "windows")]
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
    let delay_load_dlls = [
        "gdi32", "advapi32", "shell32", "ole32", "psapi", "propsys", "secur32", "crypt32", "ws2_32", "oleaut32", "bcrypt", "comctl32",
    ];
    for dll in delay_load_dlls {
        println!("cargo:rustc-link-arg-bin={bin_name}=/delayload:{dll}.dll");
    }

    // combase.dll is Windows 8+ but windows-rs links COM functions from it; delay load it so Windows 7 can
    // start the binaries. src/windows/delay_load.rs substitutes ole32.dll when it is first needed.
    println!("cargo:rustc-link-arg-bin={bin_name}=/delayload:combase.dll");

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
