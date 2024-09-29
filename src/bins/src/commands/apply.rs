use crate::{
    shared::{self, OperationWait},
};
use velopack::{bundle::load_bundle_from_file, bundle::Manifest, locator::VelopackLocator, constants};
use anyhow::{bail, Result};
use std::path::PathBuf;

#[cfg(target_os = "linux")]
use super::apply_linux_impl::apply_package_impl;
#[cfg(target_os = "macos")]
use super::apply_osx_impl::apply_package_impl;
#[cfg(target_os = "windows")]
use super::apply_windows_impl::apply_package_impl;

pub fn apply<'a>(
    locator: &VelopackLocator,
    restart: bool,
    wait: OperationWait,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    run_hooks: bool,
) -> Result<()> {
    shared::operation_wait(wait);

    let package = package.cloned().map_or_else(|| auto_locate_package(&locator), Ok);
    
    match package {
        Ok(package) => {
            info!("Getting ready to apply package to {} ver {}: {}", 
                locator.get_manifest_id(), 
                locator.get_manifest_version_full_string(), 
                package.to_string_lossy());
            match apply_package_impl(&locator, &package, run_hooks) {
                Ok(applied_locator) => {
                    info!("Package version {} applied successfully.", applied_locator.get_manifest_version_full_string());
                    // if successful, we want to restart the new version of the app, which could have different metadata
                    if restart {
                        shared::start_package(&applied_locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
                    }
                    return Ok(());
                }
                Err(e) => {
                    error!("Error applying package: {}", e);
                }
            }
        }
        Err(e) => {
            error!("Failed to locate package ({}).", e);
        }
    }

    // an error occurred if we're here, but we still want to restart the old version of the app if it was requested
    if restart {
        shared::start_package(&locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
    }

    bail!("Apply failed, see logs for details.");
}

fn auto_locate_package(locator: &VelopackLocator) -> Result<PathBuf> {
    let packages_dir = locator.get_packages_dir_as_string();
    info!("Attempting to auto-detect package in: {}", packages_dir);
    let mut package_path: Option<PathBuf> = None;
    let mut package_manifest: Option<Manifest> = None;

    if let Ok(paths) = glob::glob(format!("{}/*.nupkg", packages_dir).as_str()) {
        for path in paths {
            if let Ok(path) = path {
                trace!("Checking package: '{}'", path.to_string_lossy());
                if let Ok(mut bun) = load_bundle_from_file(&path) {
                    if let Ok(mani) = bun.read_manifest() {
                        if package_manifest.is_none() || mani.version > package_manifest.clone().unwrap().version {
                            info!("Found {}: '{}'", mani.version, path.to_string_lossy());
                            package_manifest = Some(mani);
                            package_path = Some(path);
                        }
                    }
                }
            }
        }
    }

    if let Some(p) = package_path {
        Ok(p)
    } else {
        bail!("Unable to find/load suitable package. Provide via the --package argument.");
    }
}
