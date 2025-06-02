use pyo3::prelude::*;

// Import your original structs from your rust library
// Adjust the import path according to your crate structure
use velopack::{VelopackAsset, UpdateInfo};

#[pyclass(name = "VelopackAsset")]
#[derive(Debug, Clone)]
pub struct PyVelopackAsset(pub VelopackAsset);

#[pymethods]
impl PyVelopackAsset {
    #[new]
    #[pyo3(signature = (package_id=String::new(), version=String::new(), type_=String::new(), file_name=String::new(), sha1=String::new(), sha256=String::new(), size=0, notes_markdown=String::new(), notes_html=String::new()))]
    pub fn new(
        package_id: String,
        version: String,
        type_: String,
        file_name: String,
        sha1: String,
        sha256: String,
        size: u64,
        notes_markdown: String,
        notes_html: String,
    ) -> Self {
        PyVelopackAsset(VelopackAsset {
            PackageId: package_id,
            Version: version,
            Type: type_,
            FileName: file_name,
            SHA1: sha1,
            SHA256: sha256,
            Size: size,
            NotesMarkdown: notes_markdown,
            NotesHtml: notes_html,
        })
    }

    // Direct field access - much cleaner!
    #[getter]
    fn package_id(&self) -> &str {
        &self.0.PackageId
    }

    #[getter]
    fn version(&self) -> &str {
        &self.0.Version
    }

    #[getter]
    fn type_(&self) -> &str {
        &self.0.Type
    }

    #[getter]
    fn file_name(&self) -> &str {
        &self.0.FileName
    }

    #[getter]
    fn sha1(&self) -> &str {
        &self.0.SHA1
    }

    #[getter]
    fn sha256(&self) -> &str {
        &self.0.SHA256
    }

    #[getter]
    fn size(&self) -> u64 {
        self.0.Size
    }

    #[getter]
    fn notes_markdown(&self) -> &str {
        &self.0.NotesMarkdown
    }

    #[getter]
    fn notes_html(&self) -> &str {
        &self.0.NotesHtml
    }

    fn __repr__(&self) -> String {
        format!(
            "VelopackAsset(package_id='{}', version='{}', type='{}', file_name='{}', size={})",
            self.0.PackageId, self.0.Version, self.0.Type, self.0.FileName, self.0.Size
        )
    }
}

// Conversion traits for seamless interop
impl From<VelopackAsset> for PyVelopackAsset {
    fn from(asset: VelopackAsset) -> Self {
        PyVelopackAsset(asset)
    }
}

impl From<PyVelopackAsset> for VelopackAsset {
    fn from(py_asset: PyVelopackAsset) -> Self {
        py_asset.0
    }
}

impl AsRef<VelopackAsset> for PyVelopackAsset {
    fn as_ref(&self) -> &VelopackAsset {
        &self.0
    }
}

#[pyclass(name = "UpdateInfo")]
#[derive(Debug, Clone)]
pub struct PyUpdateInfo(pub UpdateInfo);

#[pymethods]
impl PyUpdateInfo {
    #[new]
    #[pyo3(signature = (target_full_release, base_release=None, deltas_to_target=Vec::new(), is_downgrade=false))]
    pub fn new(
        target_full_release: PyVelopackAsset,
        base_release: Option<PyVelopackAsset>,
        deltas_to_target: Vec<PyVelopackAsset>,
        is_downgrade: bool,
    ) -> Self {
        PyUpdateInfo(UpdateInfo {
            TargetFullRelease: target_full_release.into(),
            BaseRelease: base_release.map(Into::into),
            DeltasToTarget: deltas_to_target.into_iter().map(Into::into).collect(),
            IsDowngrade: is_downgrade,
        })
    }

    #[staticmethod]
    pub fn new_full(target: PyVelopackAsset, is_downgrade: bool) -> PyUpdateInfo {
        PyUpdateInfo(UpdateInfo {
            TargetFullRelease: target.into(),
            BaseRelease: None,
            DeltasToTarget: Vec::new(),
            IsDowngrade: is_downgrade,
        })
    }

    #[staticmethod]
    pub fn new_delta(
        target: PyVelopackAsset,
        base: PyVelopackAsset,
        deltas: Vec<PyVelopackAsset>,
    ) -> PyUpdateInfo {
        let rust_deltas = deltas.into_iter().map(Into::into).collect();
        PyUpdateInfo(UpdateInfo {
            TargetFullRelease: target.into(),
            BaseRelease: Some(base.into()),
            DeltasToTarget: rust_deltas,
            IsDowngrade: false,
        })
    }

    #[getter]
    fn target_full_release(&self) -> PyVelopackAsset {
        PyVelopackAsset(self.0.TargetFullRelease.clone())
    }

    #[getter]
    fn base_release(&self) -> Option<PyVelopackAsset> {
        self.0.BaseRelease.clone().map(PyVelopackAsset)
    }

    #[getter]
    fn deltas_to_target(&self) -> Vec<PyVelopackAsset> {
        self.0.DeltasToTarget.iter().cloned().map(PyVelopackAsset).collect()
    }

    #[getter]
    fn is_downgrade(&self) -> bool {
        self.0.IsDowngrade
    }

    fn __repr__(&self) -> String {
        format!(
            "UpdateInfo(target_version='{}', has_base_release={}, deltas_count={}, is_downgrade={})",
            self.0.TargetFullRelease.Version,
            self.0.BaseRelease.is_some(),
            self.0.DeltasToTarget.len(),
            self.0.IsDowngrade
        )
    }
}

impl From<UpdateInfo> for PyUpdateInfo {
    fn from(info: UpdateInfo) -> Self {
        PyUpdateInfo(info)
    }
}

impl From<PyUpdateInfo> for UpdateInfo {
    fn from(py_info: PyUpdateInfo) -> Self {
        py_info.0
    }
}

impl AsRef<UpdateInfo> for PyUpdateInfo {
    fn as_ref(&self) -> &UpdateInfo {
        &self.0
    }
}

