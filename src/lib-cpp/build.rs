fn main() {
    cxx_build::bridge("src/lib.rs")
        .file("src/bridge.cc")
        .warnings_into_errors(true)
        .flag_if_supported("/std:c++17")
        .flag_if_supported("/EHsc") // exception unwind handling
        .flag_if_supported("-Wno-unused-function") // allow unused functions
        .define("VELOPACK_LIBC_EXPORTS", Some("1"))
        .std("c++17")
        .compile("velopack_libc");

    println!("cargo:rerun-if-changed=include/Velopack.h");
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/bridge.hpp");
    println!("cargo:rerun-if-changed=src/bridge.cc");

    #[cfg(target_os = "windows")]
    println!("cargo:rustc-link-arg=/WHOLEARCHIVE:velopack_libc.lib");
}