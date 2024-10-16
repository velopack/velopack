fn main() {
    cxx_build::bridge("src/lib.rs")
        // .file("src/blobstore.cc")
        .flag_if_supported("/std:c++17")
        .std("c++17")
        .compile("velopack_libc");

    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=include/Velopack.hpp");
    println!("cargo:rerun-if-changed=include/bridge.hpp");
}