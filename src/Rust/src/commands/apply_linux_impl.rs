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
    let temp_path = std::env::temp_dir().join(format!("velopack_{}", shared::random_string(8)));
    let script_path = std::env::temp_dir().join(format!("update_{}.sh", manifest.id));

    let action: Result<()> = (|| {
        info!("Extracting bundle to temp file: {}", temp_path.to_string_lossy());
        bundle.extract_zip_predicate_to_path(|z| z.ends_with(".AppImage"), &temp_path)?;

        info!("Chmod as executable");
        std::fs::set_permissions(&temp_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;

        info!("Extracting AppImage to temp file");
        let result: Result<()> = (|| {
            std::fs::rename(&temp_path, &root_path)?;
            Ok(())
        })();

        match result {
            Ok(()) => {
                info!("AppImage extracted successfully to {}", &root_path.to_string_lossy());
            }
            Err(e) => {
                if shared::is_error_permission_denied(&e) {
                    error!("An error occurred {}, will attempt to elevate permissions and try again...", e);
                    dialogs::ask_user_to_elevate(&manifest)?;
                    let script = format!("#!/bin/sh\nmv -f '{}' '{}'", temp_path.to_string_lossy(), &root_path.to_string_lossy());
                    info!("Writing script for elevation: \n{}", script);
                    fs::write(&script_path, script)?;
                    std::fs::set_permissions(&script_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;
                    let args = vec![&script_path];
                    info!("Attempting to elevate: pkexec {:?}", args);
                    let status = Command::new("pkexec").args(args).status()?;
                    if status.success() {
                        info!("AppImage extracted successfully to {}", &root_path.to_string_lossy());
                    } else {
                        bail!("pkexec exited with status: {}", status);
                    }
                } else {
                    bail!("Unable to extract AppImage ({})", e);
                }
            }
        }
        Ok(())
    })();
    let _ = fs::remove_file(&script_path);
    let _ = fs::remove_file(&temp_path);
    action?;
    Ok(manifest)
}
