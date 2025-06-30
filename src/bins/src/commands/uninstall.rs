use crate::shared::{self};
use velopack::{constants, locator::VelopackLocator};

use crate::windows;
use anyhow::Result;
use std::fs::File;

pub fn uninstall(locator: &VelopackLocator, delete_self: bool) -> Result<()> {
    info!("Command: Uninstall");

    let root_path = locator.get_root_dir();

    // the real app could be running at the moment
    let _ = shared::force_stop_package(&root_path);

    // run uninstall hook
    windows::run_hook(&locator, constants::HOOK_CLI_UNINSTALL, 60);

    // remove all shortcuts pointing to the app
    windows::remove_all_shortcuts_for_root_dir(&root_path);

    info!("Removing directory '{:?}'", root_path);
    let _ = remove_dir_all::remove_dir_contents(&root_path);

    let temp_dir = std::env::temp_dir();
    let temp_dir = temp_dir.join(format!("velopack_{}", locator.get_manifest_id()));

    info!("Removing directory '{:?}'", temp_dir);
    let _ = remove_dir_all::remove_dir_all(&temp_dir);

    if let Err(e) = windows::registry::remove_uninstall_entry(&locator) {
        error!("Unable to remove uninstall registry entry ({}).", e);
    }

    let app_title = locator.get_manifest_title();

    info!("Finished successfully.");
    shared::dialogs::show_info(format!("{} Uninstall", app_title).as_str(), None, "The application was successfully uninstalled.");

    let dead_path = root_path.join(".dead");
    let _ = File::create(dead_path);

    if delete_self {
        if let Err(e) = windows::register_intent_to_delete_self(3, &root_path) {
            warn!("Unable to schedule self delete ({}).", e);
        }
    }

    Ok(())
}
