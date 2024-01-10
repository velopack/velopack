use crate::shared::{
    self,
    bundle::{self, Manifest},
    dialogs,
};
use anyhow::{anyhow, bail, Result};
use std::path::PathBuf;

pub fn apply<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    restart: bool,
    wait_for_parent: bool,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    noelevate: bool,
    runhooks: bool,
) -> Result<()> {
    if wait_for_parent {
        if let Err(e) = shared::wait_for_parent_to_exit(60_000) {
            warn!("Failed to wait for parent process to exit ({}).", e);
        }
    }

    if let Err(e) = apply_package_impl(&root_path, &app, package, exe_args.clone(), noelevate, runhooks) {
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

#[cfg(not(target_os = "linux"))]
fn apply_package_impl<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    noelevate: bool,
    runhooks: bool,
) -> Result<()> {
    let mut package_manifest: Option<Manifest> = None;
    let mut package_bundle: Option<bundle::BundleInfo<'a>> = None;

    if let Some(pkg) = package {
        info!("Loading package from argument '{}'.", pkg.to_string_lossy());
        let bun = bundle::load_bundle_from_file(&pkg)?;
        package_manifest = Some(bun.read_manifest()?);
        package_bundle = Some(bun);
    } else {
        info!("No package specified, searching for latest.");
        let packages_dir = app.get_packages_path(&root_path);
        if let Ok(paths) = glob::glob(format!("{}/*.nupkg", packages_dir).as_str()) {
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

    info!("Applying package to current: {}", found_version);

    #[cfg(target_os = "windows")]
    {
        if !crate::windows::prerequisite::prompt_and_install_all_missing(&package_manifest, Some(&app.version))? {
            bail!("Stopping apply. Pre-requisites are missing and user cancelled.");
        }

        if runhooks {
            crate::windows::run_hook(&app, &root_path, "--veloapp-obsolete", 15);
        } else {
            info!("Skipping --veloapp-obsolete hook.");
        }
    }

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
        info!("Will try to elevate permissions and try again...");
        ask_user_to_elevate(&package_manifest, noelevate, package, exe_args)?;
    }

    #[cfg(target_os = "windows")]
    {
        if let Err(e) = package_manifest.write_uninstall_entry(root_path) {
            warn!("Failed to write uninstall entry ({}).", e);
        }

        if runhooks {
            crate::windows::run_hook(&package_manifest, &root_path, "--veloapp-updated", 15);
        } else {
            info!("Skipping --veloapp-updated hook.");
        }
    }

    info!("Package applied successfully.");
    Ok(())
}

#[cfg(target_os = "linux")]
fn apply_package_impl<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    noelevate: bool,
    _runhooks: bool,
) -> Result<()> {
    // on linux, the current "dir" is actually an AppImage file which we need to replace.
    let pkg = package.ok_or(anyhow!("Package is required"))?;

    info!("Loading bundle from {}", pkg.to_string_lossy());
    let bundle = bundle::load_bundle_from_file(pkg)?;
    let mut tmp_path = root_path.to_string_lossy().to_string();
    tmp_path = tmp_path + "_" + shared::random_string(8).as_ref();

    info!("Extracting AppImage to temp file");

    let result: Result<()> = (|| {
        bundle.extract_zip_predicate_to_path(|z| z.ends_with(".AppImage"), &tmp_path)?;
        std::fs::set_permissions(&tmp_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;
        std::fs::rename(&tmp_path, &root_path)?;
        Ok(())
    })();

    match result {
        Ok(()) => {
            info!("AppImage extracted successfully to {}", &root_path.to_string_lossy());
        }
        Err(e) => {
            let _ = std::fs::remove_file(&tmp_path);
            if shared::is_error_permission_denied(&e) {
                error!("An error occurred {}, will attempt to elevate permissions and try again...", e);
                ask_user_to_elevate(&app, noelevate, package, exe_args)?;
            } else {
                bail!("Unable to extract AppImage ({})", e);
            }
        }
    }
    Ok(())
}

fn ask_user_to_elevate(app_to: &Manifest, noelevate: bool, package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Result<()> {
    if noelevate {
        bail!("Not allowed to ask for elevated permissions because --noelevate flag is set.");
    }

    if dialogs::get_silent() {
        bail!("Not allowed to ask for elevated permissions because --silent flag is set.");
    }

    let title = format!("{} Update", app_to.title);
    let body =
        format!("{} would like to update to version {}, but requested elevated permissions to do so. Would you like to proceed?", app_to.title, app_to.version);

    info!("Showing user elevation prompt?");
    if dialogs::show_ok_cancel(title.as_str(), None, body.as_str(), Some("Install Update")) {
        info!("User answered yes, starting elevation...");
        run_apply_elevated(package, exe_args)?;
    } else {
        bail!("User cancelled elevation prompt.");
    }
    Ok(())
}

#[cfg(not(target_os = "linux"))]
fn run_apply_elevated(package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Result<()> {
    use runas::Command as RunAsCommand;
    let exe = std::env::current_exe()?;
    let args = get_run_elevated_args(package, exe_args);

    info!("Attempting to elevate: {} {:?}", exe.to_string_lossy(), args);
    let mut cmd = RunAsCommand::new(&exe);
    cmd.gui(true);
    cmd.force_prompt(false);
    cmd.args(&args);
    let status = cmd.status().map_err(|z| anyhow!("Failed to restart elevated ({}).", z))?;
    info!("elevated proess exited with status: {}", status);
    Ok(())
}

#[cfg(target_os = "linux")]
fn run_apply_elevated(package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Result<()> {
    // in linux, as soon as the main AppImage process exits, the fs is unmounted
    // so we need to write self to a temporary file before we can use pkexec
    let temp_path = format!("/tmp/{}_update", shared::random_string(8));
    shared::copy_own_fd_to_file(&temp_path)?;
    std::fs::set_permissions(&temp_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;

    let path = std::env::var("APPIMAGE")?;
    let mut args = get_run_elevated_args(false, package, exe_args);
    args.insert(0, "env".to_string());
    args.insert(1, format!("APPIMAGE={}", path));
    args.insert(2, temp_path.to_owned());

    info!("Attempting to elevate: pkexec {:?}", args);
    let status = std::process::Command::new("pkexec").args(args).status();
    let _ = std::fs::remove_file(&temp_path);
    info!("pkexec exited with status: {}", status?);
    Ok(())
}

fn get_run_elevated_args(package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Vec<String> {
    let mut args: Vec<String> = Vec::new();
    args.push("apply".to_string());
    args.push("--noelevate".to_string());

    let package = package.map(|p| p.to_string_lossy().to_string());

    if let Some(pkg) = package {
        args.push("--package".to_string());
        args.push(pkg);
    }

    if let Some(a) = exe_args {
        args.push("--".to_string());
        a.iter().for_each(|a| args.push(a.to_string()));
    }
    args
}
