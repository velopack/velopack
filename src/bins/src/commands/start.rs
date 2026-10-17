use crate::shared::{self, OperationWait};
use anyhow::Result;
use std::ffi::OsString;
use std::fs;
use std::path::{Path, PathBuf};
use velopack::chunks::{BACKUP_DIR_PREFIX, COPY_DIR_PREFIX, SWAP_LOCK_NAME};
use velopack::locator::LocationContext;
use velopack::lockfile::LockFile;

#[allow(unused_variables)]
pub fn start(
    wait: OperationWait,
    context: LocationContext,
    exe_name: Option<&OsString>,
    exe_args: Option<Vec<OsString>>,
    legacy_args: Option<&OsString>,
) -> Result<()> {
    shared::operation_wait(wait);

    #[cfg(target_os = "windows")]
    super::start_windows_impl::start_impl(context, exe_name, exe_args, legacy_args)?;

    #[cfg(not(target_os = "windows"))]
    {
        let locator = velopack::locator::auto_locate_app_manifest(context)?;
        shared::start_package(&locator, exe_args, None)?;
    }

    Ok(())
}

/// Parses the unix timestamp out of a `current.old-<secs>-<hex>` / `current.new-<secs>-<hex>` name.
fn leftover_timestamp(name: &str, prefix: &str) -> Option<u64> {
    name.strip_prefix(prefix)?.split('-').next()?.parse().ok()
}

/// Repairs the leftovers of an interrupted swap (§7.3): takes the swap lock non-blocking and does
/// nothing if it is held (an `apply` / `apply-staged` is swapping, or root is not writable).
pub fn recover_current_dir(root: &Path) -> Result<()> {
    // A protected root (e.g. Program Files, unelevated) can never hold leftovers we could repair, so bail
    // cheaply first.
    #[cfg(windows)]
    if !velopack::misc::is_directory_writable(root) {
        debug!("Root {:?} is not writable, skipping recovery.", root);
        return Ok(());
    }
    let lock = match LockFile::try_acquire_lock_once(root.join(SWAP_LOCK_NAME)) {
        Ok(lock) => lock,
        Err(e) => {
            info!("Swap lock in {:?} is held or unavailable ({}), skipping recovery.", root, e);
            return Ok(());
        }
    };
    let result = recover_current_dir_locked(root);
    drop(lock);
    result
}

/// The recovery itself; the caller holds the swap lock. If `root/current` is missing, the
/// `current.old-*` backup with the highest timestamp is renamed back. Once `current` exists, every
/// other `current.old-*` and `current.new-*` is deleted best-effort.
pub fn recover_current_dir_locked(root: &Path) -> Result<()> {
    let current = root.join("current");
    let mut backups: Vec<(u64, PathBuf)> = Vec::new();
    let mut copies: Vec<PathBuf> = Vec::new();
    for entry in fs::read_dir(root)?.filter_map(|e| e.ok()) {
        let path = entry.path();
        if !path.is_dir() {
            continue;
        }
        let name = entry.file_name().to_string_lossy().to_string();
        if let Some(ts) = leftover_timestamp(&name, BACKUP_DIR_PREFIX) {
            backups.push((ts, path));
        } else if name.starts_with(COPY_DIR_PREFIX) {
            copies.push(path);
        }
    }

    if !current.exists() {
        let newest = backups.iter().max_by_key(|(ts, _)| *ts).map(|(_, p)| p.clone());
        match newest {
            Some(backup) => {
                warn!("{:?} is missing, restoring the backup {:?}", current, backup);
                shared::retry_io(|| fs::rename(&backup, &current))?;
                info!("Restored {:?} from {:?}", current, backup);
                backups.retain(|(_, p)| p != &backup);
            }
            None => return Ok(()),
        }
    }

    for (_, path) in backups {
        match remove_dir_all::remove_dir_all(&path) {
            Ok(()) => info!("Removed leftover backup {:?}", path),
            Err(e) => warn!("Failed to remove leftover backup {:?}: {}", path, e),
        }
    }
    for path in copies {
        match remove_dir_all::remove_dir_all(&path) {
            Ok(()) => info!("Removed leftover copy {:?}", path),
            Err(e) => warn!("Failed to remove leftover copy {:?}: {}", path, e),
        }
    }
    Ok(())
}
