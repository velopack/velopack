use crate::{
    bundle,
    shared::{self, bundle::Manifest},
};
use anyhow::{bail, Result};
use std::path::PathBuf;

#[cfg(target_os = "linux")]
use super::apply_linux_impl::apply_package_impl;
#[cfg(target_os = "macos")]
use super::apply_osx_impl::apply_package_impl;
#[cfg(target_os = "windows")]
use super::apply_windows_impl::apply_package_impl;

pub fn apply<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    restart: bool,
    wait_for_parent: bool,
    wait_pid: Option<u32>,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    runhooks: bool,
) -> Result<()> {
    if let Some(pid) = wait_pid {
        if let Err(e) = shared::wait_for_pid_to_exit(pid, 60_000) {
            warn!("Failed to wait for process ({}) to exit ({}).", pid, e);
        }
    } else if wait_for_parent {
        if let Err(e) = shared::wait_for_parent_to_exit(60_000) {
            warn!("Failed to wait for parent process to exit ({}).", e);
        }
    }

    let package = package.cloned().map_or_else(|| auto_locate_package(&app, &root_path), Ok);
    match package {
        Ok(package) => {
            info!("Getting ready to apply package to {} ver {}: {}", app.id, app.version, package.to_string_lossy());
            match apply_package_impl(&root_path, &app, &package, runhooks) {
                Ok(applied_app) => {
                    info!("Package version {} applied successfully.", applied_app.version);
                    // if successful, we want to restart the new version of the app, which could have different metadata
                    if restart {
                        shared::start_package(&applied_app, &root_path, exe_args, Some("VELOPACK_RESTART"))?;
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
        shared::start_package(&app, &root_path, exe_args, Some("VELOPACK_RESTART"))?;
    }

    bail!("Apply failed, see logs for details.");
}

fn auto_locate_package(app: &Manifest, _root_path: &PathBuf) -> Result<PathBuf> {
    #[cfg(target_os = "windows")]
    let packages_dir = app.get_packages_path(_root_path);
    #[cfg(target_os = "linux")]
    let packages_dir = format!("/var/tmp/velopack/{}/packages", &app.id);
    #[cfg(target_os = "macos")]
    let packages_dir = format!("/tmp/velopack/{}/packages", &app.id);

    info!("Attempting to auto-detect package in: {}", packages_dir);
    let mut package_path: Option<PathBuf> = None;
    let mut package_manifest: Option<Manifest> = None;

    if let Ok(paths) = glob::glob(format!("{}/*.nupkg", packages_dir).as_str()) {
        for path in paths {
            if let Ok(path) = path {
                trace!("Checking package: '{}'", path.to_string_lossy());
                if let Ok(bun) = bundle::load_bundle_from_file(&path) {
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
        return Ok(p);
    } else {
        bail!("Unable to find/load suitable package. Provide via the --package argument.");
    }
}
