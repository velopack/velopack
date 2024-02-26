#![allow(unused_variables)]
use semver;
use std::process::Command;

#[cfg(target_os = "windows")]
extern crate winres;

fn main() {
    #[cfg(target_os = "windows")]
    link_locksmith();

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
