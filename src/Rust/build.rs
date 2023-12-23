use semver;
use std::process::Command;

extern crate winres;
fn main() {
    let ver_output = Command::new("nbgv").args(&["get-version", "-v", "NuGetPackageVersion"]).output().expect("Failed to execute nbgv get-version");
    let version = String::from_utf8(ver_output.stdout).expect("Unable to convert ngbv output to string");
    let version = version.trim();
    let ver = semver::Version::parse(&version).expect("Unable to parse ngbv output as semver version");
    let ver: u64 = ver.major << 48 | ver.minor << 32 | ver.patch << 16;
    let desc = format!("Clowd.Squirrel {}", version);

    println!("cargo:rustc-env=NGBV_VERSION={}", version);

    let _ = winres::WindowsResource::new()
        .set_manifest_file("app.manifest")
        .set_version_info(winres::VersionInfo::PRODUCTVERSION, ver)
        .set_version_info(winres::VersionInfo::FILEVERSION, ver)
        .set("CompanyName", "Clowd.Squirrel")
        .set("ProductName", "Clowd.Squirrel")
        .set("ProductVersion", version)
        .set("FileDescription", &desc)
        .set("LegalCopyright", "Caelan Sayler (c) 2023")
        .compile()
        .unwrap();
}
