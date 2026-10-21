mod common;

use common::index_writer::random_bytes;
use flate2::{Compress, Compression, FlushCompress};
use std::io::Read;
use velopack::chunks::{inflate_chunk, sha256, sha256_hex, InflateError, ENTRY_TERMINATOR};

/// Compresses one chunk the way vpk does: a fresh deflate stream, one write, a sync flush, trailing bytes discarded.
fn deflate_chunk_sync(plain: &[u8]) -> Vec<u8> {
    let mut c = Compress::new(Compression::default(), false);
    let mut out = Vec::with_capacity(plain.len() + 64);
    c.compress_vec(plain, &mut out, FlushCompress::Sync).unwrap();
    assert_eq!(c.total_in() as usize, plain.len());
    assert!(out.ends_with(&[0x00, 0x00, 0xFF, 0xFF]), "sync flush must end with an empty stored block");
    out
}

fn deflate_chunk_finished(plain: &[u8]) -> Vec<u8> {
    let mut c = Compress::new(Compression::default(), false);
    let mut out = Vec::with_capacity(plain.len() + 64);
    c.compress_vec(plain, &mut out, FlushCompress::Finish).unwrap();
    out
}

fn compressible(seed: u64, len: usize) -> Vec<u8> {
    // text-like data so deflate actually shrinks it
    random_bytes(seed, len).iter().map(|b| b'a' + (b % 16)).collect()
}

#[test]
fn inflates_non_final_chunk() {
    let plain = compressible(1, 70_000);
    let comp = deflate_chunk_sync(&plain);
    assert!(comp.len() < plain.len());
    let out = inflate_chunk(&comp, 8, plain.len()).unwrap();
    assert_eq!(out, plain);
    assert_eq!(sha256(&out), sha256(&plain));
}

#[test]
fn inflates_final_chunk() {
    // the last chunk of an entry is produced exactly like the others; the concatenation plus the
    // entry terminator is one standard deflate stream
    let chunks: Vec<Vec<u8>> = vec![compressible(1, 65536), compressible(2, 65536), compressible(3, 1234)];
    let comps: Vec<Vec<u8>> = chunks.iter().map(|c| deflate_chunk_sync(c)).collect();
    let last = inflate_chunk(&comps[2], 8, chunks[2].len()).unwrap();
    assert_eq!(last, chunks[2]);

    let mut entry = Vec::new();
    for c in &comps {
        entry.extend_from_slice(c);
    }
    entry.extend_from_slice(&ENTRY_TERMINATOR);
    let mut decoder = flate2::read::DeflateDecoder::new(&entry[..]);
    let mut whole = Vec::new();
    decoder.read_to_end(&mut whole).unwrap();
    assert_eq!(whole, chunks.concat());
}

#[test]
fn rejects_wrong_length() {
    let plain = compressible(4, 50_000);
    let comp = deflate_chunk_sync(&plain);
    assert!(matches!(
        inflate_chunk(&comp, 8, plain.len() - 1),
        Err(InflateError::Length(..)) | Err(InflateError::Inflate(_))
    ));
    assert!(matches!(inflate_chunk(&comp, 8, plain.len() + 1), Err(InflateError::Length(..))));
    assert!(matches!(inflate_chunk(&comp[..comp.len() - 10], 8, plain.len()), Err(_)));
    assert!(matches!(inflate_chunk(&plain, 0, plain.len() + 1), Err(InflateError::Length(..))));
    assert!(matches!(inflate_chunk(&plain, 5, plain.len()), Err(InflateError::Method(5))));
}

#[test]
fn stored_chunk_passthrough() {
    let plain = random_bytes(5, 10_000);
    let out = inflate_chunk(&plain, 0, plain.len()).unwrap();
    assert_eq!(out, plain);
}

#[test]
fn extra_bytes_before_terminator_fail_length_or_hash() {
    let plain = compressible(6, 40_000);
    let mut comp = deflate_chunk_sync(&plain);
    // a second sync-flushed fragment before the terminator produces extra plaintext
    comp.extend_from_slice(&deflate_chunk_sync(b"extra"));
    match inflate_chunk(&comp, 8, plain.len()) {
        Err(InflateError::Length(..)) | Err(InflateError::Inflate(_)) => {}
        Ok(out) => assert_ne!(sha256_hex(&sha256(&out)), sha256_hex(&sha256(&plain))),
        Err(e) => panic!("unexpected error {}", e),
    }
    // corrupted bytes inside the fragment
    let mut corrupt = deflate_chunk_sync(&plain);
    let mid = corrupt.len() / 2;
    corrupt[mid] ^= 0xFF;
    match inflate_chunk(&corrupt, 8, plain.len()) {
        Err(_) => {}
        Ok(out) => assert_ne!(out, plain),
    }
}

#[test]
fn rejects_trailing_garbage() {
    // a finished stream (BFINAL set) ends before the terminator is consumed: total_in must catch it
    let plain = compressible(7, 30_000);
    let finished = deflate_chunk_finished(&plain);
    let err = inflate_chunk(&finished, 8, plain.len()).unwrap_err();
    assert!(matches!(err, InflateError::Inflate(_)), "{}", err);
    let mut with_garbage = finished.clone();
    with_garbage.extend_from_slice(b"garbage");
    assert!(inflate_chunk(&with_garbage, 8, plain.len()).is_err());
}
