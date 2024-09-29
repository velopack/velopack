use crate::{
    dialogs,
    shared::{self, OperationWait},
    windows as win,
};
use anyhow::{anyhow, bail, Result};
use std::os::windows::process::CommandExt;
use std::{
    fs,
    path::Path,
    process::Command as Process,
};
use velopack::{bundle, constants};
use velopack::locator::VelopackLocator;
use windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow;

pub fn start_impl(
    locator: &VelopackLocator,
    exe_name: Option<&String>,
    exe_args: Option<Vec<&str>>,
    legacy_args: Option<&String>,
) -> Result<()> {
    let root_dir = locator.get_root_dir();
    let app_title = locator.get_manifest_title();
    match shared::has_app_prefixed_folder(&root_dir) {
        Ok(has_prefix) => {
            if has_prefix {
                info!("This is a legacy app. Will try and upgrade it now.");

                // if started by legacy Squirrel, the working dir of Update.exe may be inside the app-* folder,
                // meaning we can not clean up properly.
                std::env::set_current_dir(&root_dir)?;

                return match try_legacy_migration(&locator) {
                    Ok(new_locator) => {
                        shared::start_package(&new_locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
                        Ok(())
                    }
                    Err(e) => {
                        warn!("Failed to migrate legacy app ({}).", e);
                        dialogs::show_error(
                            &app_title,
                            Some("Unable to start app"),
                            "This app installation has been corrupted and cannot be started. Please reinstall the app.",
                        );
                        Err(e)
                    }
                }
            }
        }
        Err(e) => warn!("Failed legacy check ({}).", e),
    }

    // we can't just run the normal start_package command, because legacy squirrel might provide 
    // an "exe name" to restart which no longer exists in the package

    let current = locator.get_current_bin_dir();
    let exe_to_execute = if let Some(exe) = exe_name {
        Path::new(&current).join(exe)
    } else {
        locator.get_main_exe_path()
    };

    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{:?}'", exe_to_execute);
    }

    info!("About to launch: '{:?}' in dir '{:?}'", exe_to_execute, current);

    let mut cmd = Process::new(&exe_to_execute);
    cmd.current_dir(&current);

    if let Some(args) = exe_args {
        cmd.args(args);
    } else if let Some(args) = legacy_args {
        cmd.raw_arg(args);
    }

    let cmd = cmd.spawn()?;
    let _ = unsafe { AllowSetForegroundWindow(cmd.id()) };
    Ok(())
}

fn try_legacy_migration(locator: &VelopackLocator) -> Result<VelopackLocator> {
    let root_dir = locator.get_root_dir();
    let current_dir = locator.get_current_bin_dir();
    let package = shared::find_latest_full_package(&locator).ok_or_else(|| anyhow!("Unable to find latest full package."))?;
    let mut bundle = bundle::load_bundle_from_file(&package.file_path)?;
    let bundle_manifest = bundle.read_manifest()?; // this verifies it's a bundle we support
    warn!("This application is installed in a folder prefixed with 'app-'. Attempting to migrate...");
    let _ = shared::force_stop_package(&root_dir);

    if !Path::new(&current_dir).exists() {
        info!("Renaming latest app-* folder to current.");
        if let Some((latest_app_dir, _latest_ver)) = shared::get_latest_app_version_folder(&root_dir)? {
            fs::rename(latest_app_dir, &current_dir)?;
        }
    }

    info!("Removing old shortcuts...");
    win::remove_all_shortcuts_for_root_dir(&root_dir);

    // reset current manifest shortcuts, so when the new manifest is being read
    // new shortcuts will be force-created
    let modified_app = locator.clone_self_with_blank_shortcuts();

    info!("Applying latest full package...");
    let buf = Path::new(&package.file_path).to_path_buf();
    super::apply(&modified_app, false, OperationWait::NoWait, Some(&buf), None, false)?;

    info!("Removing old app-* folders...");
    shared::delete_app_prefixed_folders(&root_dir)?;
    let _ = remove_dir_all::remove_dir_all(root_dir.join("staging"));
    
    let new_locator = locator.clone_self_with_new_manifest(&bundle_manifest);
    Ok(new_locator)
}
