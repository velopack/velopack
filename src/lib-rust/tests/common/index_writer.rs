//! Test-only chunk index writer: serialises Section A and Section B byte-for-byte per the delta v2
//! spec (§2.2/§2.3) from an in-memory file list. Chunking is fixed-size (the CDC algorithm is advisory)
//! and compressed sizes are synthetic unless a compressor is supplied. The writer never validates its
//! input, so tests can build deliberately invalid indexes.
//!
//! Shared with the bins tests via `#[path]`, so it depends only on `velopack` and std.
#![allow(dead_code)]

use std::fs;
use std::path::Path;
use velopack::chunks::{self, sha256};

#[derive(Clone, Debug)]
pub struct TestFile {
    pub path: String,
    /// 0 AppFile, 1 Updater, 2 Stub
    pub disposition: u8,
    /// 0 File, 1 Symlink, 2 EmptyDir
    pub kind: u8,
    pub data: Vec<u8>,
    pub target: String,
    pub mode: u16,
}

impl TestFile {
    pub fn file(path: &str, data: impl Into<Vec<u8>>) -> TestFile {
        TestFile {
            path: path.to_string(),
            disposition: 0,
            kind: 0,
            data: data.into(),
            target: String::new(),
            mode: 0,
        }
    }

    pub fn updater(data: impl Into<Vec<u8>>) -> TestFile {
        TestFile {
            disposition: 1,
            ..TestFile::file("Squirrel.exe", data)
        }
    }

    pub fn stub(name: &str, data: impl Into<Vec<u8>>) -> TestFile {
        TestFile {
            disposition: 2,
            ..TestFile::file(&format!("{}_ExecutionStub.exe", name), data)
        }
    }

    pub fn symlink(path: &str, target: &str) -> TestFile {
        TestFile {
            path: path.to_string(),
            disposition: 0,
            kind: 1,
            data: Vec::new(),
            target: target.to_string(),
            mode: 0,
        }
    }

    pub fn empty_dir(path: &str) -> TestFile {
        TestFile {
            path: path.to_string(),
            disposition: 0,
            kind: 2,
            data: Vec::new(),
            target: String::new(),
            mode: 0,
        }
    }

    pub fn with_disposition(mut self, disposition: u8) -> TestFile {
        self.disposition = disposition;
        self
    }
}

#[derive(Clone)]
pub struct TestIndexOptions {
    pub chunk_size: usize,
    /// Emit stored entries (chunk ranges map 1:1 onto plaintext) instead of deflate entries.
    pub stored: bool,
    /// Package offset of the first entry's data.
    pub base_offset: u64,
    /// Produces the transport bytes of a chunk; None uses synthetic sizes (3/4 of the plaintext + 1).
    pub compress: Option<fn(&[u8]) -> Vec<u8>>,
    /// Keep the files in the given order instead of sorting them (to build unsorted indexes).
    pub keep_order: bool,
}

impl Default for TestIndexOptions {
    fn default() -> Self {
        TestIndexOptions {
            chunk_size: 64 * 1024,
            stored: false,
            base_offset: 100,
            compress: None,
            keep_order: false,
        }
    }
}

/// One chunk of the built index with its plaintext and transport bytes.
#[derive(Clone, Debug)]
pub struct BuiltChunk {
    pub file_path: String,
    pub plain_offset: u64,
    pub plain: Vec<u8>,
    pub sha256: [u8; 32],
    /// Transport bytes when a compressor was supplied (stored entries: the plaintext), else empty.
    pub transport: Vec<u8>,
    pub offset: u64,
    pub length: u32,
}

#[derive(Clone, Debug)]
pub struct BuiltIndex {
    pub a: Vec<u8>,
    pub b: Vec<u8>,
    pub chunks: Vec<BuiltChunk>,
    /// Files in record order.
    pub files: Vec<TestFile>,
}

impl BuiltIndex {
    /// A‖B
    pub fn trailer(&self) -> Vec<u8> {
        let mut t = self.a.clone();
        t.extend_from_slice(&self.b);
        t
    }

    pub fn digest_hex(&self) -> String {
        chunks::sha256_hex(&sha256(&self.trailer()))
    }

    pub fn staged_id(&self) -> String {
        chunks::staged_id(&self.digest_hex())
    }

    pub fn chunk_hashes(&self) -> Vec<[u8; 32]> {
        self.chunks.iter().map(|c| c.sha256).collect()
    }
}

pub fn chunk_lengths(len: usize, chunk_size: usize) -> Vec<usize> {
    let mut out = Vec::new();
    let mut pos = 0;
    while pos < len {
        let n = chunk_size.min(len - pos);
        out.push(n);
        pos += n;
    }
    out
}

fn put_u16(v: &mut Vec<u8>, x: u16) {
    v.extend_from_slice(&x.to_le_bytes());
}
fn put_u32(v: &mut Vec<u8>, x: u32) {
    v.extend_from_slice(&x.to_le_bytes());
}
fn put_u64(v: &mut Vec<u8>, x: u64) {
    v.extend_from_slice(&x.to_le_bytes());
}

/// Builds A and B for the given files.
pub fn build_index(files: &[TestFile], opts: &TestIndexOptions) -> BuiltIndex {
    let mut files: Vec<TestFile> = files.to_vec();
    if !opts.keep_order {
        files.sort_by(|a, b| a.path.as_bytes().cmp(b.path.as_bytes()));
    }

    let mut path_blob: Vec<u8> = Vec::new();
    let mut file_records: Vec<u8> = Vec::new();
    let mut chunk_records: Vec<u8> = Vec::new();
    let mut b_files: Vec<u8> = Vec::new();
    let mut b_chunks: Vec<u8> = Vec::new();
    let mut built_chunks: Vec<BuiltChunk> = Vec::new();
    let mut next_chunk: u32 = 0;
    let mut cursor = opts.base_offset;

    for f in &files {
        let path_off = path_blob.len() as u32;
        path_blob.extend_from_slice(f.path.as_bytes());
        let (target_off, target_len) = if f.kind == 1 {
            let off = path_blob.len() as u32;
            path_blob.extend_from_slice(f.target.as_bytes());
            (off, f.target.len() as u16)
        } else {
            (0, 0)
        };

        let (length, sha, chunk_count, first_chunk) = match f.kind {
            0 => {
                let lens = chunk_lengths(f.data.len(), opts.chunk_size);
                let first = if lens.is_empty() {
                    0
                } else {
                    next_chunk
                };
                let data_offset = cursor + 30 + ("lib/app/".len() + f.path.len()) as u64;
                let mut plain_off = 0u64;
                let mut transport_off = data_offset;
                let mut compressed_total = 0u64;
                for &n in &lens {
                    let plain = f.data[plain_off as usize..plain_off as usize + n].to_vec();
                    let hash = sha256(&plain);
                    put_u32(&mut chunk_records, n as u32);
                    chunk_records.extend_from_slice(&hash);
                    let transport = if opts.stored {
                        plain.clone()
                    } else if let Some(compress) = opts.compress {
                        compress(&plain)
                    } else {
                        Vec::new()
                    };
                    let t_len = if opts.stored {
                        n as u32
                    } else if opts.compress.is_some() {
                        transport.len() as u32
                    } else {
                        (n * 3 / 4 + 1) as u32
                    };
                    let offset = if opts.stored {
                        data_offset + plain_off
                    } else {
                        transport_off
                    };
                    put_u64(&mut b_chunks, offset);
                    put_u32(&mut b_chunks, t_len);
                    built_chunks.push(BuiltChunk {
                        file_path: f.path.clone(),
                        plain_offset: plain_off,
                        plain,
                        sha256: hash,
                        transport,
                        offset,
                        length: t_len,
                    });
                    plain_off += n as u64;
                    transport_off += t_len as u64;
                    compressed_total += t_len as u64;
                }
                let count = lens.len() as u32;
                if count > 0 {
                    let (method, compressed_size) = if opts.stored {
                        (0u8, f.data.len() as u64)
                    } else {
                        (8u8, compressed_total + 2)
                    };
                    b_files.push(method);
                    b_files.extend_from_slice(&[0u8; 7]);
                    put_u64(&mut b_files, data_offset);
                    put_u64(&mut b_files, compressed_size);
                    cursor = data_offset + compressed_size;
                } else {
                    b_files.extend_from_slice(&[0u8; 24]);
                }
                next_chunk += count;
                (f.data.len() as u64, sha256(&f.data), count, first)
            }
            1 => {
                b_files.extend_from_slice(&[0u8; 24]);
                (f.target.len() as u64, sha256(f.target.as_bytes()), 0, 0)
            }
            _ => {
                b_files.extend_from_slice(&[0u8; 24]);
                (0, [0u8; 32], 0, 0)
            }
        };

        put_u32(&mut file_records, path_off);
        put_u16(&mut file_records, f.path.len() as u16);
        file_records.push(f.disposition);
        file_records.push(f.kind);
        put_u64(&mut file_records, length);
        put_u32(&mut file_records, first_chunk);
        put_u32(&mut file_records, chunk_count);
        put_u32(&mut file_records, target_off);
        put_u16(&mut file_records, target_len);
        put_u16(&mut file_records, f.mode);
        file_records.extend_from_slice(&sha);
    }

    let mut a: Vec<u8> = Vec::new();
    a.extend_from_slice(b"VPKA");
    put_u16(&mut a, 1);
    a.push(1);
    a.push(1);
    put_u32(&mut a, 0);
    put_u32(&mut a, chunks::CDC_MIN);
    put_u32(&mut a, chunks::CDC_AVG);
    put_u32(&mut a, chunks::CDC_MAX);
    put_u32(&mut a, files.len() as u32);
    put_u32(&mut a, next_chunk);
    put_u32(&mut a, path_blob.len() as u32);
    a.extend_from_slice(&[0u8; 12]);
    assert_eq!(a.len(), 48);
    a.extend_from_slice(&path_blob);
    a.extend_from_slice(&file_records);
    a.extend_from_slice(&chunk_records);

    let mut b: Vec<u8> = Vec::new();
    b.extend_from_slice(b"VPKB");
    put_u16(&mut b, 1);
    put_u16(&mut b, 0);
    put_u32(&mut b, files.len() as u32);
    put_u32(&mut b, next_chunk);
    b.extend_from_slice(&b_files);
    b.extend_from_slice(&b_chunks);

    BuiltIndex {
        a,
        b,
        chunks: built_chunks,
        files,
    }
}

/// Reads an installed-view tree (`current/` contents) into test files, skipping `sq.chunks`. Top-level
/// `Squirrel.exe` becomes the updater and top-level `*_ExecutionStub.exe` stubs, as the pack does.
pub fn files_from_dir(dir: &Path) -> Vec<TestFile> {
    fn walk(root: &Path, dir: &Path, out: &mut Vec<TestFile>) {
        for entry in fs::read_dir(dir).unwrap() {
            let entry = entry.unwrap();
            let path = entry.path();
            if path.is_dir() {
                walk(root, &path, out);
            } else {
                let rel = path.strip_prefix(root).unwrap().to_string_lossy().replace('\\', "/");
                if rel == chunks::LOCAL_INDEX_NAME {
                    continue;
                }
                let disposition = if rel == chunks::UPDATER_ENTRY_NAME {
                    1
                } else if !rel.contains('/') && rel.ends_with(chunks::STUB_SUFFIX) {
                    2
                } else {
                    0
                };
                out.push(TestFile::file(&rel, fs::read(&path).unwrap()).with_disposition(disposition));
            }
        }
    }
    let mut out = Vec::new();
    walk(dir, dir, &mut out);
    out
}

/// Writes AppFile records of a file list into an installed view: `current_dir/<path>` plus `current/sq.chunks`.
/// Updater and stub records go to `root/Update.exe` and `root/<Name>.exe`.
pub fn write_installed_view(root: &Path, files: &[TestFile], built: &BuiltIndex) {
    let current = root.join("current");
    fs::create_dir_all(&current).unwrap();
    for f in files {
        match (f.kind, f.disposition) {
            (0, 0) => {
                let dest = current.join(&f.path);
                fs::create_dir_all(dest.parent().unwrap()).unwrap();
                fs::write(dest, &f.data).unwrap();
            }
            (0, 1) => fs::write(root.join("Update.exe"), &f.data).unwrap(),
            (0, 2) => {
                let name = format!("{}.exe", f.path.strip_suffix(chunks::STUB_SUFFIX).unwrap());
                fs::write(root.join(name), &f.data).unwrap();
            }
            (2, _) => fs::create_dir_all(current.join(&f.path)).unwrap(),
            _ => {}
        }
    }
    fs::write(current.join(chunks::LOCAL_INDEX_NAME), &built.a).unwrap();
}

/// Deterministic pseudo-random bytes (SplitMix64 stream).
pub fn random_bytes(seed: u64, len: usize) -> Vec<u8> {
    let mut state = seed;
    let mut out = Vec::with_capacity(len + 8);
    while out.len() < len {
        state = state.wrapping_add(0x9E3779B97F4A7C15);
        let mut z = state;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58476D1CE4E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D049BB133111EB);
        z ^= z >> 31;
        out.extend_from_slice(&z.to_le_bytes());
    }
    out.truncate(len);
    out
}
