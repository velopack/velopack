use pyo3_stub_gen::Result;

fn main() -> Result<()> {
    let stub = velopack_python::stub_info()?;
    stub.generate()?;
    Ok(())
}
