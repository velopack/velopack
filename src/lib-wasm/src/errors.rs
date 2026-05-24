use std::path::PathBuf;

#[derive(thiserror::Error, Debug)]
pub enum Error {
    #[error("File does not exist: {0}")]
    FileNotFound(PathBuf),
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    #[error("Checksum did not match for {0} (expected {1}, actual {2})")]
    ChecksumInvalid(PathBuf, String, String),
    #[error("Size did not match for {0} (expected {1}, actual {2})")]
    SizeInvalid(PathBuf, u64, u64),
    #[error("Network error: {0}")]
    Network(String),
    #[error("Json error: {0}")]
    Json(#[from] serde_json::Error),
    #[error("Semver parse error: {0}")]
    Semver(#[from] semver::Error),
    #[error("This update package is invalid: {0}.")]
    InvalidPackage(String),
    #[error("This application is not properly installed: {0}")]
    NotInstalled(String),
    #[error("This is not supported: {0}")]
    NotSupported(String),
    #[error("{0}")]
    Other(String),
}
