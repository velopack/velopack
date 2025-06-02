use pyo3::prelude::*;
use pyo3::types::PyModule;

mod exceptions;
pub use exceptions::VelopackError;

mod app;
pub use app::VelopackAppWrapper;

mod manager;
pub use manager::UpdateManagerWrapper;


#[pymodule]
fn velopack(m: &Bound<'_, PyModule>) -> PyResult<()> {
    m.add_class::<VelopackError>()?;
    

    m.add_class::<VelopackAppWrapper>()?;
    m.add_class::<UpdateManagerWrapper>()?;

    // add __version__ attribute
    m.add("__version__", env!("CARGO_PKG_VERSION"))?;
    
    
    
    // add __author__ attribute
    m.add("__author__", env!("CARGO_PKG_AUTHORS"))?;
    
    Ok(())
}