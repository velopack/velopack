use crate::dialogs::{self, DialogResult};
use std::{ffi::OsStr, path::Path};
use velopack::locator::VelopackLocator;

pub fn close_processes_locking_dir(locator: &VelopackLocator) -> bool {
    let app_title = locator.get_manifest_title();
    let app_version = locator.get_manifest_version_full_string();
    loop {
        let bin_dir = locator.get_current_bin_dir();
        let pids = filelocksmith::find_processes_locking_path(&bin_dir);
        if pids.is_empty() {
            return true;
        }

        let pids_str = pids
            .iter()
            .map(|pid| {
                format!(
                    "[PID.{}]{}",
                    pid,
                    filelocksmith::pid_to_process_path(*pid)
                        .and_then(|fp| Path::new(&fp).file_name().map(OsStr::to_owned))
                        .map(|os_str| os_str.to_string_lossy().into_owned())
                        .unwrap_or_else(|| "unknown".to_owned())
                )
            })
            .collect::<Vec<String>>()
            .join(", ");

        let result = dialogs::show_processes_locking_folder_dialog(&app_title, &app_version, &pids_str);

        match result {
            DialogResult::Retry => continue,
            DialogResult::Continue => {
                if filelocksmith::quit_processes(pids) {
                    return true;
                }
            }
            _ => return false,
        }
    }
}

#[test]
#[ignore]
fn test_close_processes_locking_dir() {
    let mut paths = velopack::locator::VelopackLocatorConfig::default();
    paths.CurrentBinaryDir = std::path::PathBuf::from(r"C:\Users\Caelan\AppData\Local\Clowd");

    let mut mani = velopack::bundle::Manifest::default();
    mani.title = "Test".to_owned();
    mani.version = semver::Version::parse("1.0.0").unwrap();

    let locator = VelopackLocator::new(paths.clone(), mani.clone());
    close_processes_locking_dir(&locator);
}
