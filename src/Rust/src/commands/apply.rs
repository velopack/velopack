use crate::shared::{
    self,
    bundle::{self, BundleInfo, Manifest},
};
use anyhow::{bail, Result};
use glob::glob;
use std::path::PathBuf;

pub fn apply<'a>(restart: bool, wait_for_parent: bool, package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Result<()> {
    if wait_for_parent {
        let _ = shared::wait_for_parent_to_exit(60_000); // 1 minute
    }

    let (root_path, app) = shared::detect_current_manifest()?;

    if let Err(e) = apply_package(package, &app, &root_path) {
        error!("Error applying package: {}", e);
        if !restart {
            return Err(e);
        }
    }

    if restart {
        shared::start_package(&app, &root_path, exe_args, Some("VELOPACK_RESTART"))?;
    }

    Ok(())
}

fn apply_package<'a>(package: Option<&PathBuf>, app: &Manifest, root_path: &PathBuf) -> Result<()> {
    let mut package_manifest: Option<Manifest> = None;
    let mut package_bundle: Option<BundleInfo<'a>> = None;

    #[cfg(target_os = "windows")]
    let _mutex = crate::windows::create_global_mutex(&app)?;

    if let Some(pkg) = package {
        info!("Loading package from argument '{}'.", pkg.to_string_lossy());
        let bun = bundle::load_bundle_from_file(&pkg)?;
        package_manifest = Some(bun.read_manifest()?);
        package_bundle = Some(bun);
    } else {
        info!("No package specified, searching for latest.");
        let packages_dir = app.get_packages_path(&root_path);
        if let Ok(paths) = glob(format!("{}/*.nupkg", packages_dir).as_str()) {
            for path in paths {
                if let Ok(path) = path {
                    trace!("Checking package: '{}'", path.to_string_lossy());
                    if let Ok(bun) = bundle::load_bundle_from_file(&path) {
                        if let Ok(mani) = bun.read_manifest() {
                            if package_manifest.is_none() || mani.version > package_manifest.clone().unwrap().version {
                                info!("Found {}: '{}'", mani.version, path.to_string_lossy());
                                package_manifest = Some(mani);
                                package_bundle = Some(bun);
                            }
                        }
                    }
                }
            }
        }
    }

    if package_manifest.is_none() || package_bundle.is_none() {
        bail!("Unable to find/load suitable package.");
    }

    let package_manifest = package_manifest.unwrap();

    let found_version = (&package_manifest.version).to_owned();
    if found_version <= app.version {
        bail!("Latest package found is {}, which is not newer than current version {}.", found_version, app.version);
    }

    #[cfg(target_os = "windows")]
    if !crate::windows::prerequisite::prompt_and_install_all_missing(&package_manifest, Some(&app.version))? {
        bail!("Stopping apply. Pre-requisites are missing.");
    }

    info!("Applying package to current: {}", found_version);

    #[cfg(target_os = "windows")]
    crate::windows::run_hook(&app, &root_path, "--veloapp-obsolete", 15);

    let current_dir = app.get_current_path(&root_path);
    shared::replace_dir_with_rollback(current_dir.clone(), || {
        if let Some(bundle) = package_bundle.take() {
            bundle.extract_lib_contents_to_path(&current_dir, |_| {})
        } else {
            bail!("No bundle could be loaded.");
        }
    })?;

    #[cfg(target_os = "windows")]
    crate::windows::run_hook(&package_manifest, &root_path, "--veloapp-updated", 15);

    info!("Package applied successfully.");
    Ok(())
}
