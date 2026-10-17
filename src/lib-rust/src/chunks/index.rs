//! Parser for the chunk index: Section A (content map) and Section B (transport map).
//!
//! The parsed index keeps the raw bytes and decodes records on demand, so memory stays
//! proportional to the input rather than to the record count.

use super::*;
use std::collections::HashSet;

/// The index failed validation; the caller treats it as absent (classic path).
#[derive(Debug, Clone, thiserror::Error)]
#[error("Invalid chunk index: {0}")]
pub struct IndexError(pub String);

fn err<T>(msg: impl Into<String>) -> Result<T, IndexError> {
    Err(IndexError(msg.into()))
}

/// Where a file record lands in the installed view.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Disposition {
    /// `current/<path>`
    AppFile = 0,
    /// root `Update.exe`
    Updater = 1,
    /// root `<name minus _ExecutionStub>.exe`
    Stub = 2,
}

/// The kind of a file record. Symlink and EmptyDir are reserved (never emitted by the Windows pack).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FileKind {
    File = 0,
    Symlink = 1,
    EmptyDir = 2,
}

/// A decoded Section A file record.
#[derive(Debug, Clone, Copy)]
pub struct FileRecord {
    /// Position of this record in the index.
    pub index: usize,
    pub disposition: Disposition,
    pub kind: FileKind,
    /// File: plaintext bytes; Symlink: UTF-8 byte length of the target; EmptyDir: 0.
    pub length: u64,
    pub first_chunk: u32,
    pub chunk_count: u32,
    pub mode: u16,
    pub sha256: [u8; 32],
    path_off: u32,
    path_len: u16,
    target_off: u32,
    target_len: u16,
}

/// A decoded Section A chunk record.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ChunkRecord {
    pub length: u32,
    pub sha256: [u8; 32],
}

/// A decoded Section B file record.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BFileRecord {
    /// 0 = stored, 8 = deflate
    pub method: u8,
    pub data_offset: u64,
    pub compressed_size: u64,
}

/// A decoded Section B chunk record: the byte range to fetch from the package.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BChunkRecord {
    pub offset: u64,
    pub length: u32,
}

/// A validated chunk index. Created from a trailer (A‖B) or a local `sq.chunks` (A only).
pub struct ChunkIndex {
    a: Vec<u8>,
    b: Vec<u8>,
    file_count: usize,
    chunk_count: usize,
    path_blob_len: usize,
}

fn u16_at(b: &[u8], off: usize) -> u16 {
    u16::from_le_bytes([b[off], b[off + 1]])
}

fn u32_at(b: &[u8], off: usize) -> u32 {
    u32::from_le_bytes([b[off], b[off + 1], b[off + 2], b[off + 3]])
}

fn u64_at(b: &[u8], off: usize) -> u64 {
    let mut arr = [0u8; 8];
    arr.copy_from_slice(&b[off..off + 8]);
    u64::from_le_bytes(arr)
}

/// Returns the first 32 hex characters of a trailer SHA-256, lowercase: the staged directory name and
/// the identity of a staged tree.
pub fn staged_id(sha256_hex: &str) -> String {
    sha256_hex.chars().take(32).collect::<String>().to_ascii_lowercase()
}

/// Maps ASCII `A..Z` to `a..z`, leaving every other byte untouched (the index uniqueness rule).
pub fn fold_ascii(bytes: &[u8]) -> Vec<u8> {
    bytes.to_ascii_lowercase()
}

const RESERVED_NAMES: [&str; 24] = [
    "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
    "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
];

fn is_reserved_stem(segment: &str) -> bool {
    let stem = segment.split('.').next().unwrap_or(segment);
    RESERVED_NAMES.iter().any(|r| r.eq_ignore_ascii_case(stem))
}

fn check_control_chars(s: &[u8]) -> Result<(), IndexError> {
    if s.is_empty() || s.len() > MAX_PATH_LEN {
        return err(format!("path length {} is out of range", s.len()));
    }
    if s.iter().any(|&c| c < 0x20 || c == 0x7F) {
        return err("path contains control characters");
    }
    Ok(())
}

fn check_segment_chars(segment: &str) -> Result<(), IndexError> {
    if segment.len() > MAX_SEGMENT_LEN {
        return err(format!("path segment '{}' is too long", segment));
    }
    if segment.chars().any(|c| matches!(c, '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|')) {
        return err(format!("path segment '{}' contains an invalid character", segment));
    }
    if segment.ends_with('.') || segment.ends_with(' ') {
        return err(format!("path segment '{}' ends with a dot or space", segment));
    }
    Ok(())
}

/// Validates an index path (§2.5 rules 1-5).
pub fn validate_path(path: &str) -> Result<(), IndexError> {
    check_control_chars(path.as_bytes())?;
    if path.starts_with('/') || path.ends_with('/') {
        return err(format!("path '{}' has a leading or trailing separator", path));
    }
    for segment in path.split('/') {
        if segment.is_empty() || segment == "." || segment == ".." {
            return err(format!("path '{}' contains an empty, '.' or '..' segment", path));
        }
        check_segment_chars(segment)?;
        if is_reserved_stem(segment) {
            return err(format!("path '{}' contains a reserved device name", path));
        }
    }
    Ok(())
}

/// Validates a symlink target against §2.5 rule 9 and returns it with `\` normalised to `/`.
pub fn validate_symlink_target(link_path: &str, target: &str) -> Result<String, IndexError> {
    check_control_chars(target.as_bytes())?;
    let target = target.replace('\\', "/");
    if target.starts_with('/') {
        return err(format!("symlink target '{}' is absolute", target));
    }
    let bytes = target.as_bytes();
    if bytes.len() >= 2 && bytes[1] == b':' {
        return err(format!("symlink target '{}' is absolute", target));
    }
    let trimmed = target.strip_suffix('/').unwrap_or(&target);
    let mut stack: Vec<&str> = link_path.split('/').collect();
    stack.pop(); // the link's own name
    for segment in trimmed.split('/') {
        if segment.is_empty() {
            return err(format!("symlink target '{}' contains an empty segment", target));
        }
        if segment == "." {
            continue;
        }
        if segment == ".." {
            if stack.pop().is_none() {
                return err(format!("symlink '{}' -> '{}' escapes the tree root", link_path, target));
            }
            continue;
        }
        check_segment_chars(segment)?;
        stack.push(segment);
    }
    Ok(target)
}

impl ChunkIndex {
    /// Parses and validates a trailer (A‖B) fetched from a package.
    pub fn parse_trailer(bytes: &[u8]) -> Result<ChunkIndex, IndexError> {
        Self::parse(bytes, true)
    }

    /// Parses and validates a local content map (A only, e.g. `current/sq.chunks`).
    pub fn parse_local(bytes: &[u8]) -> Result<ChunkIndex, IndexError> {
        Self::parse(bytes, false)
    }

    fn parse(bytes: &[u8], with_b: bool) -> Result<ChunkIndex, IndexError> {
        let total = bytes.len() as u64;
        let min_len = if with_b {
            A_HEADER_LEN + B_HEADER_LEN
        } else {
            A_HEADER_LEN
        };
        if bytes.len() < min_len {
            return err("index is too short");
        }
        if with_b && total > MAX_TRAILER_LEN {
            return err("trailer exceeds the maximum length");
        }
        if bytes[0..4] != A_MAGIC {
            return err("bad section A magic");
        }
        if u16_at(bytes, 4) != FORMAT_VERSION {
            return err(format!("unsupported format version {}", u16_at(bytes, 4)));
        }
        if bytes[6] != HASH_ALGO_SHA256 {
            return err(format!("unsupported hash algorithm {}", bytes[6]));
        }
        // bytes[7] (cdc_algo), flags and the cdc parameters are advisory.
        let file_count = u32_at(bytes, 24) as u64;
        let chunk_count = u32_at(bytes, 28) as u64;
        let path_blob_len = u32_at(bytes, 32) as u64;
        if file_count > MAX_FILES || chunk_count > MAX_CHUNKS || path_blob_len > MAX_PATH_BLOB_LEN {
            return err("index counts exceed the caps");
        }
        let a_len = A_HEADER_LEN as u64 + path_blob_len + FILE_RECORD_LEN as u64 * file_count + CHUNK_RECORD_LEN as u64 * chunk_count;
        if a_len > MAX_A_LEN {
            return err("section A exceeds the maximum length");
        }
        let expected = if with_b {
            a_len + B_HEADER_LEN as u64 + B_FILE_RECORD_LEN as u64 * file_count + B_CHUNK_RECORD_LEN as u64 * chunk_count
        } else {
            a_len
        };
        if expected != total {
            return err(format!("index length {} does not match the record counts (expected {})", total, expected));
        }

        let a_len = a_len as usize;
        let index = ChunkIndex {
            a: bytes[..a_len].to_vec(),
            b: bytes[a_len..].to_vec(),
            file_count: file_count as usize,
            chunk_count: chunk_count as usize,
            path_blob_len: path_blob_len as usize,
        };
        if with_b {
            let b = &index.b;
            if b[0..4] != B_MAGIC {
                return err("bad section B magic");
            }
            if u16_at(b, 4) != FORMAT_VERSION {
                return err("unsupported section B version");
            }
            if u32_at(b, 8) as u64 != file_count || u32_at(b, 12) as u64 != chunk_count {
                return err("section B counts do not match section A");
            }
        }
        index.validate()?;
        Ok(index)
    }

    /// The raw Section A bytes (what `current/sq.chunks` contains).
    pub fn a_bytes(&self) -> &[u8] {
        &self.a
    }

    /// The raw Section B bytes, or None for a local index.
    pub fn b_bytes(&self) -> Option<&[u8]> {
        if self.b.is_empty() {
            None
        } else {
            Some(&self.b)
        }
    }

    /// True when this index carries a transport map (it was parsed from a trailer).
    pub fn has_transport(&self) -> bool {
        !self.b.is_empty()
    }

    pub fn file_count(&self) -> usize {
        self.file_count
    }

    pub fn chunk_count(&self) -> usize {
        self.chunk_count
    }

    fn file_records_off(&self) -> usize {
        A_HEADER_LEN + self.path_blob_len
    }

    fn chunk_records_off(&self) -> usize {
        self.file_records_off() + FILE_RECORD_LEN * self.file_count
    }

    fn path_bytes_raw(&self, off: u32, len: u16) -> Option<&[u8]> {
        let start = off as usize;
        let end = start.checked_add(len as usize)?;
        if end > self.path_blob_len {
            return None;
        }
        Some(&self.a[A_HEADER_LEN + start..A_HEADER_LEN + end])
    }

    fn file_raw(&self, i: usize) -> FileRecord {
        let off = self.file_records_off() + i * FILE_RECORD_LEN;
        let r = &self.a[off..off + FILE_RECORD_LEN];
        let mut sha256 = [0u8; 32];
        sha256.copy_from_slice(&r[32..64]);
        FileRecord {
            index: i,
            disposition: match r[6] {
                1 => Disposition::Updater,
                2 => Disposition::Stub,
                _ => Disposition::AppFile,
            },
            kind: match r[7] {
                1 => FileKind::Symlink,
                2 => FileKind::EmptyDir,
                _ => FileKind::File,
            },
            length: u64_at(r, 8),
            first_chunk: u32_at(r, 16),
            chunk_count: u32_at(r, 20),
            mode: u16_at(r, 30),
            sha256,
            path_off: u32_at(r, 0),
            path_len: u16_at(r, 4),
            target_off: u32_at(r, 24),
            target_len: u16_at(r, 28),
        }
    }

    /// Decodes file record `i`. Panics if out of range.
    pub fn file(&self, i: usize) -> FileRecord {
        assert!(i < self.file_count, "file record index out of range");
        self.file_raw(i)
    }

    /// Iterates all file records in index order (ascending path order).
    pub fn files(&self) -> impl Iterator<Item = FileRecord> + '_ {
        (0..self.file_count).map(move |i| self.file_raw(i))
    }

    /// The path of file record `i` (validated at parse time).
    pub fn path(&self, i: usize) -> &str {
        let r = self.file(i);
        let bytes = self.path_bytes_raw(r.path_off, r.path_len).expect("validated path range");
        std::str::from_utf8(bytes).expect("validated utf-8 path")
    }

    /// The symlink target of record `i` (`\` normalised to `/`), or None for other kinds.
    pub fn symlink_target(&self, i: usize) -> Option<String> {
        let r = self.file(i);
        if r.kind != FileKind::Symlink {
            return None;
        }
        let bytes = self.path_bytes_raw(r.target_off, r.target_len)?;
        Some(std::str::from_utf8(bytes).ok()?.replace('\\', "/"))
    }

    /// The name of a record in the installed view, relative to its disposition root (§2.2).
    pub fn installed_name(&self, record: &FileRecord) -> String {
        let path = self.path(record.index);
        match record.disposition {
            Disposition::AppFile => path.to_string(),
            Disposition::Updater => UPDATER_INSTALLED_NAME.to_string(),
            Disposition::Stub => format!("{}.exe", path.strip_suffix(STUB_SUFFIX).unwrap_or(path)),
        }
    }

    /// Decodes chunk record `c`. Panics if out of range.
    pub fn chunk(&self, c: usize) -> ChunkRecord {
        assert!(c < self.chunk_count, "chunk record index out of range");
        let off = self.chunk_records_off() + c * CHUNK_RECORD_LEN;
        let r = &self.a[off..off + CHUNK_RECORD_LEN];
        let mut sha256 = [0u8; 32];
        sha256.copy_from_slice(&r[4..36]);
        ChunkRecord {
            length: u32_at(r, 0),
            sha256,
        }
    }

    /// Iterates all chunk records.
    pub fn chunks(&self) -> impl Iterator<Item = ChunkRecord> + '_ {
        (0..self.chunk_count).map(move |c| self.chunk(c))
    }

    /// Iterates the chunks of a file record as `(chunk index, record)`.
    pub fn file_chunks(&self, record: &FileRecord) -> impl Iterator<Item = (usize, ChunkRecord)> + '_ {
        let start = record.first_chunk as usize;
        let end = start + record.chunk_count as usize;
        (start..end).map(move |c| (c, self.chunk(c)))
    }

    /// The transport record of file `i`, or None for a local index.
    pub fn b_file(&self, i: usize) -> Option<BFileRecord> {
        if !self.has_transport() {
            return None;
        }
        assert!(i < self.file_count, "file record index out of range");
        let off = B_HEADER_LEN + i * B_FILE_RECORD_LEN;
        let r = &self.b[off..off + B_FILE_RECORD_LEN];
        Some(BFileRecord {
            method: r[0],
            data_offset: u64_at(r, 8),
            compressed_size: u64_at(r, 16),
        })
    }

    /// The byte range of chunk `c` in the package, or None for a local index.
    pub fn b_range(&self, c: usize) -> Option<BChunkRecord> {
        if !self.has_transport() {
            return None;
        }
        assert!(c < self.chunk_count, "chunk record index out of range");
        let off = B_HEADER_LEN + B_FILE_RECORD_LEN * self.file_count + c * B_CHUNK_RECORD_LEN;
        let r = &self.b[off..off + B_CHUNK_RECORD_LEN];
        Some(BChunkRecord {
            offset: u64_at(r, 0),
            length: u32_at(r, 8),
        })
    }

    /// Binary search for a path (exact bytes); records are validated to be in ascending order.
    pub fn find_file(&self, path: &str) -> Option<usize> {
        let (mut lo, mut hi) = (0usize, self.file_count);
        while lo < hi {
            let mid = lo + (hi - lo) / 2;
            match self.path(mid).as_bytes().cmp(path.as_bytes()) {
                std::cmp::Ordering::Less => lo = mid + 1,
                std::cmp::Ordering::Greater => hi = mid,
                std::cmp::Ordering::Equal => return Some(mid),
            }
        }
        None
    }

    /// The record with disposition Updater, if any.
    pub fn updater_record(&self) -> Option<FileRecord> {
        self.files().find(|f| f.disposition == Disposition::Updater)
    }

    /// Sum of the plaintext lengths of all File records.
    pub fn total_file_bytes(&self) -> u64 {
        self.files().filter(|f| f.kind == FileKind::File).map(|f| f.length).sum()
    }

    /// Remote-only check: every transport range must lie inside the asset.
    pub fn validate_asset_size(&self, asset_size: u64) -> Result<(), IndexError> {
        for c in 0..self.chunk_count {
            if let Some(r) = self.b_range(c) {
                match r.offset.checked_add(r.length as u64) {
                    Some(end) if end <= asset_size => {}
                    _ => return err(format!("chunk {} range lies outside the package", c)),
                }
            }
        }
        Ok(())
    }

    fn validate(&self) -> Result<(), IndexError> {
        for c in 0..self.chunk_count {
            let len = self.chunk(c).length;
            if len == 0 || len > MAX_CHUNK_LEN {
                return err(format!("chunk {} has an invalid length {}", c, len));
            }
        }

        let mut folded: HashSet<Vec<u8>> = HashSet::with_capacity(self.file_count);
        let mut paths: HashSet<&[u8]> = HashSet::with_capacity(self.file_count);
        let mut prev_path: &[u8] = &[];
        let mut next_chunk: u64 = 0;
        let mut updater_seen = false;

        for i in 0..self.file_count {
            let r = self.file_raw(i);
            let raw = &self.a[self.file_records_off() + i * FILE_RECORD_LEN..][..FILE_RECORD_LEN];
            let path_bytes = self
                .path_bytes_raw(r.path_off, r.path_len)
                .ok_or_else(|| IndexError(format!("record {} path is outside the blob", i)))?;
            let path = std::str::from_utf8(path_bytes).map_err(|_| IndexError(format!("record {} path is not valid UTF-8", i)))?;
            validate_path(path)?;
            if i > 0 && path_bytes <= prev_path {
                return err(format!("record '{}' is not in ascending order", path));
            }
            prev_path = path_bytes;
            let fold = fold_ascii(path_bytes);
            if fold == LOCAL_INDEX_NAME.as_bytes() {
                return err(format!("record '{}' is reserved", path));
            }
            if !folded.insert(fold) {
                return err(format!("record '{}' duplicates another record (case-insensitive)", path));
            }
            paths.insert(path_bytes);
            if raw[6] > 2 {
                return err(format!("record '{}' has an unknown disposition {}", path, raw[6]));
            }
            if raw[7] > 2 {
                return err(format!("record '{}' has an unknown kind {}", path, raw[7]));
            }

            match r.kind {
                FileKind::File => {
                    if r.target_len != 0 {
                        return err(format!("record '{}' is a file with a symlink target", path));
                    }
                    if r.chunk_count == 0 {
                        if r.length != 0 || r.first_chunk != 0 {
                            return err(format!("record '{}' has a length but no chunks", path));
                        }
                    } else {
                        if r.first_chunk as u64 != next_chunk {
                            return err(format!("record '{}' chunks are not consecutive", path));
                        }
                        let end = r.first_chunk as u64 + r.chunk_count as u64;
                        if end > self.chunk_count as u64 {
                            return err(format!("record '{}' chunk range is out of bounds", path));
                        }
                        let sum: u64 = (r.first_chunk as usize..end as usize).map(|c| self.chunk(c).length as u64).sum();
                        if sum != r.length {
                            return err(format!("record '{}' length does not match its chunks", path));
                        }
                        next_chunk = end;
                    }
                }
                FileKind::Symlink => {
                    if r.chunk_count != 0 || r.first_chunk != 0 {
                        return err(format!("symlink '{}' has chunks", path));
                    }
                    let target = self
                        .path_bytes_raw(r.target_off, r.target_len)
                        .filter(|t| !t.is_empty())
                        .ok_or_else(|| IndexError(format!("symlink '{}' target is outside the blob", path)))?;
                    if r.length != r.target_len as u64 {
                        return err(format!("symlink '{}' length does not match its target", path));
                    }
                    let target = std::str::from_utf8(target).map_err(|_| IndexError(format!("symlink '{}' target is not valid UTF-8", path)))?;
                    validate_symlink_target(path, target)?;
                }
                FileKind::EmptyDir => {
                    if r.chunk_count != 0 || r.first_chunk != 0 || r.length != 0 || r.target_len != 0 {
                        return err(format!("empty directory '{}' has content", path));
                    }
                }
            }

            match r.disposition {
                Disposition::Updater => {
                    if updater_seen || r.kind != FileKind::File || path != UPDATER_ENTRY_NAME {
                        return err(format!("record '{}' is not a valid updater record", path));
                    }
                    updater_seen = true;
                }
                Disposition::Stub => {
                    if r.kind != FileKind::File || path.contains('/') || !path.ends_with(STUB_SUFFIX) || path.len() == STUB_SUFFIX.len() {
                        return err(format!("record '{}' is not a valid stub record", path));
                    }
                    if path.ends_with(UPDATER_ENTRY_NAME) {
                        return err(format!("record '{}' would be mistaken for the updater", path));
                    }
                }
                Disposition::AppFile => {
                    if path.ends_with(UPDATER_ENTRY_NAME) {
                        return err(format!("record '{}' would be mistaken for the updater", path));
                    }
                    if path.ends_with(STUB_SUFFIX) {
                        return err(format!("record '{}' would be mistaken for a stub", path));
                    }
                }
            }
        }

        if next_chunk != self.chunk_count as u64 {
            return err("chunk records are not fully covered by file records");
        }

        for &path in &paths {
            for (pos, &c) in path.iter().enumerate() {
                if c == b'/' && paths.contains(&path[..pos]) {
                    return err(format!(
                        "record '{}' is a directory prefix of another record",
                        String::from_utf8_lossy(&path[..pos])
                    ));
                }
            }
        }

        if self.has_transport() {
            self.validate_transport()?;
        }
        Ok(())
    }

    fn validate_transport(&self) -> Result<(), IndexError> {
        for i in 0..self.file_count {
            let r = self.file_raw(i);
            let bf = self.b_file(i).expect("transport present");
            let path = self.path(i);
            if r.kind != FileKind::File || r.chunk_count == 0 {
                if bf.method != 0 || bf.data_offset != 0 || bf.compressed_size != 0 {
                    return err(format!("transport record of '{}' must be all zero", path));
                }
                continue;
            }
            match bf.method {
                0 => {
                    if bf.compressed_size != r.length {
                        return err(format!("stored entry '{}' size does not match the file length", path));
                    }
                    let mut plain_off = 0u64;
                    for (c, chunk) in self.file_chunks(&r) {
                        let bc = self.b_range(c).expect("transport present");
                        if bc.length != chunk.length || bc.offset != bf.data_offset.wrapping_add(plain_off) {
                            return err(format!("stored entry '{}' has an inconsistent chunk range", path));
                        }
                        plain_off += chunk.length as u64;
                    }
                }
                8 => {
                    // must at least hold the terminator; "smaller than length" is a writer rule, not a reader rule
                    if bf.compressed_size < ENTRY_TERMINATOR.len() as u64 {
                        return err(format!("deflate entry '{}' size {} is too small", path, bf.compressed_size));
                    }
                    let mut expect = bf.data_offset;
                    for (c, _) in self.file_chunks(&r) {
                        let bc = self.b_range(c).expect("transport present");
                        if bc.length == 0 || bc.offset != expect {
                            return err(format!("deflate entry '{}' has non-contiguous chunk ranges", path));
                        }
                        expect = expect.wrapping_add(bc.length as u64);
                    }
                    let entry_end = bf.data_offset.checked_add(bf.compressed_size);
                    if entry_end != expect.checked_add(ENTRY_TERMINATOR.len() as u64) {
                        return err(format!("deflate entry '{}' chunk ranges do not cover its data", path));
                    }
                }
                m => return err(format!("entry '{}' has an unknown method {}", path, m)),
            }
        }
        Ok(())
    }
}
