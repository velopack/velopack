fn main() {
    cxx_build::bridge("src/lib.rs")
        .file("src/bridge.cc")
        .flag_if_supported("/std:c++17")
        .flag_if_supported("/DEF:src/lib.def")
        .flag_if_supported("/FORCE:UNRESOLVED")
        // .warnings_into_errors(true)
        .std("c++17")
        .compile("velopack_libc");

    println!("cargo:rerun-if-changed=include/Velopack.h");
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/bridge.hpp");
    println!("cargo:rerun-if-changed=src/bridge.cc");
    println!("cargo:rustc-link-arg=/WHOLEARCHIVE:velopack_libc.lib");
}