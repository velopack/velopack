//! Compression level

use core::fmt::Display;
use std::error::Error;

use flate2::Compression;

/// Compression level that should be used when compressing a file or data.
///
/// Current compression providers support only levels from 0 to 9, so these are the only ones being
/// supported.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub struct CompressionLevel(u8);

impl CompressionLevel {
    /// Construct a new value of a compression level setting.
    ///
    /// The integer value must be less than or equal to 9, otherwise `None` is returned
    #[inline]
    pub const fn new(level: u8) -> Option<Self> {
        if level <= 9 { Some(Self(level)) } else { None }
    }

    /// Construct a new value of a compression level setting without checking the value.
    ///
    /// # Safety
    ///
    /// The value must be a valid supported compression level
    #[inline]
    pub const unsafe fn new_unchecked(level: u8) -> Self {
        Self(level)
    }

    /// No compression
    #[inline]
    pub const fn none() -> Self {
        Self(0)
    }

    /// Fastest compression
    #[inline]
    pub const fn fast() -> Self {
        Self(1)
    }

    /// Balanced level with moderate compression and speed. The raw value is 6.
    #[inline]
    pub const fn balanced() -> Self {
        Self(6)
    }

    /// Best compression ratio, comes at a worse performance
    #[inline]
    pub const fn best() -> Self {
        Self(9)
    }

    /// Get the compression level as an integer
    #[inline]
    pub const fn get(self) -> u8 {
        self.0
    }
}

impl Default for CompressionLevel {
    /// Equivalent to [`Self::balanced`]
    fn default() -> Self {
        Self::balanced()
    }
}

/// The number for compression level was invalid
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct InvalidCompressionLevel(u32);

impl InvalidCompressionLevel {
    /// The value which was supplied
    pub fn value(self) -> u32 {
        self.0
    }
}

impl Display for InvalidCompressionLevel {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Invalid compression level number: {}", self.0)
    }
}

impl Error for InvalidCompressionLevel {}

impl From<CompressionLevel> for Compression {
    #[inline]
    fn from(value: CompressionLevel) -> Self {
        Compression::new(value.0.into())
    }
}

impl TryFrom<Compression> for CompressionLevel {
    type Error = InvalidCompressionLevel;

    fn try_from(value: Compression) -> Result<Self, Self::Error> {
        let level = value.level();
        Self::new(
            level
                .try_into()
                .map_err(|_| InvalidCompressionLevel(level))?,
        )
        .ok_or(InvalidCompressionLevel(level))
    }
}

impl From<CompressionLevel> for u8 {
    #[inline]
    fn from(value: CompressionLevel) -> Self {
        value.0
    }
}

impl TryFrom<u8> for CompressionLevel {
    type Error = InvalidCompressionLevel;

    #[inline]
    fn try_from(value: u8) -> Result<Self, Self::Error> {
        Self::new(value).ok_or(InvalidCompressionLevel(value.into()))
    }
}
