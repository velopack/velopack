use pyo3::prelude::*;
use pyo3::types::PyModule;

mod types;
use types::*;

mod app;
use app::VelopackAppWrapper;

mod manager;
use manager::UpdateManagerWrapper;

mod sources;
use sources::{PyGiteaSource, PyGithubSource, PyGitlabSource, PyHttpSource};

use ::velopack::VelopackAsset;

#[derive(FromPyObject)]
#[allow(clippy::large_enum_variant)]
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

// FromPyObject unions aren't pyclasses, so stub-gen can't derive their Python type. Describe it explicitly.
#[cfg(feature = "stub-gen")]
pyo3_stub_gen::impl_stub_type!(PyUpdateInfoOrAsset = PyUpdateInfo | PyVelopackAsset);

#[pymodule]
#[pyo3(name = "velopack")]
fn velopack(m: &Bound<'_, PyModule>) -> PyResult<()> {
    pyo3_log::init();

    // auto-generated DTO's
    m.add_class::<PyVelopackAsset>()?;
    m.add_class::<PyUpdateInfo>()?;
    m.add_class::<PyUpdateOptions>()?;
    m.add_class::<PyVelopackLocatorConfig>()?;

    // update sources
    m.add_class::<PyGithubSource>()?;
    m.add_class::<PyGitlabSource>()?;
    m.add_class::<PyGiteaSource>()?;
    m.add_class::<PyHttpSource>()?;

    // concrete classes
    m.add_class::<VelopackAppWrapper>()?;
    m.add_class::<UpdateManagerWrapper>()?;

    // add __version__ attribute
    m.add("__version__", env!("CARGO_PKG_VERSION"))?;

    // add __author__ attribute
    m.add("__author__", env!("CARGO_PKG_AUTHORS"))?;

    Ok(())
}

// Gathers all the stub-gen annotated items in this crate so the `stub_gen`
// binary can emit `velopack.pyi`. Only present when generating stubs.
#[cfg(feature = "stub-gen")]
pyo3_stub_gen::define_stub_info_gatherer!(stub_info);
