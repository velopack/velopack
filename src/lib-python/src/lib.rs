use pyo3::prelude::*;
use pyo3::types::PyModule;

mod types;
use types::*;

mod app;
use app::VelopackAppWrapper;

mod manager;
use manager::UpdateManagerWrapper;

use ::velopack::VelopackAsset;

#[derive(FromPyObject)]
pub enum PyUpdateInfoOrAsset {
    UpdateInfo(PyUpdateInfo),
    Asset(PyVelopackAsset),
}

impl PyUpdateInfoOrAsset {
    pub fn into_asset(self) -> VelopackAsset {
        match self {
            PyUpdateInfoOrAsset::UpdateInfo(update_info) => update_info.TargetFullRelease.into(),
            PyUpdateInfoOrAsset::Asset(asset) => asset.into(),
        }
    }
}

#[pymodule]
#[pyo3(name = "velopack")]
fn velopack(m: &Bound<'_, PyModule>) -> PyResult<()> {
    pyo3_log::init();

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
