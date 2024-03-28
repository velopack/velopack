use crate::shared::{
    self,
    bundle::{self, Manifest},
    dialogs,
};
use anyhow::{bail, Result};
use std::{fs, path::PathBuf, process::Command};

pub fn apply_package_impl<'a>(root_path: &PathBuf, _app: &Manifest, pkg: &PathBuf, _runhooks: bool) -> Result<Manifest> {
    // on linux, the current "dir" is actually an AppImage file which we need to replace.
    info!("Loading bundle from {}", pkg.to_string_lossy());
    let bundle = bundle::load_bundle_from_file(pkg)?;
    let manifest = bundle.read_manifest()?;
    let temp_path = format!("/var/tmp/velopack_{}", shared::random_string(8));
    let script_path = format!("/var/tmp/velopack_update_{}.sh", manifest.id);

    let action: Result<()> = (|| {
        info!("Extracting bundle to temp file: {}", temp_path);
        bundle.extract_zip_predicate_to_path(|z| z.ends_with(".AppImage"), &temp_path)?;

        info!("Chmod as executable");
        std::fs::set_permissions(&temp_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;

        info!("Moving temp file to target: {}", &root_path.to_string_lossy());
        let mut result: Result<(), std::io::Error> = std::fs::rename(&temp_path, &root_path);
        if result.is_ok() {
            info!("AppImage moved successfully to: {}", &root_path.to_string_lossy());
            return Ok(());
        }

        let mut result_err = result.unwrap_err();

        // if we tried to rename across filesystems, let's try again with a copy.
        // ideally we check against std::io::ErrorKind::CrossesDevices but that is unstable at the moment
        if Some(18) == result_err.raw_os_error() {
            info!("Move failed (cross-device), trying again with a copy.");
            result = std::fs::copy(&temp_path, &root_path).map(|_| {});
            if result.is_ok() {
                info!("AppImage copied successfully to: {}", &root_path.to_string_lossy());
                return Ok(());
            }
            result_err = result.unwrap_err();
        }

        // if the operation failed with permission denied, let's try again elevated with pkexec
        if result_err.kind() == std::io::ErrorKind::PermissionDenied {
            error!("An error occurred {}, will attempt to elevate permissions and try again...", result_err);
            dialogs::ask_user_to_elevate(&manifest)?;
            let script = format!("#!/bin/sh\nmv -f '{}' '{}'", temp_path, &root_path.to_string_lossy());
            info!("Writing script for elevation: \n{}", script);
            fs::write(&script_path, script)?;
            std::fs::set_permissions(&script_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;
            let args = vec![&script_path];
            info!("Attempting to elevate: pkexec {:?}", args);
            let status = Command::new("pkexec").args(args).status()?;
            if status.success() {
                info!("AppImage moved (elevated) to {}", &root_path.to_string_lossy());
                return Ok(());
            } else {
                bail!("pkexec exited with status: {}", status);
            }
        }

        bail!("Failed to move the AppImage to target ({})", result_err);
    })();
    let _ = fs::remove_file(&script_path);
    let _ = fs::remove_file(&temp_path);
    action?;
    Ok(manifest)
}
