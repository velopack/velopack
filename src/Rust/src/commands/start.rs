use crate::{
    dialogs,
    shared::{self, bundle},
    windows,
};
use anyhow::{anyhow, bail, Result};
use std::{
    fs,
    path::{Path, PathBuf},
};

pub fn start(wait_for_parent: bool, exe_name: Option<&String>, exe_args: Option<Vec<&str>>, legacy_args: Option<&String>) -> Result<()> {
    if legacy_args.is_some() && exe_args.is_some() {
        bail!("Cannot use both legacy args and new args format.");
    }

    if wait_for_parent {
        if let Err(e) = shared::wait_for_parent_to_exit(60_000) {
            warn!("Failed to wait for parent process to exit ({}).", e);
        }
    }

    let (root_dir, app) = shared::detect_current_manifest()?;

    match shared::has_app_prefixed_folder(&root_dir) {
        Ok(has_prefix) => {
            if has_prefix {
                info!("This is a legacy app. Will try and upgrade it now.");

                // if started by legacy Squirrel, the working dir of Update.exe may be inside the app-* folder,
                // meaning we can not clean up properly.
                std::env::set_current_dir(&root_dir)?;

                if let Err(e) = try_legacy_migration(&root_dir, &app) {
                    warn!("Failed to migrate legacy app ({}).", e);
                    dialogs::show_error(
                        &app.title,
                        Some("Unable to start app"),
                        "This app installation has been corrupted and cannot be started. Please reinstall the app.",
                    );
                    return Err(e);
                }

                // we can't run the normal start command, because legacy squirrel might provide an "exe name" to restart
                // which no longer exists in the package
                let (root_dir, app) = shared::detect_current_manifest()?;
                shared::start_package(&app, &root_dir, exe_args, Some("VELOPACK_RESTART"))?;
                return Ok(());
            }
        }
        Err(e) => warn!("Failed legacy check ({}).", e),
    }

    let current = app.get_current_path(&root_dir);
    let exe_to_execute = if let Some(exe) = exe_name {
        Path::new(&current).join(exe)
    } else {
        let exe = app.get_main_exe_path(&root_dir);
        Path::new(&exe).to_path_buf()
    };

    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{}'", exe_to_execute.to_string_lossy());
    }

    info!("About to launch: '{}' in dir '{}'", exe_to_execute.to_string_lossy(), current);

    if let Some(args) = exe_args {
        crate::windows::run_process(exe_to_execute, args, current)?;
    } else if let Some(args) = legacy_args {
        crate::windows::run_process_raw_args(exe_to_execute, args, current)?;
    } else {
        crate::windows::run_process(exe_to_execute, vec![], current)?;
    };

    Ok(())
}

fn try_legacy_migration(root_dir: &PathBuf, app: &bundle::Manifest) -> Result<()> {
    let package = shared::find_latest_full_package(&root_dir).ok_or_else(|| anyhow!("Unable to find latest full package."))?;
    let bundle = bundle::load_bundle_from_file(&package.file_path)?;
    let _bundle_manifest = bundle.read_manifest()?; // this verifies it's a bundle we support
    warn!("This application is installed in a folder prefixed with 'app-'. Attempting to migrate...");
    let _ = shared::force_stop_package(&root_dir);
    let current_dir = app.get_current_path(&root_dir);

    if !Path::new(&current_dir).exists() {
        info!("Renaming latest app-* folder to current.");
        if let Some((latest_app_dir, _latest_ver)) = shared::get_latest_app_version_folder(&root_dir)? {
            fs::rename(&latest_app_dir, &current_dir)?;
        }
    }

    info!("Applying latest full package...");
    let buf = Path::new(&package.file_path).to_path_buf();
    super::apply(&root_dir, &app, false, false, Some(&buf), None, false)?;

    info!("Removing old app-* folders...");
    shared::delete_app_prefixed_folders(&root_dir)?;
    let _ = remove_dir_all::remove_dir_all(root_dir.join("staging"));

    info!("Removing old shortcuts...");
    if let Err(e) = windows::remove_all_shortcuts_for_root_dir(&root_dir) {
        warn!("Failed to remove shortcuts ({}).", e);
    }

    info!("Creating new default shortcuts...");
    let _ = windows::create_default_lnks(&root_dir, &app);

    Ok(())
}
