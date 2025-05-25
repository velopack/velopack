use crate::shared::dialogs;
use anyhow::{bail, Result};
use std::os::unix::fs::PermissionsExt;
use std::{fs, path::PathBuf, process::Command};
use velopack::{bundle, locator::VelopackLocator};

pub fn apply_package_impl<'a>(locator: &VelopackLocator, pkg: &PathBuf, _runhooks: bool) -> Result<VelopackLocator> {
    // on linux, the current "dir" is actually an AppImage file which we need to replace.
    info!("Loading bundle from {:?}", pkg);
    let mut bundle = bundle::load_bundle_from_file(pkg)?;
    let manifest = bundle.read_manifest()?;
    let temp_path = locator.get_temp_dir_rand16().to_string_lossy().to_string();
    let root_path_string = locator.get_root_dir_as_string();
    let script_path = format!("/var/tmp/velopack_update_{}.sh", manifest.id);
    let new_locator = locator.clone_self_with_new_manifest(&manifest);

    let action: Result<()> = (|| {
        info!("Extracting bundle to temp file: {}", temp_path);
        bundle.extract_zip_predicate_to_path(|z| z.ends_with(".AppImage"), &temp_path)?;

        info!("Chmod as executable");
        std::fs::set_permissions(&temp_path, fs::Permissions::from_mode(0o755))?;

        info!("Moving temp file to target: {}", &root_path_string);
        // we use mv instead of fs::rename / fs::copy because rename fails cross-device
        // and copy fails if the process is running (presumably because rust opens the file for writing)
        // while mv works in both cases.
        let mv_args = vec!["-f", &temp_path, &root_path_string];
        let mv_output = Command::new("mv").args(mv_args).output()?;

        if mv_output.status.success() {
            info!("AppImage moved successfully to: {}", &root_path_string);
            return Ok(());
        }

        // if the operation failed, let's try again elevated with pkexec
        error!("An error occurred ({:?}), will attempt to elevate permissions and try again...", mv_output);
        dialogs::ask_user_to_elevate(&manifest.title, &manifest.version.to_string())?;
        let script = format!("#!/bin/sh\nmv -f '{}' '{}'", temp_path, &root_path_string);
        info!("Writing script for elevation: \n{}", script);
        fs::write(&script_path, script)?;
        std::fs::set_permissions(&script_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;
        let args = vec![&script_path];
        info!("Attempting to elevate: pkexec {:?}", args);
        let elev_output = Command::new("pkexec").args(args).output()?;
        if elev_output.status.success() {
            info!("AppImage moved (elevated) to {}", &root_path_string);
            return Ok(());
        } else {
            bail!("pkexec failed with status: {:?}", elev_output);
        }
    })();
    let _ = fs::remove_file(&script_path);
    let _ = fs::remove_file(&temp_path);
    action?;
    Ok(new_locator)
}
