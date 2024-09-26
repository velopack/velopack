use crate::{
    dialogs,
    shared::{self},
    windows::locksmith,
    windows::splash,
};
use anyhow::{bail, Result};
use std::sync::mpsc;
use std::{
    fs,
    path::{Path, PathBuf},
};
use velopack::{bundle::load_bundle_from_file, locator::VelopackLocator};

fn ropycopy<P1: AsRef<Path>, P2: AsRef<Path>>(source: &P1, dest: &P2) -> Result<()> {
    let source = source.as_ref();
    let dest = dest.as_ref();

    // robocopy C:\source\something.new C:\destination\something /MIR /ZB /W:5 /R:5 /MT:8 /LOG:C:\logs\copy_log.txt
    let cmd = std::process::Command::new("robocopy")
        .arg(source)
        .arg(dest)
        .arg("/MIR")
        .arg("/IS")
        .arg("/W:1")
        .arg("/R:5")
        .arg("/MT:2")
        .output()?;

    let stdout = String::from_utf8_lossy(&cmd.stdout);
    let stderr = String::from_utf8_lossy(&cmd.stderr);

    let exit_code = cmd.status.code().unwrap_or(-9999);
    if (0..7).contains(&exit_code) {
        info!("{stdout}");
        info!("Robocopy completed successfully.");
    } else {
        error!("{stdout}");
        error!("{stderr}");
        bail!("Robocopy failed with code: {:?}", exit_code);
    }
    Ok(())
}

pub fn apply_package_impl(old_locator: &VelopackLocator, package: &PathBuf, run_hooks: bool) -> Result<VelopackLocator> {
    let mut bundle = load_bundle_from_file(package)?;
    let new_app_manifest = bundle.read_manifest()?;
    let new_locator = old_locator.clone_self_with_new_manifest(&new_app_manifest);

    let root_path = old_locator.get_root_dir();
    let old_version = old_locator.get_manifest_version();
    let new_version = new_locator.get_manifest_version();

    info!("Applying package {} to current: {}", new_version, old_version);

    if !crate::windows::prerequisite::prompt_and_install_all_missing(&new_app_manifest, Some(&old_version))? {
        bail!("Stopping apply. Pre-requisites are missing and user cancelled.");
    }

    let packages_dir = old_locator.get_packages_dir();
    let current_dir = old_locator.get_current_bin_dir();
    let temp_path_new = packages_dir.join(format!("tmp_{}", shared::random_string(16)));
    let temp_path_old = packages_dir.join(format!("tmp_{}", shared::random_string(16)));

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
        bundle.extract_lib_contents_to_path(&temp_path_new, |p| {
            let _ = tx.send(p);
        })?;

        let _ = tx.send(splash::MSG_INDEFINITE);

        // second, run application hooks (but don't care if it fails)
        if run_hooks {
            crate::windows::run_hook(old_locator, "--veloapp-obsolete", 15);
        } else {
            info!("Skipping --veloapp-obsolete hook.");
        }

        // third, we try _REALLY HARD_ to stop the package
        let _ = shared::force_stop_package(root_path);
        if winsafe::IsWindows10OrGreater() == Ok(true) && !locksmith::close_processes_locking_dir(&old_locator) {
            bail!("Failed to close processes locking directory / user cancelled.");
        }

        // fourth, we make as backup of the current dir to temp_path_old
        info!("Backing up current dir to {}", &temp_path_old.to_string_lossy());
        let mut requires_robocopy = false;
        if let Err(e) = fs::rename(&current_dir, &temp_path_old) {
            warn!("Failed to rename current_dir to temp_path_old ({}). Retrying with robocopy...", e);
            ropycopy(&current_dir, &temp_path_old)?;
            requires_robocopy = true;
        }

        // fifth, we try to replace the current dir with temp_path_new
        // if this fails we will yolo a rollback...
        info!("Replacing current dir with {}", &temp_path_new.to_string_lossy());

        if !requires_robocopy {
            // if we didn't need robocopy for the backup, we don't need it for the deploy hopefully
            if let Err(e1) = fs::rename(&temp_path_new, &current_dir) {
                warn!("Failed to rename temp_path_new to current_dir ({}). Retrying with robocopy...", e1);
                requires_robocopy = true;
            }
        }

        if requires_robocopy {
            if let Err(e2) = ropycopy(&temp_path_new, &current_dir) {
                error!("Failed to robocopy temp_path_new to current_dir ({}). Will attempt a rollback...", e2);
                let _ = ropycopy(&temp_path_old, &current_dir);
                let _ = tx.send(splash::MSG_CLOSE);

                info!("Showing error dialog...");
                let title = format!("{} Update", &new_locator.get_manifest_title());
                let header = "Failed to update";
                let body =
                    format!("Failed to update {} to version {}. Please check the logs for more details.",
                            &new_locator.get_manifest_title(),
                            &new_locator.get_manifest_version_full_string());
                dialogs::show_error(&title, Some(header), &body);

                bail!("Fatal error performing update.");
            }
        }

        // from this point on, we're past the point of no return and should not bail
        // sixth, we write the uninstall entry
        if !old_locator.get_is_portable() {
            if old_locator.get_manifest_id() != new_locator.get_manifest_id() {
                info!("The app ID has changed, removing old uninstall registry entry.");
                if let Err(e) = crate::windows::registry::remove_uninstall_entry(&old_locator) {
                    warn!("Failed to remove old uninstall entry ({}).", e);
                }
            }
            if let Err(e) = crate::windows::registry::write_uninstall_entry(&new_locator) {
                warn!("Failed to write new uninstall entry ({}).", e);
            }
        } else {
            info!("Skipping uninstall entry for portable app.");
        }
      
        // seventh, we run the post-install hooks
        if run_hooks {
            crate::windows::run_hook(&new_locator, "--veloapp-updated", 15);
        } else {
            info!("Skipping --veloapp-updated hook.");
        }

        // update application shortcuts
        // should try and remove the temp dirs before recalculating the shortcuts,
        // because windows may try to use the "Distributed Link Tracking and Object Identifiers (DLT) service"
        // to update the shortcut to point at the temp/renamed location
        let _ = remove_dir_all::remove_dir_all(&temp_path_new);
        let _ = remove_dir_all::remove_dir_all(&temp_path_old);
        
        crate::windows::create_or_update_manifest_lnks(&new_locator, Some(old_locator));

        // done!
        info!("Package applied successfully.");
        Ok(())
    })();

    let _ = tx.send(splash::MSG_CLOSE);
    let _ = remove_dir_all::remove_dir_all(&temp_path_new);
    let _ = remove_dir_all::remove_dir_all(&temp_path_old);
    action?;
    Ok(new_locator)
}
