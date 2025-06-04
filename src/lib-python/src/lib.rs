use pyo3::prelude::*;
use pyo3::types::PyModule;

mod types;
use types::*;

mod exceptions;
use exceptions::VelopackError;

mod app;
use app::VelopackAppWrapper;

mod manager;
use manager::UpdateManagerWrapper;

#[pymodule]
fn velopack(m: &Bound<'_, PyModule>) -> PyResult<()> {
    m.add_class::<VelopackError>()?;

    // auto-generated DTO's
    m.add_class::<PyVelopackAsset>()?;
    m.add_class::<PyUpdateInfo>()?;
    m.add_class::<PyUpdateOptions>()?;
    m.add_class::<PyVelopackLocatorConfig>()?;

    // concrete classes
    m.add_class::<VelopackAppWrapper>()?;
    m.add_class::<UpdateManagerWrapper>()?;

    // add __version__ attribute
    m.add("__version__", env!("CARGO_PKG_VERSION"))?;

    // add __author__ attribute
    m.add("__author__", env!("CARGO_PKG_AUTHORS"))?;

    Ok(())
}
