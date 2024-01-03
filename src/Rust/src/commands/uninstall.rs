use crate::shared::{self, bundle::Manifest};
use crate::windows;
use anyhow::Result;
use std::fs::File;
use std::path::PathBuf;

pub fn uninstall(root_path: &PathBuf, app: &Manifest, delete_self: bool) -> Result<()> {
    info!("Command: Uninstall");

    fn _uninstall_impl(app: &Manifest, root_path: &PathBuf) -> bool {
        // the real app could be running at the moment
        let _ = shared::force_stop_package(&root_path);

        let mut finished_with_errors = false;

        // run uninstall hook
        windows::run_hook(&app, root_path, "--veloapp-uninstall", 60);

        if let Err(e) = windows::remove_all_shortcuts_for_root_dir(&root_path) {
            error!("Unable to remove shortcuts ({}).", e);
            // finished_with_errors = true;
        }

        info!("Removing directory '{}'", root_path.to_string_lossy());
        if let Err(e) = shared::retry_io(|| remove_dir_all::remove_dir_but_not_self(&root_path)) {
            error!("Unable to remove directory, some files may be in use ({}).", e);
            finished_with_errors = true;
        }

        if let Err(e) = app.remove_uninstall_entry() {
            error!("Unable to remove uninstall registry entry ({}).", e);
            // finished_with_errors = true;
        }

        !finished_with_errors
    }

    // if it returns true, it was a success.
    // if it returns false, it was completed with errors which the user should be notified of.
    let result = _uninstall_impl(&app, &root_path);

    if result {
        info!("Finished successfully.");
        shared::dialogs::show_info(format!("{} Uninstall", app.title).as_str(), None, "The application was successfully uninstalled.");
    } else {
        error!("Finished with errors.");
        shared::dialogs::show_uninstall_complete_with_errors_dialog(&app, None);
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
