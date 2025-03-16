use crate::shared::{self};
use velopack::{constants, locator::VelopackLocator};

use crate::windows;
use anyhow::{bail, Result};
use std::fs::File;

pub fn uninstall(locator: &VelopackLocator, delete_self: bool) -> Result<()> {
    info!("Command: Uninstall");
    
    let root_path = locator.get_root_dir();

    if !windows::is_directory_writable(&root_path) {
        if windows::process::is_process_elevated() { 
            bail!("The root directory is not writable & process is already admin.");
        } else {
            info!("Re-launching as administrator to uninstall from {:?}", root_path);
            let args = vec!["uninstall".to_string()];
            windows::process::relaunch_self_as_admin(args)?;
            return Ok(());
        }
    }

    fn _uninstall_impl(locator: &VelopackLocator) -> bool {
        let root_path = locator.get_root_dir();
        
        // the real app could be running at the moment
        let _ = shared::force_stop_package(&root_path);

        let mut finished_with_errors = false;

        // run uninstall hook
        windows::run_hook(&locator, constants::HOOK_CLI_UNINSTALL, 60);

        // remove all shortcuts pointing to the app
        windows::remove_all_shortcuts_for_root_dir(&root_path);

        info!("Removing directory '{}'", root_path.to_string_lossy());
        if let Err(e) = shared::retry_io(|| remove_dir_all::remove_dir_but_not_self(&root_path)) {
            error!("Unable to remove directory, some files may be in use ({}).", e);
            finished_with_errors = true;
        }
        
        if let Err(e) = windows::registry::remove_uninstall_entry(&locator) {
            error!("Unable to remove uninstall registry entry ({}).", e);
            // finished_with_errors = true;
        }

        !finished_with_errors
    }

    // if it returns true, it was a success.
    // if it returns false, it was completed with errors which the user should be notified of.
    let result = _uninstall_impl(&locator);
    let app_title = locator.get_manifest_title();

    if result {
        info!("Finished successfully.");
        shared::dialogs::show_info(format!("{} Uninstall", app_title).as_str(), None, "The application was successfully uninstalled.");
    } else {
        error!("Finished with errors.");
        shared::dialogs::show_uninstall_complete_with_errors_dialog(&app_title, None);
    }

    let dead_path = root_path.join(".dead");
    let _ = File::create(dead_path);

    if delete_self {
        if let Err(e) = windows::register_intent_to_delete_self(3, &root_path) {
            warn!("Unable to schedule self delete ({}).", e);
        }
    }

    Ok(())
}
