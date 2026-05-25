#[derive(thiserror::Error, Debug)]
pub enum Error {
    #[error("File does not exist: {0}")]
    FileNotFound(String),
    #[error("Checksum did not match for {0} (expected {1}, actual {2})")]
    ChecksumInvalid(String, String, String),
    #[error("Size did not match for {0} (expected {1}, actual {2})")]
    SizeInvalid(String, u64, u64),
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
    #[error("IO error: {0}")]
    Io(String),
    #[error("{0}")]
    Other(String),
}

impl From<std::io::Error> for Error {
    fn from(e: std::io::Error) -> Self {
        Error::Io(e.to_string())
    }
}
