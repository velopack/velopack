use crate::{
    dialogs,
    shared::{self, bundle, bundle::Manifest},
};
use anyhow::{bail, Result};
use std::{fs, path::PathBuf};

pub fn apply_package_impl<'a>(root_path: &PathBuf, app: &Manifest, package: &PathBuf, runhooks: bool) -> Result<()> {
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

    let temp_path_new = std::env::temp_dir().join(format!("velopack_{}", shared::random_string(8)));
    let temp_path_old = std::env::temp_dir().join(format!("velopack_{}", shared::random_string(8)));
    let current_dir = app.get_current_path(&root_path);

    let action: Result<()> = (|| {
        info!("Extracting bundle to {}", &temp_path_new.to_string_lossy());
        bundle.extract_lib_contents_to_path(&temp_path_new, |_| {})?;

        let _ = shared::force_stop_package(&root_path);

        let result: Result<()> = (|| {
            info!("Replacing bundle at {}", &current_dir);
            fs::rename(&current_dir, &temp_path_old)?;
            fs::rename(&temp_path_new, &current_dir)?;
            Ok(())
        })();

        match result {
            Ok(()) => {
                info!("Bundle extracted successfully to {}", &current_dir);
            }
            Err(e) => {
                let title = format!("{} Update", &manifest.title);
                let header = format!("Failed to update, application is in use");
                let body = format!(
                    "Failed to update {} to version {}. Please close any applications (e.g Explorer, cmd.exe) that may be using the application's directory, or try restarting your computer. ({})", 
                    &manifest.title, &manifest.version, e);
                dialogs::show_error(&title, Some(&header), &body);
                return Ok(()); // so that a generic error dialog is not shown.

                // if shared::is_error_permission_denied(&e) {
                //     error!("A permissions error occurred ({}), will attempt to elevate permissions and try again...", e);
                //     dialogs::ask_user_to_elevate(&manifest)?;
                //     let exe = std::env::current_exe()?;
                //     let tmp = temp_path_new.to_string_lossy();
                //     let args = vec!["swap", &current_dir, &tmp];
                //     info!("Attempting to elevate: {} {:?}", exe.to_string_lossy(), args);
                //     let mut cmd = RunAsCommand::new(&exe);
                //     cmd.gui(true);
                //     cmd.force_prompt(false);
                //     cmd.args(&args);
                //     let status = cmd.status().map_err(|z| anyhow!("Failed to restart elevated ({}).", z))?;
                //     if status.success() {
                //         info!("Bundle extracted successfully to {}", &current_dir);
                //     } else {
                //         bail!("elevated proess failed: exited with code: {}", status);
                //     }
                // } else {
                //     bail!("Failed to extract bundle ({})", e);
                // }
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
    action
}
