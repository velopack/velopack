use pyo3::exceptions::PyException;
use pyo3::prelude::*;

#[pyclass(name="VelopackError", extends=PyException, module="velopack.exceptions")]
#[derive(Debug)]
pub struct VelopackError {
    pub message: String,
}

#[pymethods]
impl VelopackError {
    #[new]
    fn new(message: String) -> Self {
        VelopackError { message }
    }

    fn __str__(&self) -> String {
        self.message.clone()
    }
}
