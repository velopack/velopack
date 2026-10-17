use super::{ElevationFlags, HookRunMode};
use crate::{dialogs, shared, windows};
use anyhow::{anyhow, bail, Context, Result};
use std::{
    ffi::OsString,
    fs,
    path::{Path, PathBuf},
    time::{Duration, SystemTime, UNIX_EPOCH},
};
use velopack::{
    bundle::{read_manifest_from_string, Manifest},
    chunks::{self, staging, ChunkIndex, Disposition, FileKind, StagedLayout},
    constants,
    locator::VelopackLocator,
    lockfile::LockFile,
    misc, process,
};
use velopack_l18n::progress::ProgressReporter;

const ERROR_NOT_SAME_DEVICE: i32 = 17;

struct StagedTree {
    layout: StagedLayout,
    manifest: Manifest,
    total_bytes: u64,
}

/// Validates a staged directory before anything is touched (§7.2 step 2): marker, index digest and id,
/// `sq.chunks`, manifest, updater sidecar and a cheap size walk over every record.
fn validate_staged(dir: &Path) -> Result<StagedTree> {
    let layout = StagedLayout::from_dir(dir);
    if !layout.dir.is_dir() {
        bail!("Staged directory {:?} does not exist", dir);
    }
    let marker = staging::read_ready_marker(dir).ok_or_else(|| anyhow!("ready.json is missing or invalid"))?;
    let index_bytes = fs::read(layout.index_bin()).context("index.bin could not be read")?;
    let digest = chunks::sha256_hex(&chunks::sha256(&index_bytes));
    if !digest.eq_ignore_ascii_case(&marker.IndexSHA256) {
        bail!("index.bin does not match the ready marker");
    }
    let index = ChunkIndex::parse_trailer(&index_bytes)?;
    let local_map = fs::read(layout.current_dir().join(chunks::LOCAL_INDEX_NAME)).context("sq.chunks could not be read")?;
    if local_map != index.a_bytes() {
        bail!("current/sq.chunks does not match index.bin");
    }
    let manifest_xml = fs::read_to_string(layout.current_dir().join("sq.version")).context("sq.version could not be read")?;
    let manifest = read_manifest_from_string(&manifest_xml)?;
    let updater_sha = chunks::sha256_file(layout.updater()).context("Update.exe sidecar could not be read")?;
    if !chunks::sha256_hex(&updater_sha).eq_ignore_ascii_case(&marker.UpdaterSHA256) {
        bail!("Update.exe sidecar does not match the ready marker");
    }

    let mut total_bytes = 0u64;
    for record in index.files() {
        let dest = match record.disposition {
            Disposition::AppFile => layout.current_dir().join(index.path(record.index)),
            Disposition::Updater => layout.updater(),
            Disposition::Stub => layout.stubs_dir().join(index.installed_name(&record)),
        };
        match record.kind {
            FileKind::File => {
                let md = dest.metadata().with_context(|| format!("staged file {:?} is missing", dest))?;
                if md.len() != record.length {
                    bail!("staged file {:?} has {} bytes, expected {}", dest, md.len(), record.length);
                }
                total_bytes += record.length;
            }
            FileKind::EmptyDir => {
                if !dest.is_dir() {
                    bail!("staged directory {:?} is missing", dest);
                }
            }
            FileKind::Symlink => {
                if dest.symlink_metadata().is_err() {
                    bail!("staged symlink {:?} is missing", dest);
                }
            }
        }
    }
    Ok(StagedTree {
        layout,
        manifest,
        total_bytes,
    })
}

fn timestamped_name(prefix: &str) -> String {
    let secs = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    format!("{}{}-{}", prefix, secs, shared::random_string(4).to_ascii_lowercase())
}

fn remove_dir_timed(path: &Path) {
    if !path.exists() {
        return;
    }
    let sw = std::time::Instant::now();
    match remove_dir_all::remove_dir_all(path) {
        Ok(()) => info!("Removed dir {:?} in {}ms", path, sw.elapsed().as_millis()),
        Err(e) => warn!("Failed to remove dir {:?} after {}ms: {}", path, sw.elapsed().as_millis(), e),
    }
}

/// True when `staged_dir` lives under `root` (so a rename keeps root's ACL inheritance chain).
fn is_inside_root(staged_dir: &Path, root: &Path) -> bool {
    match (fs::canonicalize(staged_dir), fs::canonicalize(root)) {
        (Ok(s), Ok(r)) => misc::is_sub_path(s, r),
        _ => false,
    }
}

/// Best-effort free space of the volume holding `path`; None when the query fails.
fn free_space(path: &Path) -> Option<u64> {
    use ::windows::Win32::Storage::FileSystem::GetDiskFreeSpaceExW;
    use velopack::wide_strings::string_to_wide;
    let wide = string_to_wide(path);
    let mut free: u64 = 0;
    unsafe { GetDiskFreeSpaceExW(wide.as_pcwstr(), Some(&mut free), None, None).ok()? };
    Some(free)
}

/// Copies the staged `current/` tree into `root/current.new-<secs>-<hex>` with `std::fs::copy` per file,
/// so every new file inherits root's DACL natively, and verifies every file's length. Returns the new
/// directory, or deletes it and fails.
pub fn copy_under_root(staged_current: &Path, root: &Path, total_bytes: u64, reporter: &dyn ProgressReporter) -> Result<PathBuf> {
    if let Some(free) = free_space(root) {
        let need = (total_bytes as f64 * chunks::FREE_SPACE_FACTOR) as u64 + chunks::FREE_SPACE_MARGIN;
        if free < need {
            bail!("Not enough free space in {:?}: {} bytes free, {} bytes needed", root, free, need);
        }
    }
    let dest = root.join(timestamped_name(chunks::COPY_DIR_PREFIX));
    info!("Copying staged tree {:?} to {:?}", staged_current, dest);
    let result = copy_tree(staged_current, &dest, total_bytes, reporter);
    if let Err(e) = result {
        remove_dir_timed(&dest);
        return Err(e);
    }
    Ok(dest)
}

fn copy_tree(src: &Path, dest: &Path, total_bytes: u64, reporter: &dyn ProgressReporter) -> Result<()> {
    fs::create_dir_all(dest)?;
    let mut copied = 0u64;
    let mut symlinks: Vec<(PathBuf, PathBuf)> = Vec::new();
    for entry in walkdir::WalkDir::new(src).follow_links(false).min_depth(1) {
        let entry = entry?;
        let rel = entry.path().strip_prefix(src)?;
        let target = dest.join(rel);
        let file_type = entry.file_type();
        if file_type.is_symlink() {
            symlinks.push((entry.path().to_path_buf(), target));
        } else if file_type.is_dir() {
            fs::create_dir_all(&target)?;
        } else {
            let expected = entry.metadata()?.len();
            let written = misc::retry_io(|| fs::copy(entry.path(), &target))?;
            let actual = target.metadata()?.len();
            if written != expected || actual != expected {
                bail!("Copy of {:?} is incomplete ({} of {} bytes)", entry.path(), actual, expected);
            }
            copied += expected;
            reporter.set_progress(((copied.min(total_bytes) as u128 * 100) / total_bytes.max(1) as u128) as i16);
        }
    }
    for (link, target) in symlinks {
        let link_target = fs::read_link(&link)?;
        if fs::metadata(&link).map(|m| m.is_dir()).unwrap_or(false) {
            std::os::windows::fs::symlink_dir(&link_target, &target)?;
        } else {
            std::os::windows::fs::symlink_file(&link_target, &target)?;
        }
    }
    Ok(())
}

enum SwapOutcome {
    Done,
    NotSameDevice,
}

/// The crash-safe swap: `current` → `backup`, then immediately `new` → `current`. On failure of the
/// second rename the backup is moved back. `NotSameDevice` reports a junction/mount point inside root
/// so the caller can copy under root and retry.
fn swap_current_dir(current: &Path, backup: &Path, new: &Path, retry_delay_ms: i32) -> Result<SwapOutcome> {
    shared::retry_io_ex(|| fs::rename(current, backup), retry_delay_ms, 10)
        .context("Unable to start the update, because one or more running processes prevented it. Try again later, or if the issue persists, restart your computer.")?;

    let second = shared::retry_io_ex(|| fs::rename(new, current), retry_delay_ms, 30);
    let e = match second {
        Ok(()) => return Ok(SwapOutcome::Done),
        Err(e) => e,
    };
    error!("Failed to replace current dir ({}), restoring the previous version...", e);
    if let Err(e2) = shared::retry_io_ex(|| fs::rename(backup, current), retry_delay_ms, 10) {
        error!("Failed to restore the previous version ({}), it is still in {:?}", e2, backup);
        return Err(e).context(
            "Unable to complete the update, and the app was left in a broken state. You may need to re-install or repair this application manually.",
        );
    }
    info!("Restored the previous version to {:?}", current);
    if e.raw_os_error() == Some(ERROR_NOT_SAME_DEVICE) {
        return Ok(SwapOutcome::NotSameDevice);
    }
    Err(e).context(
        "Unable to complete the update, the previous version was restored. Try again later, or if the issue persists, restart your computer.",
    )
}

fn relaunch_elevated(old_locator: &VelopackLocator, staged_dir: &Path, flags: &ElevationFlags) -> Result<()> {
    let root_path = old_locator.get_root_dir();
    info!("Re-launching as administrator to update in {:?}", root_path);
    let mut args: Vec<OsString> = vec![
        "apply-staged".into(),
        "--staged".into(),
        staged_dir.into(),
        "--norestart".into(),
        "--rootDir".into(),
        root_path.into(),
        "--packageDir".into(),
        old_locator.get_packages_dir().into(),
    ];
    args.extend(flags.to_args());
    let exe_path = std::env::current_exe()?;
    let work_dir: Option<String> = None;
    // NB: show_window must be true for dialogs to be shown
    let process_handle = process::run_process_as_admin(&exe_path, args, work_dir, true)?;
    info!(
        "Waiting (up to 10 minutes) for elevated process (pid: {}) to exit...",
        process_handle.pid()
    );
    match process::wait_for_process_to_exit(process_handle, Some(Duration::from_secs(10 * 60)))? {
        process::WaitResult::WaitTimeout => bail!("Elevated process has not exited within 10 minutes. (TIMEOUT)"),
        process::WaitResult::ExitCode(code) if code != 0 => bail!("Elevated process has exited with ERROR: {}.", code),
        process::WaitResult::ExitCode(_) => info!("Elevated process has run successfully."),
        process::WaitResult::NoWaitRequired => info!("Elevated process has not required waiting."),
    }
    Ok(())
}

pub fn apply_staged_impl(
    old_locator: &VelopackLocator,
    staged_dir: &Path,
    hook_mode: HookRunMode,
    flags: &ElevationFlags,
) -> Result<VelopackLocator> {
    let root_path = old_locator.get_root_dir();

    let tree = match validate_staged(staged_dir) {
        Ok(t) => t,
        Err(e) => {
            warn!("Deleting staged directory {:?} to prevent update loop: {}", staged_dir, e);
            remove_dir_timed(staged_dir);
            return Err(e);
        }
    };
    let new_locator = old_locator.clone_self_with_new_manifest(&tree.manifest);

    if !windows::is_directory_writable(&root_path) {
        if process::is_current_process_elevated() {
            bail!("The root directory is not writable & process is already admin. The update cannot continue.");
        }
        relaunch_elevated(old_locator, staged_dir, flags)?;
        return Ok(new_locator);
    }

    // Acquire the locks AFTER the self-elevation check (the unelevated parent would otherwise hold them
    // while waiting for the elevated child).
    let _mutex = old_locator.try_get_exclusive_lock()?;
    let swap_lock =
        LockFile::try_acquire_lock(root_path.join(chunks::SWAP_LOCK_NAME)).context("Another update is swapping the application directory")?;

    let old_version = old_locator.get_manifest_version();
    let new_version = new_locator.get_manifest_version();
    info!("Applying staged version {} to current: {}", new_version, old_version);

    if !crate::windows::prerequisite::prompt_and_install_all_missing(
        &tree.manifest.title,
        &new_version.to_string(),
        &tree.manifest.runtime_dependencies,
        Some(&old_version),
    )? {
        bail!("Stopping apply. Pre-requisites are missing and user cancelled.");
    }

    // Leftovers of an interrupted swap (we hold the swap lock).
    super::recover_current_dir_locked(&root_path)?;

    let current_dir = old_locator.get_current_bin_dir();
    let reporter = dialogs::progress::show_apply_progress(&new_locator.get_manifest_title(), &new_locator.get_manifest_version_full_string());

    let action: Result<()> = (|| {
        // The swap rule: a tree created under root is renamed, anything else is copied under root first
        // so the new files inherit root's ACL.
        let inside_root = is_inside_root(staged_dir, &root_path);
        let mut new_dir = if inside_root {
            info!("Staged tree is inside the root directory, it will be renamed into place.");
            reporter.set_indeterminate();
            tree.layout.current_dir()
        } else {
            info!("Staged tree is outside the root directory, copying it under root first.");
            copy_under_root(&tree.layout.current_dir(), &root_path, tree.total_bytes, &*reporter)?
        };
        reporter.set_indeterminate();

        if hook_mode == HookRunMode::All {
            crate::windows::run_hook(old_locator, constants::HOOK_CLI_OBSOLETE, 15);
        } else {
            info!("Skipping --veloapp-obsolete hook.");
        }
        let _ = shared::force_stop_package(&root_path);

        let backup = root_path.join(timestamped_name(chunks::BACKUP_DIR_PREFIX));
        info!("Backing up current dir to {:?} and swapping in {:?}", backup, new_dir);
        if let SwapOutcome::NotSameDevice = swap_current_dir(&current_dir, &backup, &new_dir, 1000)? {
            warn!("The staged tree is on another volume (junction or mount point inside root), copying it under root.");
            if inside_root {
                new_dir = copy_under_root(&new_dir, &root_path, tree.total_bytes, &*reporter)?;
                reporter.set_indeterminate();
            }
            if let SwapOutcome::NotSameDevice = swap_current_dir(&current_dir, &backup, &new_dir, 1000)? {
                bail!("Unable to move the new version into {:?} (not the same device).", current_dir);
            }
        }

        // from this point on, we're past the point of no return and should not bail
        if !old_locator.get_is_portable() {
            if let Err(e) = crate::windows::registry::update_uninstall_entry(old_locator, &new_locator) {
                warn!("Failed to update uninstall entry ({}).", e);
            }
        } else {
            info!("Skipping uninstall entry for portable app.");
        }

        if hook_mode == HookRunMode::All || hook_mode == HookRunMode::PostOnly {
            crate::windows::run_hook(&new_locator, constants::HOOK_CLI_UPDATED, 15);
        } else {
            info!("Skipping --veloapp-updated hook.");
        }

        // Delete the backup before touching shortcuts (Distributed Link Tracking), then release the swap lock.
        remove_dir_timed(&backup);
        drop(swap_lock);

        if !old_locator.get_is_portable() {
            crate::windows::create_or_update_manifest_lnks(&new_locator, Some(old_locator));
        }

        sync_update_exe(&root_path);
        sync_stubs(&tree.layout.stubs_dir(), &root_path);

        // Cleanup: the consumed staged dir, every other staged dir, every package, temp dirs.
        let packages_dir = old_locator.get_packages_dir();
        remove_dir_timed(staged_dir);
        staging::gc_staged(&packages_dir, None);
        remove_packages(&packages_dir);
        remove_dir_timed(&old_locator.get_temp_dir_root());

        info!("Staged update applied successfully.");
        Ok(())
    })();

    reporter.close();
    action?;
    Ok(new_locator)
}

fn sync_update_exe(root_path: &Path) {
    let default_update_exe = root_path.join("Update.exe");
    let current_update_exe = match std::env::current_exe() {
        Ok(p) => p,
        Err(e) => {
            warn!("Failed to get current Update.exe path ({}), skipping default path sync", e);
            return;
        }
    };
    match same_file::is_same_file(&default_update_exe, &current_update_exe) {
        Ok(true) => info!("Update.exe is already in the correct location: {:?}", current_update_exe),
        _ => {
            info!(
                "Running from non-default location. Updating default Update.exe at: {:?}",
                default_update_exe
            );
            match shared::retry_io(|| fs::copy(&current_update_exe, &default_update_exe)) {
                Ok(_) => info!("Successfully updated default Update.exe"),
                Err(e) => warn!("Failed to update default Update.exe: {} (non-fatal)", e),
            }
        }
    }
}

fn sync_stubs(stubs_dir: &Path, root_path: &Path) {
    let entries = match fs::read_dir(stubs_dir) {
        Ok(e) => e,
        Err(_) => return,
    };
    for entry in entries.filter_map(|e| e.ok()) {
        let path = entry.path();
        if path.extension().map(|e| e.eq_ignore_ascii_case("exe")).unwrap_or(false) {
            let dest = root_path.join(entry.file_name());
            match shared::retry_io(|| fs::copy(&path, &dest)) {
                Ok(_) => info!("Synced stub executable: {:?}", dest),
                Err(e) => warn!("Failed to sync stub executable {:?}: {} (non-fatal)", dest, e),
            }
        }
    }
}

fn remove_packages(packages_dir: &Path) {
    if let Ok(entries) = fs::read_dir(packages_dir) {
        for entry in entries.filter_map(|e| e.ok()) {
            let path = entry.path();
            let is_package = path
                .extension()
                .map(|e| e.eq_ignore_ascii_case("nupkg") || e.eq_ignore_ascii_case("partial"))
                .unwrap_or(false);
            if path.is_file() && is_package {
                match fs::remove_file(&path) {
                    Ok(()) => info!("Deleted package {:?}", path),
                    Err(e) => warn!("Failed to delete package {:?}: {}", path, e),
                }
            }
        }
    }
}
