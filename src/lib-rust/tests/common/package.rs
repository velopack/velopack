//! A synthetic chunked package for fetch and manager tests: the chunk transport bytes sit at their
//! Section B offsets, the trailer (A‖B) at the end and noise everywhere else, with a feed asset that
//! points at the trailer. Nothing reads it as a zip, so no zip structure is needed.

use super::index_writer::*;
use flate2::{Compress, Compression, FlushCompress};
use velopack::chunks::{sha256, sha256_hex};
use velopack::VelopackAsset;

/// Compresses one chunk the way vpk does: a fresh deflate stream, one write, a sync flush.
pub fn deflate_chunk_sync(plain: &[u8]) -> Vec<u8> {
    let mut c = Compress::new(Compression::default(), false);
    let mut out = Vec::with_capacity(plain.len() + 64);
    c.compress_vec(plain, &mut out, FlushCompress::Sync).unwrap();
    assert_eq!(c.total_in() as usize, plain.len());
    out
}

/// Text-like pseudo-random bytes, so deflate actually shrinks them.
pub fn compressible(seed: u64, len: usize) -> Vec<u8> {
    random_bytes(seed, len).iter().map(|b| b'a' + (b % 16)).collect()
}

pub struct Package {
    pub bytes: Vec<u8>,
    pub built: BuiltIndex,
    pub asset: VelopackAsset,
}

pub fn build_package(files: &[TestFile], file_name: &str, version: &str) -> Package {
    let built = build_index(
        files,
        &TestIndexOptions {
            chunk_size: 64 * 1024,
            compress: Some(deflate_chunk_sync),
            base_offset: 100,
            ..Default::default()
        },
    );
    let data_end = built.chunks.iter().map(|c| c.offset + c.length as u64).max().unwrap_or(100) + 2;
    let mut bytes = random_bytes(99, data_end as usize);
    for c in &built.chunks {
        bytes[c.offset as usize..c.offset as usize + c.length as usize].copy_from_slice(&c.transport);
    }
    let trailer = built.trailer();
    let trailer_offset = bytes.len() as u64;
    bytes.extend_from_slice(&trailer);
    bytes.extend_from_slice(&[0u8; 64]);
    let asset = VelopackAsset {
        PackageId: "App".to_string(),
        Version: version.to_string(),
        Type: "Full".to_string(),
        FileName: file_name.to_string(),
        SHA256: sha256_hex(&sha256(&bytes)),
        Size: bytes.len() as u64,
        ChunkIndexOffset: trailer_offset,
        ChunkIndexLength: trailer.len() as u64,
        ChunkIndexSHA256: built.digest_hex(),
        ..Default::default()
    };
    Package { bytes, built, asset }
}
