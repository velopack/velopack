//! Planning (§5.3) and range coalescing (§5.4): which chunks must be fetched, and in which requests.

use super::index::{ChunkIndex, Disposition, FileKind, FileRecord};
use super::inflate::sha256_file;
use super::{COALESCE_GAP, PLANNING_HASH_THRESHOLD, REQUEST_CAP, TRANSPORT_CEILING_DENOMINATOR, TRANSPORT_CEILING_NUMERATOR};
use std::collections::HashSet;
use std::path::{Path, PathBuf};

/// Where the installed view lives, so local sources can be resolved by disposition.
#[derive(Debug, Clone)]
pub struct LocalRoots {
    /// `current/` (AppFile records)
    pub current_dir: PathBuf,
    /// The updater that is the local source for the Updater record.
    pub updater_path: PathBuf,
    /// The root directory (Stub records live at `root/<name>.exe`).
    pub root_dir: PathBuf,
}

impl LocalRoots {
    /// The installed location of a record.
    pub fn destination(&self, index: &ChunkIndex, record: &FileRecord) -> PathBuf {
        match record.disposition {
            Disposition::AppFile => self.current_dir.join(index.path(record.index)),
            Disposition::Updater => self.updater_path.clone(),
            Disposition::Stub => self.root_dir.join(index.installed_name(record)),
        }
    }
}

/// A chunk that must be fetched, with its transport range.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NeededChunk {
    pub chunk_index: usize,
    pub sha256: [u8; 32],
    /// Plaintext length.
    pub length: u32,
    /// 0 stored, 8 deflate.
    pub method: u8,
    /// Byte offset in the package.
    pub offset: u64,
    /// Bytes to fetch.
    pub compressed_length: u32,
}

/// The fetch plan: an estimate, since assembly may still discover local mismatches.
#[derive(Debug, Clone, Default)]
pub struct Plan {
    /// Chunks to fetch, deduplicated by hash, in index order.
    pub needed: Vec<NeededChunk>,
    /// Σ compressed bytes to fetch.
    pub bytes_needed: u64,
    /// Σ plaintext bytes of the needed chunks.
    pub plaintext_needed: u64,
    /// Σ plaintext bytes of every File record in the remote index.
    pub bytes_total: u64,
    /// Remote files that are copied whole from a usable local file (nothing fetched for them).
    pub whole_file_copies: usize,
}

/// One coalesced range request.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RangeRequest {
    pub offset: u64,
    pub length: u64,
    /// The chunks inside the range, in offset order.
    pub chunks: Vec<NeededChunk>,
}

/// A local File record is usable as a source when its destination exists with exactly the recorded
/// length and, for small files, its SHA-256 matches (catches channel-tagged `sq.version` and hand edits).
pub fn local_file_usable(path: &Path, record: &FileRecord) -> bool {
    if record.kind != FileKind::File || record.chunk_count == 0 {
        return false;
    }
    match path.metadata() {
        Ok(md) if md.is_file() && md.len() == record.length => {}
        _ => return false,
    }
    if record.length <= PLANNING_HASH_THRESHOLD {
        return matches!(sha256_file(path), Ok(h) if h == record.sha256);
    }
    true
}

fn needed_chunk(remote: &ChunkIndex, chunk_index: usize) -> NeededChunk {
    let chunk = remote.chunk(chunk_index);
    let range = remote.b_range(chunk_index).expect("plan requires a trailer index with a transport map");
    let file = remote
        .files()
        .find(|f| f.chunk_count > 0 && (f.first_chunk as usize..(f.first_chunk + f.chunk_count) as usize).contains(&chunk_index));
    let method = file.and_then(|f| remote.b_file(f.index)).map(|b| b.method).unwrap_or(0);
    NeededChunk {
        chunk_index,
        sha256: chunk.sha256,
        length: chunk.length,
        method,
        offset: range.offset,
        compressed_length: range.length,
    }
}

/// Builds the plan: every chunk of every remote File record that is neither available from a usable
/// local file (whole file or chunk) nor already in the blob.
pub fn plan(remote: &ChunkIndex, local: Option<&ChunkIndex>, roots: &LocalRoots, blob_has: &dyn Fn(&[u8; 32]) -> bool) -> Plan {
    plan_with(
        remote,
        local,
        &|local, record| local_file_usable(&roots.destination(local, record), record),
        blob_has,
    )
}

/// [`plan`] with an explicit usability check for local File records (the cross-language plan vectors
/// describe usability as data instead of files on disk).
pub fn plan_with(
    remote: &ChunkIndex,
    local: Option<&ChunkIndex>,
    usable: &dyn Fn(&ChunkIndex, &FileRecord) -> bool,
    blob_has: &dyn Fn(&[u8; 32]) -> bool,
) -> Plan {
    let mut have: HashSet<[u8; 32]> = HashSet::new();
    let mut have_file: HashSet<[u8; 32]> = HashSet::new();
    if let Some(local) = local {
        for record in local.files() {
            if record.kind != FileKind::File || record.chunk_count == 0 || !usable(local, &record) {
                continue;
            }
            have_file.insert(record.sha256);
            for (_, chunk) in local.file_chunks(&record) {
                have.insert(chunk.sha256);
            }
        }
    }

    let mut plan = Plan::default();
    let mut needed_hashes: HashSet<[u8; 32]> = HashSet::new();
    for record in remote.files() {
        if record.kind != FileKind::File {
            continue;
        }
        plan.bytes_total += record.length;
        if record.length == 0 {
            continue;
        }
        if have_file.contains(&record.sha256) {
            plan.whole_file_copies += 1;
            continue;
        }
        let method = remote.b_file(record.index).map(|b| b.method).unwrap_or(0);
        for (c, chunk) in remote.file_chunks(&record) {
            if have.contains(&chunk.sha256) || blob_has(&chunk.sha256) || !needed_hashes.insert(chunk.sha256) {
                continue;
            }
            let range = remote.b_range(c).expect("plan requires a trailer index with a transport map");
            plan.needed.push(NeededChunk {
                chunk_index: c,
                sha256: chunk.sha256,
                length: chunk.length,
                method,
                offset: range.offset,
                compressed_length: range.length,
            });
            plan.bytes_needed += range.length as u64;
            plan.plaintext_needed += chunk.length as u64;
        }
    }
    plan
}

/// Builds a plan for exactly the given hashes (a `missing.bin` round). Unknown hashes are ignored.
pub fn plan_for_hashes(remote: &ChunkIndex, hashes: &[[u8; 32]]) -> Plan {
    let wanted: HashSet<[u8; 32]> = hashes.iter().copied().collect();
    let mut seen: HashSet<[u8; 32]> = HashSet::new();
    let mut plan = Plan {
        bytes_total: remote.total_file_bytes(),
        ..Default::default()
    };
    for c in 0..remote.chunk_count() {
        let hash = remote.chunk(c).sha256;
        if wanted.contains(&hash) && seen.insert(hash) {
            let needed = needed_chunk(remote, c);
            plan.bytes_needed += needed.compressed_length as u64;
            plan.plaintext_needed += needed.length as u64;
            plan.needed.push(needed);
        }
    }
    plan
}

/// True when fetching the plan would cost at least 90 % of the full package (classic path instead).
/// Integer form of `bytes_needed >= 0.9 * asset_size`, so C# and Rust never disagree on rounding.
pub fn exceeds_transport_ceiling(plan: &Plan, asset_size: u64) -> bool {
    plan.bytes_needed as u128 * TRANSPORT_CEILING_DENOMINATOR >= asset_size as u128 * TRANSPORT_CEILING_NUMERATOR
}

/// Merges needed chunks into requests: sorted by offset, a chunk joins the current request when the
/// gap to it is ≤ 32 KiB and the merged request stays ≤ 8 MiB. A lone chunk is never split here.
pub fn coalesce(plan: &Plan) -> Vec<RangeRequest> {
    let mut sorted = plan.needed.clone();
    sorted.sort_by_key(|c| (c.offset, c.compressed_length));
    let mut requests: Vec<RangeRequest> = Vec::new();
    for chunk in sorted {
        let chunk_end = chunk.offset + chunk.compressed_length as u64;
        if let Some(current) = requests.last_mut() {
            let current_end = current.offset + current.length;
            let gap = chunk.offset.saturating_sub(current_end);
            let merged = chunk_end.max(current_end) - current.offset;
            if gap <= COALESCE_GAP && merged <= REQUEST_CAP {
                current.length = merged;
                current.chunks.push(chunk);
                continue;
            }
        }
        requests.push(RangeRequest {
            offset: chunk.offset,
            length: chunk.compressed_length as u64,
            chunks: vec![chunk],
        });
    }
    requests
}
