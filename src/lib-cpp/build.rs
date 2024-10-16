fn main() {
    cxx_build::bridge("src/lib.rs")
        .file("src/bridge.cc")
        .flag_if_supported("/std:c++17")
        .std("c++17")
        .compile("velopack_libc");

    println!("cargo:rerun-if-changed=include/Velopack.h");
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/bridge.hpp");
    println!("cargo:rerun-if-changed=src/bridge.cc");
}