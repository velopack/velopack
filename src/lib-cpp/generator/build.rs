use flapigen::{CppConfig, CppOptional, CppStrView, CppVariant, LanguageConfig};
use std::{env, path::Path, process::Command};

fn main() {
    use rifgen::{Generator, Language, TypeCases};
    let source_folder = "./src";
    let out_file = "./src/cpp_glue.rs.in";
    Generator::new(TypeCases::CamelCase, Language::Cpp, source_folder).generate_interface(out_file);

    let cpp_cfg = CppConfig::new(Path::new("..").join("api"), "velopack_cpp".into())
        .cpp_optional(CppOptional::Std17)
        .cpp_variant(CppVariant::Std17)
        .cpp_str_view(CppStrView::Std17);

    let swig_gen = flapigen::Generator::new(LanguageConfig::CppConfig(cpp_cfg));
    swig_gen.expand("velopack_cpp", Path::new("./src/cpp_glue.rs.in"), &Path::new("src").join("cpp_glue.rs"));

    Command::new("rustfmt").arg("src/cpp_glue.rs").status().unwrap();
}
