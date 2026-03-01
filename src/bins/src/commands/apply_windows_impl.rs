use crate::{
    dialogs,
    shared::{self},
    windows::{self, splash},
};
use anyhow::{bail, Context, Result};
use std::{ffi::OsString, fs, path::PathBuf};
use std::{sync::mpsc, time::Duration};
use velopack::{bundle::load_bundle_from_file, constants, locator::VelopackLocator, process};

// fn ropycopy<P1: AsRef<Path>, P2: AsRef<Path>>(source: &P1, dest: &P2) -> Result<()> {
//     let source = source.as_ref();
//     let dest = dest.as_ref();

//     // robocopy C:\source\something.new C:\destination\something /MIR /ZB /W:5 /R:5 /MT:8 /LOG:C:\logs\copy_log.txt
//     let cmd = std::process::Command::new("robocopy")
//         .arg(source)
//         .arg(dest)
//         .arg("/MIR")
//         .arg("/IS")
//         .arg("/W:1")
//         .arg("/R:5")
//         .arg("/MT:2")
//         .output()?;

//     let stdout = String::from_utf8_lossy(&cmd.stdout);
//     let stderr = String::from_utf8_lossy(&cmd.stderr);

//     let exit_code = cmd.status.code().unwrap_or(-9999);
//     if (0..7).contains(&exit_code) {
//         info!("{stdout}");
//         info!("Robocopy completed successfully.");
//     } else {
//         error!("{stdout}");
//         error!("{stderr}");
//         bail!("Robocopy failed with code: {:?}", exit_code);
//     }
//     Ok(())
// }

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
    let (mut tx, _) = mpsc::channel::<i16>();
    if !dialogs::get_silent() {
        let title = format!("{} Update", new_locator.get_manifest_title());
        let message = format!("Installing update {}...", new_locator.get_manifest_version_full_string());
        tx = splash::show_progress_dialog(title, message);
    }

    let action: Result<()> = (|| {
        // first, extract the update to temp_path_new
        fs::create_dir_all(&temp_path_new)?;
        bundle
            .extract_lib_contents_to_path(&temp_path_new, |p| {
                let _ = tx.send(p);
            })
            .map_err(|e| {
                warn!("Deleting package {:?} to prevent update loop: {}", package, e);
                let _ = fs::remove_file(package);
                e
            })?;

        let _ = tx.send(splash::MSG_INDEFINITE);

        // second, run application hooks (but don't care if it fails)
        if hook_mode == super::HookRunMode::All {
            crate::windows::run_hook(old_locator, constants::HOOK_CLI_OBSOLETE, 15);
        } else {
            info!("Skipping --veloapp-obsolete hook.");
        }

        // third, we try _REALLY HARD_ to stop the package
        let _ = shared::force_stop_package(&root_path);
        // if winsafe::IsWindows10OrGreater() == Ok(true) && !locksmith::close_processes_locking_dir(&old_locator) {
        //     bail!("Failed to close processes locking directory / user cancelled.");
        // }

        // fourth, we make as backup of the current dir to temp_path_old
        info!("Backing up current dir to {:?}", &temp_path_old);
        shared::retry_io_ex(|| fs::rename(&current_dir, &temp_path_old), 1000, 10)
            .context("Unable to start the update, because one or more running processes prevented it. Try again later, or if the issue persists, restart your computer.")?;

        // let mut requires_robocopy = false;
        // if let Err(e) = fs::rename(&current_dir, &temp_path_old) {
        //     warn!("Failed to rename current_dir to temp_path_old ({}). Retrying with robocopy...", e);
        //     ropycopy(&current_dir, &temp_path_old)?;
        //     requires_robocopy = true;
        // }

        // fifth, we try to replace the current dir with temp_path_new
        // if this fails we will yolo a rollback...
        info!("Replacing current dir with {:?}", &temp_path_new);
        shared::retry_io_ex(|| fs::rename(&temp_path_new, &current_dir), 1000, 30).context(
            "Unable to complete the update, and the app was left in a broken state. You may need to re-install or repair this application manually.",
        )?;

        // if !requires_robocopy {
        //     // if we didn't need robocopy for the backup, we don't need it for the deploy hopefully
        //     if let Err(e1) = fs::rename(&temp_path_new, &current_dir) {
        //         warn!("Failed to rename temp_path_new to current_dir ({}). Retrying with robocopy...", e1);
        //         requires_robocopy = true;
        //     }
        // }

        // if requires_robocopy {
        //     if let Err(e2) = ropycopy(&temp_path_new, &current_dir) {
        //         error!("Failed to robocopy temp_path_new to current_dir ({}). Will attempt a rollback...", e2);
        //         let _ = ropycopy(&temp_path_old, &current_dir);
        //         let _ = tx.send(splash::MSG_CLOSE);

        //         info!("Showing error dialog...");
        //         let title = format!("{} Update", &new_locator.get_manifest_title());
        //         let header = "Failed to update";
        //         let body = format!(
        //             "Failed to update {} to version {}. Please check the logs for more details.",
        //             &new_locator.get_manifest_title(),
        //             &new_locator.get_manifest_version_full_string()
        //         );
        //         dialogs::show_error(&title, Some(header), &body);

        //         bail!("Fatal error performing update.");
        //     }
        // }

        // from this point on, we're past the point of no return and should not bail
        // sixth, we write the uninstall entry
        if !old_locator.get_is_portable() {
            if let Err(e) = crate::windows::registry::update_uninstall_entry(&old_locator, &new_locator) {
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
        let _ = remove_dir_all::remove_dir_all(&temp_path_new);
        let _ = remove_dir_all::remove_dir_all(&temp_path_old);

        if !old_locator.get_is_portable() {
            crate::windows::create_or_update_manifest_lnks(&new_locator, Some(old_locator));
        }

        // done!
        info!("Package applied successfully.");

        // Sync Update.exe to root directory if we're running from a different location
        let default_update_exe = &root_path.join("Update.exe");
        let current_update_exe = std::env::current_exe()?;

        if (current_update_exe.exists()) == false {
            warn!("Current Update.exe path does not exist, skipping default path sync (this shouldn't happen)");
            return Ok(());
        }

        match (
            default_update_exe.exists(),
            same_file::is_same_file(&default_update_exe, &current_update_exe),
        ) {
            (true, Ok(true)) => {
                info!("Update.exe is already in the correct location: {:?}", &current_update_exe);
            }
            (false, _) | (_, Ok(false)) => {
                info!(
                    "Running from non-default location. Attempting to update default Update.exe at: {:?}",
                    default_update_exe
                );
                match std::fs::copy(&current_update_exe, &default_update_exe) {
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

    let _ = tx.send(splash::MSG_CLOSE);
    let _ = remove_dir_all::remove_dir_all(&temp_path_new);
    let _ = remove_dir_all::remove_dir_all(&temp_path_old);
    action?;
    Ok(new_locator)
}
