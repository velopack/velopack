// This file is auto-generated. Do not edit by hand.
#![allow(non_snake_case)]
use pyo3::prelude::*;
use velopack::{VelopackAsset, UpdateInfo, UpdateOptions, locator::VelopackLocatorConfig};
use std::path::PathBuf;

#[pyclass(name = "VelopackLocatorConfig")]
#[derive(Debug, Clone, Default)]
pub struct PyVelopackLocatorConfig {
    pub RootAppDir: PathBuf,
    pub UpdateExePath: PathBuf,
    pub PackagesDir: PathBuf,
    pub ManifestPath: PathBuf,
    pub CurrentBinaryDir: PathBuf,
    pub IsPortable: bool,
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
    pub PackageId: String,
    pub Version: String,
    pub Type: String,
    pub FileName: String,
    pub SHA1: String,
    pub SHA256: String,
    pub Size: u64,
    pub NotesMarkdown: String,
    pub NotesHtml: String,
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
    pub TargetFullRelease: PyVelopackAsset,
    pub BaseRelease: Option<PyVelopackAsset>,
    pub DeltasToTarget: Vec<PyVelopackAsset>,
    pub IsDowngrade: bool,
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
    pub AllowVersionDowngrade: bool,
    pub ExplicitChannel: Option<String>,
    pub MaximumDeltasBeforeFallback: i32,
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

