use semver;

extern crate winres;
fn main() {
    cc::Build::new()
        .cpp(true)
        .file("src/platform/windows/shortcuts.cpp")
        .define("UNICODE", None)   
        .define("_UNICODE", None)  
        .compile("lib_shortcuts");
    println!("cargo:rerun-if-changed=src/platform/windows/shortcuts.cpp");

    let ver = env!("CARGO_PKG_VERSION");
    let ver = semver::Version::parse(&ver).unwrap();
    let ver: u64 = ver.major << 48 | ver.minor << 32 | ver.patch << 16;

    let _ = winres::WindowsResource::new()
        .set_manifest_file("app.manifest")
        .set_version_info(winres::VersionInfo::PRODUCTVERSION, ver)
        .set_version_info(winres::VersionInfo::FILEVERSION, ver)
        .set("CompanyName", "Clowd.Squirrel")
        .set("ProductName", "Clowd.Squirrel")
        .set("FileDescription", "Clowd.Squirrel")
        .set("LegalCopyright", "Caelan Sayler (c) 2023")
        .compile()
        .unwrap();
}