//! Assembly (§7.1): builds `staged/<id>/current/`, the updater and stub sidecars and `ready.json`
//! from `index.bin` + `chunks.bin` + the installed view. Never touches anything outside the staged
//! directory; reads from the installed view only.

use super::blob::ChunkBlob;
use super::index::{staged_id, ChunkIndex, Disposition, FileKind, FileRecord};
use super::inflate::{sha256, sha256_file, sha256_hex, sha256_reader};
use super::plan::LocalRoots;
use super::staging::{self, StagedLayout, StagedReadyMarker};
use super::*;
use sha2::{Digest, Sha256};
use std::collections::{HashMap, HashSet};
use std::fs::{self, File};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::Mutex;

/// What assembly needs to know about its surroundings.
#[derive(Debug, Clone)]
pub struct AssembleContext {
    /// `{PackagesDir}/staged/<id>`
    pub staged_dir: PathBuf,
    /// The installed `current/` directory (local AppFile sources and `sq.chunks`).
    pub current_dir: PathBuf,
    /// The local source for the Updater record (the running updater, or `get_update_path()` in-process).
    pub updater_local_source: PathBuf,
    /// The root directory (local Stub sources).
    pub root_dir: PathBuf,
}

/// Assembly outcome; the variants map onto the `assemble` CLI exit codes 2/3/4/1.
#[derive(Debug, thiserror::Error)]
pub enum AssembleError {
    /// Invalid input (bad index, digest or id mismatch, unsupported format): exit 2.
    #[error("Invalid staged update: {0}")]
    Invalid(String),
    /// Chunks could not be sourced; `missing.bin` was written: exit 3.
    #[error("{0} chunks could not be sourced locally")]
    Missing(usize),
    /// Disk full, access denied, readback mismatch: exit 4.
    #[error("IO error during assembly: {0}")]
    Io(#[from] io::Error),
    /// Anything else: exit 1.
    #[error("{0}")]
    Other(#[from] anyhow::Error),
}

impl AssembleError {
    /// The process exit code of the `assemble` command for this error.
    pub fn exit_code(&self) -> i32 {
        match self {
            AssembleError::Invalid(_) => 2,
            AssembleError::Missing(_) => 3,
            AssembleError::Io(_) => 4,
            AssembleError::Other(_) => 1,
        }
    }
}

fn invalid<T>(msg: impl Into<String>) -> Result<T, AssembleError> {
    Err(AssembleError::Invalid(msg.into()))
}

struct LocalChunk {
    path: PathBuf,
    offset: u64,
    length: u32,
}

struct Shared<'a> {
    remote: &'a ChunkIndex,
    layout: &'a StagedLayout,
    blob: &'a ChunkBlob,
    local_files: HashMap<[u8; 32], PathBuf>,
    local_chunks: HashMap<[u8; 32], LocalChunk>,
    jobs: Vec<FileRecord>,
    next_job: AtomicUsize,
    cancel: AtomicBool,
    missing: Mutex<HashSet<[u8; 32]>>,
    first_error: Mutex<Option<AssembleError>>,
    bytes_done: AtomicU64,
    bytes_total: u64,
    last_percent: Mutex<i16>,
    progress: &'a (dyn Fn(i16) + Sync),
}

impl Shared<'_> {
    fn add_progress(&self, bytes: u64) {
        let done = self.bytes_done.fetch_add(bytes, Ordering::Relaxed) + bytes;
        let percent = ((done.min(self.bytes_total) as u128 * 100) / self.bytes_total.max(1) as u128) as i16;
        let percent = percent.min(99);
        let mut last = self.last_percent.lock().unwrap();
        if percent > *last {
            *last = percent;
            (self.progress)(percent);
        }
    }

    fn destination(&self, record: &FileRecord) -> PathBuf {
        match record.disposition {
            Disposition::AppFile => self.layout.current_dir().join(self.remote.path(record.index)),
            Disposition::Updater => self.layout.updater(),
            Disposition::Stub => self.layout.stubs_dir().join(self.remote.installed_name(record)),
        }
    }
}

/// Builds the staged tree. Progress is 0..100 (integer percent of plaintext bytes written, 100 only on
/// success). The caller holds the packages lock (or runs under the `--parentPid` watchdog).
pub fn assemble(ctx: &AssembleContext, progress: &(dyn Fn(i16) + Sync)) -> Result<(), AssembleError> {
    let layout = StagedLayout::from_dir(&ctx.staged_dir);
    if !layout.dir.is_dir() {
        return invalid(format!("staged directory {:?} does not exist", layout.dir));
    }
    let asset = staging::read_asset_json(&layout).map_err(|e| AssembleError::Invalid(format!("asset.json is invalid: {}", e)))?;
    let index_bytes = fs::read(layout.index_bin()).map_err(|e| AssembleError::Invalid(format!("index.bin could not be read: {}", e)))?;
    let digest = sha256_hex(&sha256(&index_bytes));
    if !digest.eq_ignore_ascii_case(&asset.ChunkIndexSHA256) {
        return invalid("index.bin does not match the asset's ChunkIndexSHA256");
    }
    let id = staged_id(&digest).unwrap_or_default();
    if layout.id() != id {
        return invalid(format!("staged directory name {:?} does not match the staged id {}", layout.id(), id));
    }
    let remote = ChunkIndex::parse_trailer(&index_bytes).map_err(|e| AssembleError::Invalid(e.to_string()))?;
    let updater_record = match remote.updater_record() {
        Some(r) => r,
        None => return invalid("the package has no updater"),
    };
    let _ = fs::remove_file(layout.ready_json());

    // Local sources, declared by the installed content map and sanity-checked by length only; every
    // byte read from them is hash-verified before use.
    let roots = LocalRoots {
        current_dir: ctx.current_dir.clone(),
        updater_path: ctx.updater_local_source.clone(),
        root_dir: ctx.root_dir.clone(),
    };
    let mut local_files: HashMap<[u8; 32], PathBuf> = HashMap::new();
    let mut local_chunks: HashMap<[u8; 32], LocalChunk> = HashMap::new();
    let local = fs::read(ctx.current_dir.join(LOCAL_INDEX_NAME))
        .ok()
        .and_then(|b| match ChunkIndex::parse_local(&b) {
            Ok(idx) => Some(idx),
            Err(e) => {
                warn!("Ignoring local content map: {}", e);
                None
            }
        });
    if let Some(local) = &local {
        for record in local.files() {
            if record.kind != FileKind::File || record.chunk_count == 0 {
                continue;
            }
            let path = roots.destination(local, &record);
            match path.metadata() {
                Ok(md) if md.is_file() && md.len() == record.length => {}
                _ => continue,
            }
            local_files.entry(record.sha256).or_insert_with(|| path.clone());
            let mut offset = 0u64;
            for (_, chunk) in local.file_chunks(&record) {
                local_chunks.entry(chunk.sha256).or_insert(LocalChunk {
                    path: path.clone(),
                    offset,
                    length: chunk.length,
                });
                offset += chunk.length as u64;
            }
        }
        info!("Local content map declares {} files and {} chunks", local_files.len(), local_chunks.len());
    } else {
        info!("No local content map, everything comes from the chunk blob");
    }

    let blob = ChunkBlob::open(layout.chunks_bin())?;
    info!("Chunk blob holds {} verified chunks", blob.len());

    fs::create_dir_all(layout.current_dir())?;
    if remote.files().any(|f| f.disposition == Disposition::Stub) {
        fs::create_dir_all(layout.stubs_dir())?;
    }
    for record in remote.files().filter(|f| f.kind == FileKind::EmptyDir) {
        fs::create_dir_all(layout.current_dir().join(remote.path(record.index)))?;
    }

    let jobs: Vec<FileRecord> = remote.files().filter(|f| f.kind == FileKind::File).collect();
    let bytes_total = jobs.iter().map(|f| f.length).sum::<u64>();
    let shared = Shared {
        remote: &remote,
        layout: &layout,
        blob: &blob,
        local_files,
        local_chunks,
        jobs,
        next_job: AtomicUsize::new(0),
        cancel: AtomicBool::new(false),
        missing: Mutex::new(HashSet::new()),
        first_error: Mutex::new(None),
        bytes_done: AtomicU64::new(0),
        bytes_total,
        last_percent: Mutex::new(-1),
        progress,
    };
    shared.add_progress(0);

    let workers = ASSEMBLE_WORKERS.min(shared.jobs.len()).max(1);
    std::thread::scope(|scope| {
        for _ in 0..workers {
            scope.spawn(|| worker(&shared));
        }
    });
    if let Some(e) = shared.first_error.lock().unwrap().take() {
        return Err(e);
    }

    // Symlinks last, so their targets exist (reserved kinds, fixtures only).
    for record in remote.files().filter(|f| f.kind == FileKind::Symlink) {
        let link = layout.current_dir().join(remote.path(record.index));
        let target = remote.symlink_target(record.index).unwrap_or_default();
        if let Some(parent) = link.parent() {
            fs::create_dir_all(parent)?;
        }
        if link.symlink_metadata().is_ok() {
            remove_any(&link)?;
        }
        create_symlink(&link, &target)?;
    }

    let missing: Vec<[u8; 32]> = shared.missing.lock().unwrap().iter().copied().collect();
    if !missing.is_empty() {
        staging::write_missing_bin(&layout.missing_bin(), &missing)?;
        return Err(AssembleError::Missing(missing.len()));
    }
    let _ = fs::remove_file(layout.missing_bin());

    // Guard against a concurrent GC/refetch of the directory: the index must still be the one we built from.
    let again = fs::read(layout.index_bin()).map_err(|e| AssembleError::Invalid(format!("index.bin disappeared: {}", e)))?;
    if sha256(&again) != sha256(&index_bytes) {
        return invalid("index.bin changed during assembly");
    }
    staging::write_file_atomic(&layout.current_dir().join(LOCAL_INDEX_NAME), remote.a_bytes(), true)?;

    let updater_sha = sha256_file(shared.destination(&updater_record))?;
    let marker = StagedReadyMarker {
        Format: READY_MARKER_FORMAT,
        StagedId: id,
        IndexSHA256: digest,
        UpdaterSHA256: sha256_hex(&updater_sha),
        Asset: asset,
        CreatedUtc: staging::utc_now_rfc3339(),
    };
    staging::write_ready_marker(&layout, &marker).map_err(|e| match e {
        crate::Error::Io(e) => AssembleError::Io(e),
        other => AssembleError::Other(anyhow::anyhow!(other)),
    })?;
    (shared.progress)(100);
    Ok(())
}

fn worker(shared: &Shared<'_>) {
    loop {
        if shared.cancel.load(Ordering::Relaxed) {
            return;
        }
        let i = shared.next_job.fetch_add(1, Ordering::Relaxed);
        let record = match shared.jobs.get(i) {
            Some(r) => *r,
            None => return,
        };
        if let Err(e) = assemble_file(shared, &record) {
            let mut first = shared.first_error.lock().unwrap();
            if first.is_none() {
                *first = Some(e);
            }
            shared.cancel.store(true, Ordering::Relaxed);
            return;
        }
    }
}

fn partial_path(dest: &Path) -> PathBuf {
    let mut name = dest.file_name().map(|n| n.to_os_string()).unwrap_or_default();
    name.push(PARTIAL_SUFFIX);
    dest.with_file_name(name)
}

fn remove_any(path: &Path) -> io::Result<()> {
    match path.symlink_metadata() {
        Ok(md) if md.is_dir() => fs::remove_dir_all(path),
        Ok(_) => fs::remove_file(path),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e),
    }
}

/// Copies `src` to `out` while hashing it, returning the hash.
fn copy_hashing(src: &Path, out: &mut File) -> io::Result<[u8; 32]> {
    let mut input = File::open(src)?;
    let mut hasher = Sha256::new();
    let mut buf = vec![0u8; 64 * 1024];
    loop {
        let n = input.read(&mut buf)?;
        if n == 0 {
            break;
        }
        hasher.update(&buf[..n]);
        out.write_all(&buf[..n])?;
    }
    Ok(hasher.finalize().into())
}

fn read_local_chunk(local: &LocalChunk, hash: &[u8; 32]) -> Option<Vec<u8>> {
    let mut file = File::open(&local.path).ok()?;
    file.seek(SeekFrom::Start(local.offset)).ok()?;
    let mut buf = vec![0u8; local.length as usize];
    file.read_exact(&mut buf).ok()?;
    if sha256(&buf) == *hash {
        Some(buf)
    } else {
        None
    }
}

fn assemble_file(shared: &Shared<'_>, record: &FileRecord) -> Result<(), AssembleError> {
    let dest = shared.destination(record);
    let path = shared.remote.path(record.index);

    // Resume: a complete file from an earlier run is kept when its readback hash matches.
    if let Ok(md) = dest.symlink_metadata() {
        if md.is_file() && md.len() == record.length && sha256_file(&dest)? == record.sha256 {
            debug!("Keeping complete file '{}'", path);
            let _ = fs::remove_file(partial_path(&dest));
            shared.add_progress(record.length);
            return Ok(());
        }
        remove_any(&dest)?;
    }
    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent)?;
    }
    let part = partial_path(&dest);
    let mut out = File::create(&part)?;

    // Whole-file fast path: an unchanged local file is streamed through the hasher.
    let mut copied = false;
    if let Some(src) = shared.local_files.get(&record.sha256) {
        match copy_hashing(src, &mut out) {
            Ok(hash) if hash == record.sha256 => {
                copied = true;
                shared.add_progress(record.length);
            }
            Ok(_) => {
                debug!("Local file {:?} no longer matches '{}', assembling from chunks", src, path);
                out.set_len(0)?;
                out.seek(SeekFrom::Start(0))?;
            }
            Err(e) => {
                debug!("Local file {:?} could not be read ({}), assembling from chunks", src, e);
                out.set_len(0)?;
                out.seek(SeekFrom::Start(0))?;
            }
        }
    }

    if !copied {
        let mut file_missing: Vec<[u8; 32]> = Vec::new();
        for (_, chunk) in shared.remote.file_chunks(record) {
            if shared.cancel.load(Ordering::Relaxed) {
                return Ok(());
            }
            let data = shared
                .blob
                .read(&chunk.sha256)
                .or_else(|| shared.local_chunks.get(&chunk.sha256).and_then(|l| read_local_chunk(l, &chunk.sha256)));
            match data {
                Some(data) if file_missing.is_empty() => {
                    out.write_all(&data)?;
                    shared.add_progress(data.len() as u64);
                }
                Some(_) => {}
                None => file_missing.push(chunk.sha256),
            }
        }
        if !file_missing.is_empty() {
            drop(out);
            let _ = fs::remove_file(&part);
            info!("'{}' is missing {} chunks", path, file_missing.len());
            shared.missing.lock().unwrap().extend(file_missing);
            return Ok(());
        }
    }

    out.sync_all()?;
    drop(out);
    let mut readback = File::open(&part)?;
    if sha256_reader(&mut readback)? != record.sha256 {
        drop(readback);
        let _ = fs::remove_file(&part);
        return Err(AssembleError::Io(io::Error::new(
            io::ErrorKind::InvalidData,
            format!("readback of '{}' did not match its hash", path),
        )));
    }
    drop(readback);
    crate::misc::retry_io(|| fs::rename(&part, &dest))?;
    Ok(())
}

#[cfg(windows)]
fn create_symlink(link: &Path, target: &str) -> io::Result<()> {
    let target_path = PathBuf::from(target.trim_end_matches('/').replace('/', "\\"));
    let absolute = link.parent().unwrap_or(Path::new("")).join(&target_path);
    if absolute.is_dir() {
        std::os::windows::fs::symlink_dir(&target_path, link)
    } else {
        std::os::windows::fs::symlink_file(&target_path, link)
    }
}

#[cfg(unix)]
fn create_symlink(link: &Path, target: &str) -> io::Result<()> {
    std::os::unix::fs::symlink(target.trim_end_matches('/'), link)
}
