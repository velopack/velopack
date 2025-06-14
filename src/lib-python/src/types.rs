// This file is auto-generated. Do not edit by hand.
#![allow(non_snake_case)]
use pyo3::prelude::*;
use velopack::{VelopackAsset, UpdateInfo, UpdateOptions, locator::VelopackLocatorConfig};
use std::path::PathBuf;

#[pyclass(name = "VelopackLocatorConfig")]
#[derive(Debug, Clone, Default)]
pub struct PyVelopackLocatorConfig {
    #[pyo3(get, set)]
    pub RootAppDir: PathBuf,
    #[pyo3(get, set)]
    pub UpdateExePath: PathBuf,
    #[pyo3(get, set)]
    pub PackagesDir: PathBuf,
    #[pyo3(get, set)]
    pub ManifestPath: PathBuf,
    #[pyo3(get, set)]
    pub CurrentBinaryDir: PathBuf,
    #[pyo3(get, set)]
    pub IsPortable: bool,
}

#[pymethods]
impl PyVelopackLocatorConfig {
    #[new]
    #[pyo3(signature = (RootAppDir, UpdateExePath, PackagesDir, ManifestPath, CurrentBinaryDir, IsPortable))]
    fn new(
        RootAppDir: PathBuf,
        UpdateExePath: PathBuf,
        PackagesDir: PathBuf,
        ManifestPath: PathBuf,
        CurrentBinaryDir: PathBuf,
        IsPortable: bool,
        ) -> Self {
        Self {
            RootAppDir: RootAppDir.into(),
            UpdateExePath: UpdateExePath.into(),
            PackagesDir: PackagesDir.into(),
            ManifestPath: ManifestPath.into(),
            CurrentBinaryDir: CurrentBinaryDir.into(),
            IsPortable: IsPortable,
        }
    }
}

impl From<VelopackLocatorConfig> for PyVelopackLocatorConfig {
    fn from(value: VelopackLocatorConfig) -> Self {
        PyVelopackLocatorConfig {
            RootAppDir: value.RootAppDir.into(),
            UpdateExePath: value.UpdateExePath.into(),
            PackagesDir: value.PackagesDir.into(),
            ManifestPath: value.ManifestPath.into(),
            CurrentBinaryDir: value.CurrentBinaryDir.into(),
            IsPortable: value.IsPortable,
        }
    }
}

impl Into<VelopackLocatorConfig> for PyVelopackLocatorConfig {
    fn into(self) -> VelopackLocatorConfig {
        VelopackLocatorConfig {
            RootAppDir: self.RootAppDir.into(),
            UpdateExePath: self.UpdateExePath.into(),
            PackagesDir: self.PackagesDir.into(),
            ManifestPath: self.ManifestPath.into(),
            CurrentBinaryDir: self.CurrentBinaryDir.into(),
            IsPortable: self.IsPortable,
        }
    }
}

#[pyclass(name = "VelopackAsset")]
#[derive(Debug, Clone, Default)]
pub struct PyVelopackAsset {
    #[pyo3(get, set)]
    pub PackageId: String,
    #[pyo3(get, set)]
    pub Version: String,
    #[pyo3(get, set)]
    pub Type: String,
    #[pyo3(get, set)]
    pub FileName: String,
    #[pyo3(get, set)]
    pub SHA1: String,
    #[pyo3(get, set)]
    pub SHA256: String,
    #[pyo3(get, set)]
    pub Size: u64,
    #[pyo3(get, set)]
    pub NotesMarkdown: String,
    #[pyo3(get, set)]
    pub NotesHtml: String,
}

#[pymethods]
impl PyVelopackAsset {
    #[new]
    #[pyo3(signature = (PackageId, Version, Type, FileName, SHA1, SHA256, Size, NotesMarkdown, NotesHtml))]
    fn new(
        PackageId: String,
        Version: String,
        Type: String,
        FileName: String,
        SHA1: String,
        SHA256: String,
        Size: u64,
        NotesMarkdown: String,
        NotesHtml: String,
        ) -> Self {
        Self {
            PackageId: PackageId.into(),
            Version: Version.into(),
            Type: Type.into(),
            FileName: FileName.into(),
            SHA1: SHA1.into(),
            SHA256: SHA256.into(),
            Size: Size,
            NotesMarkdown: NotesMarkdown.into(),
            NotesHtml: NotesHtml.into(),
        }
    }
}

impl From<VelopackAsset> for PyVelopackAsset {
    fn from(value: VelopackAsset) -> Self {
        PyVelopackAsset {
            PackageId: value.PackageId.into(),
            Version: value.Version.into(),
            Type: value.Type.into(),
            FileName: value.FileName.into(),
            SHA1: value.SHA1.into(),
            SHA256: value.SHA256.into(),
            Size: value.Size,
            NotesMarkdown: value.NotesMarkdown.into(),
            NotesHtml: value.NotesHtml.into(),
        }
    }
}

impl Into<VelopackAsset> for PyVelopackAsset {
    fn into(self) -> VelopackAsset {
        VelopackAsset {
            PackageId: self.PackageId.into(),
            Version: self.Version.into(),
            Type: self.Type.into(),
            FileName: self.FileName.into(),
            SHA1: self.SHA1.into(),
            SHA256: self.SHA256.into(),
            Size: self.Size,
            NotesMarkdown: self.NotesMarkdown.into(),
            NotesHtml: self.NotesHtml.into(),
        }
    }
}

#[pyclass(name = "UpdateInfo")]
#[derive(Debug, Clone, Default)]
pub struct PyUpdateInfo {
    #[pyo3(get, set)]
    pub TargetFullRelease: PyVelopackAsset,
    #[pyo3(get, set)]
    pub BaseRelease: Option<PyVelopackAsset>,
    #[pyo3(get, set)]
    pub DeltasToTarget: Vec<PyVelopackAsset>,
    #[pyo3(get, set)]
    pub IsDowngrade: bool,
}

#[pymethods]
impl PyUpdateInfo {
    #[new]
    #[pyo3(signature = (TargetFullRelease, DeltasToTarget, IsDowngrade, BaseRelease = None))]
    fn new(
        TargetFullRelease: PyVelopackAsset,
        DeltasToTarget: Vec<PyVelopackAsset>,
        IsDowngrade: bool,
        BaseRelease: Option<PyVelopackAsset>,
        ) -> Self {
        Self {
            TargetFullRelease: TargetFullRelease.into(),
            BaseRelease: BaseRelease.map(Into::into),
            DeltasToTarget: DeltasToTarget.into_iter().map(Into::into).collect(),
            IsDowngrade: IsDowngrade,
        }
    }
}

impl From<UpdateInfo> for PyUpdateInfo {
    fn from(value: UpdateInfo) -> Self {
        PyUpdateInfo {
            TargetFullRelease: value.TargetFullRelease.into(),
            BaseRelease: value.BaseRelease.map(Into::into),
            DeltasToTarget: value.DeltasToTarget.into_iter().map(Into::into).collect(),
            IsDowngrade: value.IsDowngrade,
        }
    }
}

impl Into<UpdateInfo> for PyUpdateInfo {
    fn into(self) -> UpdateInfo {
        UpdateInfo {
            TargetFullRelease: self.TargetFullRelease.into(),
            BaseRelease: self.BaseRelease.map(Into::into),
            DeltasToTarget: self.DeltasToTarget.into_iter().map(Into::into).collect(),
            IsDowngrade: self.IsDowngrade,
        }
    }
}

#[pyclass(name = "UpdateOptions")]
#[derive(Debug, Clone, Default)]
pub struct PyUpdateOptions {
    #[pyo3(get, set)]
    pub AllowVersionDowngrade: bool,
    #[pyo3(get, set)]
    pub ExplicitChannel: Option<String>,
    #[pyo3(get, set)]
    pub MaximumDeltasBeforeFallback: i32,
}

#[pymethods]
impl PyUpdateOptions {
    #[new]
    #[pyo3(signature = (AllowVersionDowngrade, MaximumDeltasBeforeFallback, ExplicitChannel = None))]
    fn new(
        AllowVersionDowngrade: bool,
        MaximumDeltasBeforeFallback: i32,
        ExplicitChannel: Option<String>,
        ) -> Self {
        Self {
            AllowVersionDowngrade: AllowVersionDowngrade,
            ExplicitChannel: ExplicitChannel.map(Into::into),
            MaximumDeltasBeforeFallback: MaximumDeltasBeforeFallback,
        }
    }
}

impl From<UpdateOptions> for PyUpdateOptions {
    fn from(value: UpdateOptions) -> Self {
        PyUpdateOptions {
            AllowVersionDowngrade: value.AllowVersionDowngrade,
            ExplicitChannel: value.ExplicitChannel.map(Into::into),
            MaximumDeltasBeforeFallback: value.MaximumDeltasBeforeFallback,
        }
    }
}

impl Into<UpdateOptions> for PyUpdateOptions {
    fn into(self) -> UpdateOptions {
        UpdateOptions {
            AllowVersionDowngrade: self.AllowVersionDowngrade,
            ExplicitChannel: self.ExplicitChannel.map(Into::into),
            MaximumDeltasBeforeFallback: self.MaximumDeltasBeforeFallback,
        }
    }
}

