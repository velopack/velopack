//! Per-chunk inflate (§4.2) and SHA-256 helpers.

use super::INFLATE_TERMINATOR;
use flate2::{Decompress, FlushDecompress, Status};
use sha2::{Digest, Sha256};
use std::io::Read;
use std::path::Path;

/// A chunk could not be reconstructed from its transport bytes (the chunk-mismatch transient class).
#[derive(Debug, Clone, thiserror::Error)]
pub enum InflateError {
    #[error("chunk did not inflate cleanly: {0}")]
    Inflate(String),
    #[error("chunk length mismatch (expected {0} bytes, produced {1})")]
    Length(usize, usize),
    #[error("unsupported entry method {0}")]
    Method(u8),
}

/// Reconstructs one chunk's plaintext from its fetched bytes. For a deflate entry (`method == 8`) the
/// chunk is a sync-flushed deflate fragment, terminated here with an empty final block; it is accepted
/// only when the stream ends exactly at the end of the input and produces exactly `expected_len` bytes.
/// Stored entries (`method == 0`) are passed through. The caller verifies the SHA-256 afterwards.
pub fn inflate_chunk(comp: &[u8], method: u8, expected_len: usize) -> Result<Vec<u8>, InflateError> {
    match method {
        0 => {
            if comp.len() != expected_len {
                return Err(InflateError::Length(expected_len, comp.len()));
            }
            Ok(comp.to_vec())
        }
        8 => {
            let mut input = Vec::with_capacity(comp.len() + INFLATE_TERMINATOR.len());
            input.extend_from_slice(comp);
            input.extend_from_slice(&INFLATE_TERMINATOR);
            let mut out = Vec::with_capacity(expected_len);
            let mut decoder = Decompress::new(false);
            match decoder.decompress_vec(&input, &mut out, FlushDecompress::Finish) {
                Ok(Status::StreamEnd) => {
                    if out.len() != expected_len {
                        return Err(InflateError::Length(expected_len, out.len()));
                    }
                    if decoder.total_in() != input.len() as u64 {
                        return Err(InflateError::Inflate("stream ended before the end of the chunk".into()));
                    }
                    Ok(out)
                }
                Ok(status) => Err(InflateError::Inflate(format!("stream did not end ({:?})", status))),
                Err(e) => Err(InflateError::Inflate(e.to_string())),
            }
        }
        m => Err(InflateError::Method(m)),
    }
}

/// SHA-256 of a byte slice.
pub fn sha256(data: &[u8]) -> [u8; 32] {
    Sha256::digest(data).into()
}

/// Uppercase hex of a hash (the feed/JSON convention).
pub fn sha256_hex(hash: &[u8]) -> String {
    hash.iter().fold(String::with_capacity(hash.len() * 2), |mut s, b| {
        use std::fmt::Write;
        write!(s, "{:02X}", b).unwrap();
        s
    })
}

/// Parses a 64-character hex SHA-256 (either case).
pub fn parse_sha256_hex(hex: &str) -> Option<[u8; 32]> {
    let hex = hex.trim();
    if hex.len() != 64 || !hex.bytes().all(|b| b.is_ascii_hexdigit()) {
        return None;
    }
    let mut out = [0u8; 32];
    for (i, byte) in out.iter_mut().enumerate() {
        *byte = u8::from_str_radix(&hex[i * 2..i * 2 + 2], 16).ok()?;
    }
    Some(out)
}

/// Streams a file through SHA-256.
pub fn sha256_file<P: AsRef<Path>>(path: P) -> std::io::Result<[u8; 32]> {
    let mut file = std::fs::File::open(path)?;
    sha256_reader(&mut file)
}

/// Streams a reader to its end through SHA-256.
pub fn sha256_reader<R: Read>(reader: &mut R) -> std::io::Result<[u8; 32]> {
    let mut hasher = Sha256::new();
    let mut buf = vec![0u8; 64 * 1024];
    loop {
        let n = reader.read(&mut buf)?;
        if n == 0 {
            break;
        }
        hasher.update(&buf[..n]);
    }
    Ok(hasher.finalize().into())
}
