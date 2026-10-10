use crate::{dialogs, shared, windows};
use anyhow::{bail, Context, Result};
use std::{
    ffi::OsString,
    fs,
    path::{Path, PathBuf},
    time::Duration,
};
use velopack::{bundle::load_bundle_from_file, constants, locator::VelopackLocator, process};

fn remove_temp_dir_timed(path: &PathBuf) {
    if !path.exists() {
        return;
    }
    let sw = std::time::Instant::now();
    match remove_dir_all::remove_dir_all(path) {
        Ok(()) => info!("Removed temp dir {:?} in {}ms", path, sw.elapsed().as_millis()),
        Err(e) => warn!("Failed to remove temp dir {:?} after {}ms: {}", path, sw.elapsed().as_millis(), e),
    }
}

/// Removes both temp dirs, but never the backup of the old version while the current dir is missing,
/// because then the backup is the only copy of the app.
fn remove_temp_dirs(current_dir: &Path, temp_path_new: &PathBuf, temp_path_old: &PathBuf) {
    remove_temp_dir_timed(temp_path_new);
    if current_dir.exists() {
        remove_temp_dir_timed(temp_path_old);
    } else if temp_path_old.exists() {
        error!(
            "Current dir {:?} is missing, keeping the previous version in {:?}",
            current_dir, temp_path_old
        );
    }
}

/// Moves the current dir to temp_path_old and then temp_path_new into its place.
/// If the second move fails, the old version is moved back.
fn replace_current_dir(current_dir: &Path, temp_path_old: &Path, temp_path_new: &Path, retry_delay_ms: i32) -> Result<()> {
    info!("Backing up current dir to {:?}", temp_path_old);
    shared::retry_io_ex(|| fs::rename(current_dir, temp_path_old), retry_delay_ms, 10)
        .context("Unable to start the update, because one or more running processes prevented it. Try again later, or if the issue persists, restart your computer.")?;

    info!("Replacing current dir with {:?}", temp_path_new);
    if let Err(e) = shared::retry_io_ex(|| fs::rename(temp_path_new, current_dir), retry_delay_ms, 30) {
        // restore the old version, otherwise the cleanup would delete the only copy of the app
        error!("Failed to replace current dir ({}), restoring the previous version...", e);
        if let Err(e2) = shared::retry_io_ex(|| fs::rename(temp_path_old, current_dir), retry_delay_ms, 10) {
            error!("Failed to restore the previous version ({}), it is still in {:?}", e2, temp_path_old);
            return Err(e).context(
                "Unable to complete the update, and the app was left in a broken state. You may need to re-install or repair this application manually.",
            );
        }
        info!("Restored the previous version to {:?}", current_dir);
        return Err(e).context(
            "Unable to complete the update, the previous version was restored. Try again later, or if the issue persists, restart your computer.",
        );
    }
    Ok(())
}

pub fn apply_package_impl(old_locator: &VelopackLocator, package: &PathBuf, hook_mode: super::HookRunMode) -> Result<VelopackLocator> {
    let root_path = old_locator.get_root_dir();

    let mut bundle = load_bundle_from_file(package).map_err(|e| {
        warn!("Deleting package {:?} to prevent update loop: {}", package, e);
        let _ = fs::remove_file(package);
        e
    })?;
    let new_app_manifest = bundle.read_manifest().map_err(|e| {
        warn!("Deleting package {:?} to prevent update loop: {}", package, e);
        let _ = fs::remove_file(package);
        e
    })?;
    let new_locator = old_locator.clone_self_with_new_manifest(&new_app_manifest);

    if !windows::is_directory_writable(&root_path) {
        if process::is_current_process_elevated() {
            bail!("The root directory is not writable & process is already admin. The update cannot continue.");
        } else {
            info!("Re-launching as administrator to update in {:?}", root_path);

            let packages_dir = old_locator.get_packages_dir();
            let args: Vec<OsString> = vec![
                "apply".into(),
                "--norestart".into(),
                "--package".into(),
                package.into(),
                "--rootDir".into(),
                root_path.into(),
                "--packageDir".into(),
                packages_dir.into(),
            ];
            let exe_path = std::env::current_exe()?;
            let work_dir: Option<String> = None; // same as this process
                                                 // NB: show_window must be true for dialogs to be shown
                                                 // https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-taskdialogindirect#remarks
            let process_handle = process::run_process_as_admin(&exe_path, args, work_dir, true)?;

            info!(
                "Waiting (up to 10 minutes) for elevated process (pid: {}) to exit...",
                process_handle.pid()
            );
            let result = process::wait_for_process_to_exit(process_handle, Some(Duration::from_secs(10 * 60)))?;

            match result {
                process::WaitResult::WaitTimeout => {
                    bail!("Elevated process has not exited within 10 minutes. (TIMEOUT)");
                }
                process::WaitResult::ExitCode(code) => {
                    if code != 0 {
                        bail!("Elevated process has exited with ERROR: {}.", code);
                    } else {
                        info!("Elevated process has run successfully.");
                    }
                }
                process::WaitResult::NoWaitRequired => {
                    info!("Elevated process has not required waiting.");
                }
            }
            return Ok(new_locator);
        }
    }

    // Acquire exclusive lock AFTER the self-elevation check. If we acquired it before,
    // the non-elevated parent would hold the lock while waiting for the elevated child,
    // which would also try to acquire the lock — causing a deadlock.
    let _mutex = old_locator.try_get_exclusive_lock()?;

    let old_version = old_locator.get_manifest_version();
    let new_version = new_locator.get_manifest_version();

    info!("Applying package {} to current: {}", new_version, old_version);

    if !crate::windows::prerequisite::prompt_and_install_all_missing(
        &new_app_manifest.title,
        &new_version.to_string(),
        &new_app_manifest.runtime_dependencies,
        Some(&old_version),
    )? {
        bail!("Stopping apply. Pre-requisites are missing and user cancelled.");
    }

    let current_dir = old_locator.get_current_bin_dir();
    let temp_path_new = old_locator.get_temp_dir_rand16();
    let temp_path_old = old_locator.get_temp_dir_rand16();

    // open a dialog showing progress...
    let reporter = dialogs::progress::show_apply_progress(&new_locator.get_manifest_title(), &new_locator.get_manifest_version_full_string());

    let action: Result<()> = (|| {
        // first, extract the update to temp_path_new
        fs::create_dir_all(&temp_path_new)?;
        bundle
            .extract_lib_contents_to_path(&temp_path_new, |p| {
                reporter.set_progress(p);
            })
            .map_err(|e| {
                warn!("Deleting package {:?} to prevent update loop: {}", package, e);
                let _ = fs::remove_file(package);
                e
            })?;

        reporter.set_indeterminate();

        // second, run application hooks (but don't care if it fails)
        if hook_mode == super::HookRunMode::All {
            crate::windows::run_hook(old_locator, constants::HOOK_CLI_OBSOLETE, 15);
        } else {
            info!("Skipping --veloapp-obsolete hook.");
        }

        // third, we try _REALLY HARD_ to stop the package
        let _ = shared::force_stop_package(&root_path);
        // fourth, we move the current dir to temp_path_old, and fifth, we move temp_path_new into its place
        replace_current_dir(&current_dir, &temp_path_old, &temp_path_new, 1000)?;

        // from this point on, we're past the point of no return and should not bail
        // sixth, we write the uninstall entry
        if !old_locator.get_is_portable() {
            if let Err(e) = crate::windows::registry::update_uninstall_entry(old_locator, &new_locator) {
                warn!("Failed to update uninstall entry ({}).", e);
            }
        } else {
            info!("Skipping uninstall entry for portable app.");
        }

        // seventh, we run the post-install hooks
        if hook_mode == super::HookRunMode::All || hook_mode == super::HookRunMode::PostOnly {
            crate::windows::run_hook(&new_locator, constants::HOOK_CLI_UPDATED, 15);
        } else {
            info!("Skipping --veloapp-updated hook.");
        }

        // update application shortcuts
        // should try and remove the temp dirs before recalculating the shortcuts,
        // because windows may try to use the "Distributed Link Tracking and Object Identifiers (DLT) service"
        // to update the shortcut to point at the temp/renamed location
        remove_temp_dir_timed(&temp_path_new);
        remove_temp_dir_timed(&temp_path_old);

        if !old_locator.get_is_portable() {
            crate::windows::create_or_update_manifest_lnks(&new_locator, Some(old_locator));
        }

        // done!
        info!("Package applied successfully.");

        // Sync Update.exe to root directory if we're running from a different location
        let default_update_exe = &root_path.join("Update.exe");
        let current_update_exe = std::env::current_exe()?;

        if !current_update_exe.exists() {
            warn!("Current Update.exe path does not exist, skipping default path sync (this shouldn't happen)");
            return Ok(());
        }

        match (
            default_update_exe.exists(),
            same_file::is_same_file(default_update_exe, &current_update_exe),
        ) {
            (true, Ok(true)) => {
                info!("Update.exe is already in the correct location: {:?}", current_update_exe);
            }
            (false, _) | (_, Ok(false)) => {
                info!(
                    "Running from non-default location. Attempting to update default Update.exe at: {:?}",
                    default_update_exe
                );
                match std::fs::copy(&current_update_exe, default_update_exe) {
                    Ok(_) => info!("Successfully updated default Update.exe"),
                    Err(e) => warn!("Failed to update default Update.exe: {} (non-fatal)", e),
                }
            }
            (_, Err(e)) => {
                warn!("Failed to compare Update.exe locations: {} (non-fatal)", e);
            }
        }

        // Sync stub executable(s) to root directory.
        let _ = bundle.extract_stubs_to_dir(&root_path);

        Ok(())
    })();

    reporter.close();
    remove_temp_dirs(&current_dir, &temp_path_new, &temp_path_old);
    action?;
    Ok(new_locator)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_base(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("velopack_apply_test_{}_{}", name, std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn make_app_dir(dir: &Path, version: &str) {
        fs::create_dir_all(dir).unwrap();
        fs::write(dir.join("sq.version"), version).unwrap();
    }

    #[test]
    fn test_replace_current_dir_moves_new_version_into_place() {
        let base = temp_base("swap");
        let current = base.join("current");
        let tmp_old = base.join("tmp_old");
        let tmp_new = base.join("tmp_new");
        make_app_dir(&current, "old");
        make_app_dir(&tmp_new, "new");

        replace_current_dir(&current, &tmp_old, &tmp_new, 10).unwrap();

        assert_eq!(fs::read_to_string(current.join("sq.version")).unwrap(), "new");
        assert_eq!(fs::read_to_string(tmp_old.join("sq.version")).unwrap(), "old");
        assert!(!tmp_new.exists());

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn test_replace_current_dir_restores_old_version_on_failure() {
        let base = temp_base("undo");
        let current = base.join("current");
        let tmp_old = base.join("tmp_old");
        let tmp_new = base.join("tmp_new");
        make_app_dir(&current, "old");
        // tmp_new does not exist, so the second rename fails

        assert!(replace_current_dir(&current, &tmp_old, &tmp_new, 10).is_err());
        assert_eq!(fs::read_to_string(current.join("sq.version")).unwrap(), "old");
        assert!(!tmp_old.exists());

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn test_replace_current_dir_restores_old_version_when_new_version_is_locked() {
        let base = temp_base("locked");
        let current = base.join("current");
        let tmp_old = base.join("tmp_old");
        let tmp_new = base.join("tmp_new");
        make_app_dir(&current, "old");
        make_app_dir(&tmp_new, "new");
        // an open file inside tmp_new blocks renaming it, as when antivirus scans the extracted files
        let locked = fs::File::open(tmp_new.join("sq.version")).unwrap();

        assert!(replace_current_dir(&current, &tmp_old, &tmp_new, 10).is_err());
        assert_eq!(fs::read_to_string(current.join("sq.version")).unwrap(), "old");
        assert!(!tmp_old.exists());

        drop(locked);
        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn test_remove_temp_dirs_keeps_backup_while_current_dir_is_missing() {
        let base = temp_base("cleanup");
        let current = base.join("current");
        let tmp_old = base.join("tmp_old");
        let tmp_new = base.join("tmp_new");
        make_app_dir(&tmp_old, "old");
        make_app_dir(&tmp_new, "new");

        remove_temp_dirs(&current, &tmp_new, &tmp_old);
        assert!(!tmp_new.exists());
        assert_eq!(fs::read_to_string(tmp_old.join("sq.version")).unwrap(), "old");

        make_app_dir(&current, "old");
        remove_temp_dirs(&current, &tmp_new, &tmp_old);
        assert!(!tmp_old.exists());

        let _ = fs::remove_dir_all(&base);
    }
}
