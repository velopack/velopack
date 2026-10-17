//! The ranged-read abstraction the chunk fetcher consumes (§8.2). Implementations live in
//! `download.rs` (HTTP) and `sources::FileSource` (local file); this module holds only the trait.

/// Why a ranged read failed.
#[derive(Debug, Clone, thiserror::Error)]
pub enum RangeError {
    /// Connect/read/timeout/IO errors and retryable statuses: the retry policy tries again.
    #[error("transient range error: {0}")]
    Transient(String),
    /// Ranged access is not available for this asset (200 instead of 206, 416, bad Content-Range, ...):
    /// the chunk path is abandoned in favour of the full download.
    #[error("ranged access unavailable: {0}")]
    Unavailable(String),
    /// The caller cancelled the update.
    #[error("cancelled")]
    Cancelled,
}

/// Byte-range access to one release asset.
pub trait RangeReader: Send + Sync {
    /// The asset size as reported by the origin, once learned from the first ranged response.
    fn len(&self) -> Option<u64>;
    /// Performs one request and fills `buf` with the bytes at `offset`.
    fn read_exact_at(&self, offset: u64, buf: &mut [u8]) -> Result<(), RangeError>;
}
