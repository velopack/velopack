#![allow(unused_variables)]
use semver;
use std::process::Command;

#[cfg(target_os = "windows")]
extern crate winres;

fn main() {
    #[cfg(target_os = "windows")]
    link_locksmith();

    #[cfg(target_os = "windows")]
    delay_load();

    let ver_output = Command::new("nbgv").args(&["get-version", "-v", "NuGetPackageVersion"]).output().expect("Failed to execute nbgv get-version");
    let version = String::from_utf8(ver_output.stdout).expect("Unable to convert ngbv output to string");
    let version = version.trim();
    let ver = semver::Version::parse(&version).expect("Unable to parse ngbv output as semver version");
    let ver: u64 = ver.major << 48 | ver.minor << 32 | ver.patch << 16;
    let desc = format!("Velopack {}", version);

    println!("cargo:rustc-env=NGBV_VERSION={}", version);

    #[cfg(target_os = "windows")]
    let _ = winres::WindowsResource::new()
        .set_manifest_file("app.manifest")
        .set_version_info(winres::VersionInfo::PRODUCTVERSION, ver)
        .set_version_info(winres::VersionInfo::FILEVERSION, ver)
        .set("CompanyName", "Velopack")
        .set("ProductName", "Velopack")
        .set("ProductVersion", version)
        .set("FileDescription", &desc)
        .set("LegalCopyright", "Caelan Sayler (c) 2023")
        .compile()
        .unwrap();
}

#[cfg(target_os = "windows")]
fn delay_load() {
    delay_load_exe("update");
    delay_load_exe("setup");
    println!("cargo:rustc-link-arg=/DEPENDENTLOADFLAG:0x800");
    println!("cargo:rustc-link-arg=/WX");
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
    let delay_load_dlls = ["propsys", "secur32", "crypt32", "bcrypt", "comctl32"];
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
}

#[cfg(target_os = "windows")]
fn link_locksmith() {
    use core::panic;
    let arch = std::env::var("CARGO_CFG_TARGET_ARCH").unwrap();
    if arch == "x86_64" {
        println!("cargo:rustc-link-lib=UpdateLocksmith_x64");
    } else if arch == "x86" {
        println!("cargo:rustc-link-lib=UpdateLocksmith_x86");
    } else {
        panic!("Unsupported architecture: {}", arch);
    }
}
