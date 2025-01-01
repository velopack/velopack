fn main() {
    uniffi::generate_scaffolding("src/interface.udl").unwrap();
}