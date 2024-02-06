use crate::{
    dialogs,
    shared::{self, bundle, bundle::Manifest},
    windows::locksmith,
    windows::splash,
};
use anyhow::{bail, Result};
use std::{
    fs,
    path::{Path, PathBuf},
};

pub fn apply_package_impl<'a>(root_path: &PathBuf, app: &Manifest, package: &PathBuf, runhooks: bool) -> Result<Manifest> {
    let bundle = bundle::load_bundle_from_file(&package)?;
    let manifest = bundle.read_manifest()?;

    let found_version = (&manifest.version).to_owned();
    info!("Applying package to current: {}", found_version);

    if !crate::windows::prerequisite::prompt_and_install_all_missing(&manifest, Some(&app.version))? {
        bail!("Stopping apply. Pre-requisites are missing and user cancelled.");
    }

    if runhooks {
        crate::windows::run_hook(&app, &root_path, "--veloapp-obsolete", 15);
    } else {
        info!("Skipping --veloapp-obsolete hook.");
    }

    // we are going to be replacing the current dir with temp_path_new
    let current_dir = app.get_current_path(&root_path);

    // we extract to a temp directory inside $root/packages.tmp_XXXXX so that we know it's
    // on the same volume as the current dir, and we can rename it quickly.
    let packages_dir = app.get_packages_path(&root_path);
    let packages_dir = Path::new(&packages_dir);
    let temp_path_new = packages_dir.join(format!("tmp_{}", shared::random_string(8)));
    let temp_path_old = packages_dir.join(format!("tmp_{}", shared::random_string(8)));

    let action: Result<()> = (|| {
        info!("Extracting bundle to {}", &temp_path_new.to_string_lossy());
        fs::create_dir_all(&temp_path_new)?;

        if dialogs::get_silent() {
            bundle.extract_lib_contents_to_path(&temp_path_new, |_| {})?;
        } else {
            let title = format!("{} Update", &manifest.title);
            let message = format!("Installing update {}...", &manifest.version);
            let tx = splash::show_progress_dialog(title, message);
            bundle.extract_lib_contents_to_path(&temp_path_new, |p| {
                let _ = tx.send(p);
            })?;
            let _ = tx.send(splash::MSG_CLOSE);
        }

        let _ = shared::force_stop_package(&root_path);

        let mut has_retried = false;
        loop {
            let result: std::io::Result<()> = (|| {
                info!("Replacing bundle at {}", &current_dir);
                // so much stuff can lock folders on windows, so we retry a few times.
                shared::retry_io(|| fs::rename(&current_dir, &temp_path_old))?;
                shared::retry_io(|| fs::rename(&temp_path_new, &current_dir))?;
                Ok(())
            })();

            match result {
                Ok(()) => {
                    info!("Bundle extracted successfully to {}", &current_dir);
                    break;
                }
                Err(e) => {
                    match e.raw_os_error() {
                        Some(32) => {
                            // this is usually because the folder is locked by something on Windows.
                            // in the future, we also need to handle the case where this is a folder permissions issue
                            // and request elevation, but for now we will ask the user to close the program.
                            // it's also possible that an admin process is locking the dir, in which case we won't see
                            // it here unless we are also elevated.
                            if locksmith::close_processes_locking_dir(&app.title, &current_dir) {
                                // the processes were closed successfully, so we can retry the operation (only once)
                                if has_retried {
                                    bail!("Failed to swap current dir: {}", e);
                                }
                                has_retried = true;
                                continue;
                            }
                        }
                        _ => {
                            let title = format!("{} Update", &manifest.title);
                            let header = format!("Failed to update");
                            let body = format!("Failed to update {} to version {}. ({})", &manifest.title, &manifest.version, e);
                            dialogs::show_error(&title, Some(&header), &body);
                            return Ok(()); // so that a generic error dialog is not shown.
                        }
                    }
                }
            }
        }

        if let Err(e) = manifest.write_uninstall_entry(root_path) {
            warn!("Failed to write uninstall entry ({}).", e);
        }

        if runhooks {
            crate::windows::run_hook(&manifest, &root_path, "--veloapp-updated", 15);
        } else {
            info!("Skipping --veloapp-updated hook.");
        }

        info!("Package applied successfully.");
        Ok(())
    })();

    let _ = remove_dir_all::remove_dir_all(&temp_path_new);
    let _ = remove_dir_all::remove_dir_all(&temp_path_old);
    action?;
    Ok(manifest)
}
