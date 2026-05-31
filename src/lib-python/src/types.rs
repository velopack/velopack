// This file is auto-generated. Do not edit by hand.
#![allow(non_snake_case)]
#![allow(clippy::all)]
use pyo3::prelude::*;
use velopack::{VelopackAsset, UpdateInfo, UpdateOptions, locator::VelopackLocatorConfig};
use std::path::PathBuf;

/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "VelopackLocatorConfig", from_py_object)]
#[derive(Debug, Clone, Default)]
pub struct PyVelopackLocatorConfig {
    /// The root directory of the current app.
    #[pyo3(get, set)]
    pub RootAppDir: PathBuf,
    /// The path to the Update.exe binary.
    #[pyo3(get, set)]
    pub UpdateExePath: PathBuf,
    /// The path to the packages' directory.
    #[pyo3(get, set)]
    pub PackagesDir: PathBuf,
    /// The current app manifest.
    #[pyo3(get, set)]
    pub ManifestPath: PathBuf,
    /// The directory containing the application's user binaries.
    #[pyo3(get, set)]
    pub CurrentBinaryDir: PathBuf,
    /// Whether the current application is portable or installed.
    #[pyo3(get, set)]
    pub IsPortable: bool,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
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

/// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "VelopackAsset", from_py_object)]
#[derive(Debug, Clone, Default)]
pub struct PyVelopackAsset {
    /// The name or Id of the package containing this release.
    #[pyo3(get, set)]
    pub PackageId: String,
    /// The version of this release.
    #[pyo3(get, set)]
    pub Version: String,
    /// The type of asset (eg. "Full" or "Delta").
    #[pyo3(get, set)]
    pub Type: String,
    /// The filename of the update package containing this release.
    #[pyo3(get, set)]
    pub FileName: String,
    /// The SHA1 checksum of the update package containing this release.
    #[pyo3(get, set)]
    pub SHA1: String,
    /// The SHA256 checksum of the update package containing this release.
    #[pyo3(get, set)]
    pub SHA256: String,
    /// The size in bytes of the update package containing this release.
    #[pyo3(get, set)]
    pub Size: u64,
    /// The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
    #[pyo3(get, set)]
    pub NotesMarkdown: String,
    /// The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
    #[pyo3(get, set)]
    pub NotesHtml: String,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
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

/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "UpdateInfo", from_py_object)]
#[derive(Debug, Clone, Default)]
pub struct PyUpdateInfo {
    /// The available version that we are updating to.
    #[pyo3(get, set)]
    pub TargetFullRelease: PyVelopackAsset,
    /// The base release that this update is based on. This is only available if the update is a delta update.
    #[pyo3(get, set)]
    pub BaseRelease: Option<PyVelopackAsset>,
    /// The list of delta updates that can be applied to the base version to get to the target version.
    #[pyo3(get, set)]
    pub DeltasToTarget: Vec<PyVelopackAsset>,
    /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
    /// In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
    /// deleted.
    #[pyo3(get, set)]
    pub IsDowngrade: bool,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
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

/// Options to customise the behaviour of UpdateManager.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "UpdateOptions", from_py_object)]
#[derive(Debug, Clone, Default)]
pub struct PyUpdateOptions {
    /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
    /// This could happen if a release has bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest version on that
    /// channel is lower than the current version.
    #[pyo3(get, set)]
    pub AllowVersionDowngrade: bool,
    /// **This option should usually be left None**.
    /// Overrides the default channel used to fetch updates.
    /// The default channel will be whatever channel was specified on the command line when building this release.
    /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
    /// This allows users to automatically receive updates from the same channel they installed from. This options
    /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
    /// without having to reinstall the application.
    #[pyo3(get, set)]
    pub ExplicitChannel: Option<String>,
    /// Sets the maximum number of deltas to consider before falling back to a full update.
    /// The default is 10. Set to a negative number (eg. -1) to disable deltas.
    #[pyo3(get, set)]
    pub MaximumDeltasBeforeFallback: i32,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
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

