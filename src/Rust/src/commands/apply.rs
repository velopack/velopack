use crate::shared::{
    self,
    bundle::{self, BundleInfo, Manifest},
    dialogs,
};
use anyhow::{anyhow, bail, Result};
use glob::glob;
use runas::Command as RunAsCommand;
use std::path::PathBuf;

pub fn apply<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    restart: bool,
    wait_for_parent: bool,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    noelevate: bool,
) -> Result<()> {
    if wait_for_parent {
        if let Err(e) = shared::wait_for_parent_to_exit(60_000) {
            warn!("Failed to wait for parent process to exit ({}).", e);
        }
    }

    if let Err(e) = apply_package_impl(&root_path, &app, restart, package, exe_args.clone(), noelevate) {
        error!("Error applying package: {}", e);
        if !restart {
            return Err(e);
        }
    }

    // TODO: if the package fails to start, or fails hooks, we could roll back the install
    if restart {
        shared::start_package(&app, &root_path, exe_args, Some("VELOPACK_RESTART"))?;
    }

    Ok(())
}

fn apply_package_impl<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    restart: bool,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    noelevate: bool,
) -> Result<()> {
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
        if package.is_none() {
            bail!("Latest package found is {}, which is not newer than current version {}.", found_version, app.version);
        } else {
            warn!("Provided package is {}, which is not newer than current version {}.", found_version, app.version);
        }
    }

    #[cfg(target_os = "windows")]
    if !crate::windows::prerequisite::prompt_and_install_all_missing(&package_manifest, Some(&app.version))? {
        bail!("Stopping apply. Pre-requisites are missing.");
    }

    info!("Applying package to current: {}", found_version);

    #[cfg(target_os = "windows")]
    crate::windows::run_hook(&app, &root_path, "--veloapp-obsolete", 15);

    let current_dir = app.get_current_path(&root_path);
    if let Err(e) = shared::replace_dir_with_rollback(current_dir.clone(), || {
        if let Some(bundle) = package_bundle.take() {
            bundle.extract_lib_contents_to_path(&current_dir, |_| {})
        } else {
            bail!("No bundle could be loaded.");
        }
    }) {
        // replacing the package failed, we can try to elevate it though.
        error!("Failed to apply package: {}", e);
        if !dialogs::get_silent() && !noelevate {
            info!("Will try to elevate permissions and try again...");
            let title = format!("{} Update", app.title);
            let body = format!("{} {} is ready to be installed - you have {}, would you like to do this now?", app.title, found_version, app.version);
            if dialogs::show_ok_cancel(title.as_str(), None, body.as_str(), Some("Install Update")) {
                run_apply_elevated(restart, package, exe_args)?;
            } else {
                info!("User cancelled elevation prompt.");
                return Err(e);
            }
        } else {
            return Err(e);
        }
    }

    #[cfg(target_os = "windows")]
    if let Err(e) = package_manifest.write_uninstall_entry(root_path) {
        warn!("Failed to write uninstall entry ({}).", e);
    }

    #[cfg(target_os = "windows")]
    crate::windows::run_hook(&package_manifest, &root_path, "--veloapp-updated", 15);

    info!("Package applied successfully.");
    Ok(())
}

fn run_apply_elevated(restart: bool, package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Result<()> {
    let exe = std::env::current_exe()?;
    let mut args: Vec<String> = Vec::new();
    args.push("apply".to_string());
    args.push("--noelevate".to_string());

    if restart {
        args.push("--restart".to_string());
    }

    let package = package.map(|p| p.to_string_lossy().to_string());

    if let Some(pkg) = package {
        args.push("--package".to_string());
        args.push(pkg);
    }

    if let Some(a) = exe_args {
        args.push("--".to_string());
        a.iter().for_each(|a| args.push(a.to_string()));
    }

    info!("Attempting to elevate: {} {:?}", exe.to_string_lossy(), args);

    let mut cmd = RunAsCommand::new(&exe);
    cmd.gui(true);
    cmd.force_prompt(false);
    cmd.args(&args);
    cmd.status().map_err(|z| anyhow!("Failed to restart elevated ({}).", z))?;

    std::process::exit(0);
}
