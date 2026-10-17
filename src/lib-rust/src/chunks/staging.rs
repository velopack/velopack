//! The on-disk staging layout `{PackagesDir}/staged/<id>/` (§6), the `ready.json` marker and garbage collection.

use super::index::staged_id;
use super::*;
use crate::{Error, VelopackAsset};
use semver::Version;
use serde::{Deserialize, Serialize};
use std::fs::{self, File};
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

/// Paths inside one staged directory.
#[derive(Debug, Clone)]
pub struct StagedLayout {
    /// `{PackagesDir}/staged/<staged id>`
    pub dir: PathBuf,
}

impl StagedLayout {
    /// `{PackagesDir}/staged`
    pub fn root(packages_dir: &Path) -> PathBuf {
        packages_dir.join(STAGED_DIR_NAME)
    }

    /// The layout for a staged id under a packages directory.
    pub fn new(packages_dir: &Path, staged_id: &str) -> StagedLayout {
        StagedLayout {
            dir: Self::root(packages_dir).join(staged_id),
        }
    }

    /// The layout of an existing staged directory.
    pub fn from_dir<P: AsRef<Path>>(dir: P) -> StagedLayout {
        StagedLayout {
            dir: dir.as_ref().to_path_buf(),
        }
    }

    /// The directory name, i.e. the staged id this tree claims.
    pub fn id(&self) -> String {
        self.dir.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default()
    }

    pub fn asset_json(&self) -> PathBuf {
        self.dir.join(STAGED_ASSET_JSON)
    }
    pub fn index_bin(&self) -> PathBuf {
        self.dir.join(STAGED_INDEX_BIN)
    }
    pub fn chunks_bin(&self) -> PathBuf {
        self.dir.join(STAGED_CHUNKS_BIN)
    }
    pub fn missing_bin(&self) -> PathBuf {
        self.dir.join(STAGED_MISSING_BIN)
    }
    pub fn ready_json(&self) -> PathBuf {
        self.dir.join(STAGED_READY_JSON)
    }
    pub fn current_dir(&self) -> PathBuf {
        self.dir.join(STAGED_CURRENT_DIR)
    }
    /// The updater sidecar `staged/<id>/Update.exe`.
    pub fn updater(&self) -> PathBuf {
        self.dir.join(UPDATER_INSTALLED_NAME)
    }
    pub fn stubs_dir(&self) -> PathBuf {
        self.dir.join(STAGED_STUBS_DIR)
    }
}

/// The completion marker of a staged tree (§6.3).
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct StagedReadyMarker {
    pub Format: u32,
    pub StagedId: String,
    pub IndexSHA256: String,
    pub UpdaterSHA256: String,
    pub Asset: VelopackAsset,
    pub CreatedUtc: String,
}

impl StagedReadyMarker {
    /// The staged version, if it parses.
    pub fn version(&self) -> Option<Version> {
        Version::parse(&self.Asset.Version).ok()
    }

    /// Whether a feed asset refers to this staged tree (identity = staged id + index digest, never version).
    pub fn matches_asset(&self, asset: &VelopackAsset) -> bool {
        !asset.ChunkIndexSHA256.is_empty()
            && staged_id(&asset.ChunkIndexSHA256).as_deref() == Some(self.StagedId.as_str())
            && self.IndexSHA256.eq_ignore_ascii_case(&asset.ChunkIndexSHA256)
    }
}

/// Writes a file through a `.tmp` sibling and a rename, optionally syncing it first.
pub fn write_file_atomic(path: &Path, bytes: &[u8], sync: bool) -> io::Result<()> {
    let tmp = tmp_path(path);
    {
        let mut file = File::create(&tmp)?;
        file.write_all(bytes)?;
        if sync {
            file.sync_all()?;
        }
    }
    if path.exists() {
        fs::remove_file(path)?;
    }
    fs::rename(&tmp, path)
}

fn tmp_path(path: &Path) -> PathBuf {
    let mut name = path.file_name().map(|n| n.to_os_string()).unwrap_or_default();
    name.push(".tmp");
    path.with_file_name(name)
}

/// Writes `asset.json` atomically.
pub fn write_asset_json(layout: &StagedLayout, asset: &VelopackAsset) -> Result<(), Error> {
    fs::create_dir_all(&layout.dir)?;
    let json = serde_json::to_vec(asset)?;
    write_file_atomic(&layout.asset_json(), &json, false)?;
    Ok(())
}

/// Reads `asset.json`.
pub fn read_asset_json(layout: &StagedLayout) -> Result<VelopackAsset, Error> {
    let bytes = fs::read(layout.asset_json())?;
    Ok(serde_json::from_slice(&bytes)?)
}

/// Writes `ready.json` atomically (`.tmp`, sync, rename).
pub fn write_ready_marker(layout: &StagedLayout, marker: &StagedReadyMarker) -> Result<(), Error> {
    let json = serde_json::to_vec_pretty(marker)?;
    write_file_atomic(&layout.ready_json(), &json, true)?;
    Ok(())
}

/// Reads and validates the `ready.json` of a staged directory: it must parse, have `Format == 1`, a
/// `StagedId` equal to the directory name, and `StagedId == staged_id(IndexSHA256)`.
pub fn read_ready_marker(dir: &Path) -> Option<StagedReadyMarker> {
    let layout = StagedLayout::from_dir(dir);
    let bytes = fs::read(layout.ready_json()).ok()?;
    let marker: StagedReadyMarker = serde_json::from_slice(&bytes).ok()?;
    if marker.Format != READY_MARKER_FORMAT {
        return None;
    }
    if marker.StagedId != layout.id() || staged_id(&marker.IndexSHA256).as_deref() != Some(marker.StagedId.as_str()) {
        return None;
    }
    Some(marker)
}

/// Whether `name` is a staged id: 32 lowercase hex characters.
fn is_staged_id(name: &str) -> bool {
    name.len() == 32 && name.bytes().all(|b| matches!(b, b'0'..=b'9' | b'a'..=b'f'))
}

/// Whether `path` is a real directory: not a symlink, junction, mount point or other reparse point (which a
/// directory listing must never follow into, let alone delete through).
fn is_plain_dir(path: &Path) -> bool {
    match fs::symlink_metadata(path) {
        Ok(md) => {
            #[cfg(windows)]
            {
                use std::os::windows::fs::MetadataExt;
                const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
                if md.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
                    return false;
                }
            }
            md.is_dir() && !md.file_type().is_symlink()
        }
        Err(_) => false,
    }
}

/// The directories under `{PackagesDir}/staged` named like a staged id. Anything else (other names,
/// symlinks, junctions, or a `staged` dir that is itself a reparse point) is ignored and never deleted.
fn staged_dirs(packages_dir: &Path) -> Vec<PathBuf> {
    let root = StagedLayout::root(packages_dir);
    if !is_plain_dir(&root) {
        return Vec::new();
    }
    match fs::read_dir(&root) {
        Ok(entries) => entries
            .filter_map(|e| e.ok())
            .filter(|e| is_staged_id(&e.file_name().to_string_lossy()))
            .map(|e| e.path())
            .filter(|p| is_plain_dir(p))
            .collect(),
        Err(_) => Vec::new(),
    }
}

/// Every ready staged tree under the packages directory.
pub fn list_ready_staged(packages_dir: &Path) -> Vec<(PathBuf, StagedReadyMarker)> {
    staged_dirs(packages_dir)
        .into_iter()
        .filter_map(|dir| read_ready_marker(&dir).map(|m| (dir, m)))
        .collect()
}

/// The ready staged tree with the highest version, if any.
pub fn find_ready_staged(packages_dir: &Path) -> Option<(PathBuf, StagedReadyMarker)> {
    list_ready_staged(packages_dir)
        .into_iter()
        .filter(|(_, m)| m.version().is_some())
        .max_by_key(|(_, m)| m.version())
}

/// The ready staged tree matching an asset's staged id, if any.
pub fn find_ready_staged_for(packages_dir: &Path, asset: &VelopackAsset) -> Option<(PathBuf, StagedReadyMarker)> {
    let dir = StagedLayout::new(packages_dir, &staged_id(&asset.ChunkIndexSHA256)?).dir;
    let marker = read_ready_marker(&dir)?;
    if marker.matches_asset(asset) {
        Some((dir, marker))
    } else {
        None
    }
}

fn remove_staged_dir(dir: &Path) {
    match crate::misc::retry_io(|| fs::remove_dir_all(dir)) {
        Ok(()) => info!("Deleted staged directory {:?}", dir),
        Err(e) => warn!("Failed to delete staged directory {:?}: {}", dir, e),
    }
}

/// Deletes every `staged/*` directory except the one named `keep_id` (if any).
pub fn gc_staged(packages_dir: &Path, keep_id: Option<&str>) {
    for dir in staged_dirs(packages_dir) {
        let name = dir.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
        if keep_id.map(|k| k == name).unwrap_or(false) {
            continue;
        }
        remove_staged_dir(&dir);
    }
}

/// Startup cleanup (§6.6, caller holds the packages lock): deletes ready trees whose version is not
/// above `current_version`, keeping the highest-version ready tree, and in-progress trees whose newest
/// data file is older than 7 days.
pub fn gc_staged_for_run(packages_dir: &Path, current_version: &Version) {
    let ready = list_ready_staged(packages_dir);
    let highest = ready.iter().filter_map(|(_, m)| m.version()).max();
    for dir in staged_dirs(packages_dir) {
        match ready.iter().find(|(d, _)| d == &dir) {
            Some((_, marker)) => {
                let version = marker.version();
                let is_highest = version.is_some() && version == highest;
                if !is_highest && version.map(|v| v <= *current_version).unwrap_or(true) {
                    remove_staged_dir(&dir);
                }
            }
            None => {
                let layout = StagedLayout::from_dir(&dir);
                let newest = [layout.index_bin(), layout.chunks_bin(), layout.asset_json()]
                    .iter()
                    .filter_map(|p| p.metadata().and_then(|m| m.modified()).ok())
                    .max();
                let stale = match newest {
                    Some(t) => SystemTime::now()
                        .duration_since(t)
                        .map(|d| d > Duration::from_secs(7 * 24 * 3600))
                        .unwrap_or(false),
                    None => true,
                };
                if stale {
                    remove_staged_dir(&dir);
                }
            }
        }
    }
}

/// Reads `missing.bin` (`N × u8[32]`).
pub fn read_missing_bin(path: &Path) -> io::Result<Vec<[u8; 32]>> {
    let bytes = fs::read(path)?;
    if bytes.len() % 32 != 0 {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "missing.bin has a partial hash"));
    }
    Ok(bytes
        .chunks_exact(32)
        .map(|c| {
            let mut h = [0u8; 32];
            h.copy_from_slice(c);
            h
        })
        .collect())
}

/// Writes `missing.bin`.
pub fn write_missing_bin(path: &Path, hashes: &[[u8; 32]]) -> io::Result<()> {
    let mut bytes = Vec::with_capacity(hashes.len() * 32);
    for h in hashes {
        bytes.extend_from_slice(h);
    }
    write_file_atomic(path, &bytes, true)
}

/// The current UTC time as `YYYY-MM-DDTHH:MM:SSZ`.
pub fn utc_now_rfc3339() -> String {
    let secs = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    let days = secs / 86400;
    let rem = secs % 86400;
    // civil-from-days (Howard Hinnant's algorithm)
    let z = days as i64 + 719468;
    let era = z.div_euclid(146097);
    let doe = z.rem_euclid(146097);
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    format!("{:04}-{:02}-{:02}T{:02}:{:02}:{:02}Z", y, m, d, rem / 3600, (rem % 3600) / 60, rem % 60)
}

#[cfg(all(test, windows))]
mod tests {
    use super::*;

    fn junction(link: &Path, target: &Path) {
        let status = std::process::Command::new("cmd")
            .arg("/c")
            .arg("mklink")
            .arg("/J")
            .arg(link)
            .arg(target)
            .stdout(std::process::Stdio::null())
            .status()
            .unwrap();
        assert!(status.success(), "mklink /J failed");
    }

    #[test]
    fn gc_never_follows_or_deletes_foreign_entries() {
        let tmp = tempfile::tempdir().unwrap();
        let id = "0123456789abcdef0123456789abcdef";
        let victim = tmp.path().join("victim");
        fs::create_dir_all(victim.join(id)).unwrap();
        fs::write(victim.join(id).join("keep.txt"), b"keep").unwrap();

        // `packages\staged` itself is a junction to a directory the caller does not own
        let packages = tmp.path().join("packages");
        fs::create_dir_all(&packages).unwrap();
        junction(&StagedLayout::root(&packages), &victim);
        gc_staged(&packages, None);
        assert!(victim.join(id).join("keep.txt").exists(), "gc listed and deleted through a junction");

        // inside a real `staged` dir: only plain dirs named like a staged id are collected
        let packages = tmp.path().join("packages2");
        let staged = StagedLayout::root(&packages);
        fs::create_dir_all(staged.join("not-a-staged-id")).unwrap();
        fs::create_dir_all(staged.join("fedcba9876543210fedcba9876543210")).unwrap();
        junction(&staged.join("00000000000000000000000000000000"), &victim);
        gc_staged(&packages, None);
        assert!(staged.join("not-a-staged-id").exists());
        assert!(!staged.join("fedcba9876543210fedcba9876543210").exists());
        assert!(victim.join(id).join("keep.txt").exists());
    }
}

#[cfg(test)]
mod gc_tests {
    use super::*;

    fn write_ready(packages: &Path, version: &str, hex: char) -> PathBuf {
        let sha = hex.to_string().repeat(64);
        let id = staged_id(&sha).unwrap();
        let layout = StagedLayout::new(packages, &id);
        fs::create_dir_all(&layout.dir).unwrap();
        let marker = StagedReadyMarker {
            Format: READY_MARKER_FORMAT,
            StagedId: id,
            IndexSHA256: sha.clone(),
            UpdaterSHA256: "b".repeat(64),
            Asset: VelopackAsset {
                Version: version.to_string(),
                ChunkIndexSHA256: sha,
                ..Default::default()
            },
            CreatedUtc: utc_now_rfc3339(),
        };
        write_ready_marker(&layout, &marker).unwrap();
        layout.dir
    }

    fn write_aged(path: &Path, age: Duration) {
        fs::write(path, b"x").unwrap();
        let file = fs::File::options().write(true).open(path).unwrap();
        file.set_modified(SystemTime::now() - age).unwrap();
    }

    #[test]
    fn gc_for_run_keeps_highest_and_newer_trees() {
        let tmp = tempfile::tempdir().unwrap();
        let packages = tmp.path();
        let current = Version::parse("1.0.0").unwrap();
        let highest = write_ready(packages, "2.0.0", 'a');
        let above = write_ready(packages, "1.1.0", 'b');
        let installed = write_ready(packages, "1.0.0", 'c');
        let downgrade = write_ready(packages, "0.9.0", 'd');

        let week = Duration::from_secs(7 * 24 * 3600);
        let stale = StagedLayout::new(packages, &"e".repeat(32));
        fs::create_dir_all(&stale.dir).unwrap();
        write_aged(&stale.index_bin(), week + Duration::from_secs(3600));
        write_aged(&stale.chunks_bin(), week + Duration::from_secs(3600));
        // the newest data file counts: an old index next to a freshly written blob is a resume in progress
        let fresh = StagedLayout::new(packages, &"f".repeat(32));
        fs::create_dir_all(&fresh.dir).unwrap();
        write_aged(&fresh.index_bin(), week * 2);
        write_aged(&fresh.chunks_bin(), Duration::from_secs(60));

        gc_staged_for_run(packages, &current);

        assert!(highest.exists());
        assert!(above.exists(), "a ready tree above the installed version is kept");
        assert!(!installed.exists());
        assert!(!downgrade.exists());
        assert!(!stale.dir.exists(), "an incomplete tree untouched for over 7 days is deleted");
        assert!(fresh.dir.exists(), "a recently written incomplete tree is kept for resume");
    }

    #[test]
    fn gc_for_run_keeps_highest_even_as_downgrade() {
        let tmp = tempfile::tempdir().unwrap();
        let packages = tmp.path();
        let highest = write_ready(packages, "0.9.0", 'a');
        let lower = write_ready(packages, "0.8.0", 'b');
        gc_staged_for_run(packages, &Version::parse("1.0.0").unwrap());
        assert!(highest.exists());
        assert!(!lower.exists());
    }
}
