use crate::{
    dialogs,
    shared::{self, bundle, bundle::Manifest},
    windows::locksmith,
    windows::splash,
};
use anyhow::{bail, Result};
use std::sync::mpsc;
use std::{
    fs,
    path::{Path, PathBuf},
};

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

pub fn apply_package_impl<'a>(root_path: &PathBuf, app: &Manifest, package: &PathBuf, runhooks: bool) -> Result<Manifest> {
    let bundle = bundle::load_bundle_from_file(package)?;
    let manifest = bundle.read_manifest()?;

    let found_version = (manifest.version).to_owned();
    info!("Applying package to current: {}", found_version);

    if !crate::windows::prerequisite::prompt_and_install_all_missing(&manifest, Some(&app.version))? {
        bail!("Stopping apply. Pre-requisites are missing and user cancelled.");
    }

    let packages_dir = app.get_packages_path(root_path);
    let packages_dir = Path::new(&packages_dir);
    let current_dir = app.get_current_path(root_path);
    let temp_path_new = packages_dir.join(format!("tmp_{}", shared::random_string(16)));
    let temp_path_old = packages_dir.join(format!("tmp_{}", shared::random_string(16)));

    // open a dialog showing progress...
    let (mut tx, _) = mpsc::channel::<i16>();
    if !dialogs::get_silent() {
        let title = format!("{} Update", &manifest.title);
        let message = format!("Installing update {}...", &manifest.version);
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
        if runhooks {
            crate::windows::run_hook(app, root_path, "--veloapp-obsolete", 15);
        } else {
            info!("Skipping --veloapp-obsolete hook.");
        }

        // third, we try _REALLY HARD_ to stop the package
        let _ = shared::force_stop_package(root_path);
        if winsafe::IsWindows10OrGreater() == Ok(true) && !locksmith::close_processes_locking_dir(&app.title, &current_dir) {
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
                let title = format!("{} Update", &manifest.title);
                let header = "Failed to update";
                let body = format!(
                    "Failed to update {} to version {}. Please check the logs for more details.",
                    &manifest.title, &manifest.version
                );
                dialogs::show_error(&title, Some(header), &body);

                bail!("Fatal error performing update.");
            }
        }

        // from this point on, we're past the point of no return and should not bail
        // sixth, we write the uninstall entry
        if let Err(e) = manifest.write_uninstall_entry(root_path) {
            warn!("Failed to write uninstall entry ({}).", e);
        }

        // seventh, we run the post-install hooks
        if runhooks {
            crate::windows::run_hook(&manifest, &root_path, "--veloapp-updated", 15);
        } else {
            info!("Skipping --veloapp-updated hook.");
        }

        // update application shortcuts
        crate::windows::create_or_update_manifest_lnks(root_path, app, Some(&manifest));

        // done!
        info!("Package applied successfully.");
        Ok(())
    })();

    let _ = tx.send(splash::MSG_CLOSE);
    let _ = remove_dir_all::remove_dir_all(&temp_path_new);
    let _ = remove_dir_all::remove_dir_all(&temp_path_old);
    action?;
    Ok(manifest)
}
