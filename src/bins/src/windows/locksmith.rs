use std::{ffi::OsStr, path::Path};

use crate::{bundle::Manifest, dialogs, dialogs::DialogResult};

pub fn close_processes_locking_dir(manifest: &Manifest, path: &str) -> bool {
    loop {
        let pids = filelocksmith::find_processes_locking_path(path);
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

        let result = dialogs::show_processes_locking_folder_dialog(manifest, &pids_str);

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
    let mut mani = Manifest::default();
    mani.title = "Test".to_owned();
    mani.version = semver::Version::parse("1.0.0").unwrap();
    close_processes_locking_dir(&mani, r"C:\Users\Caelan\AppData\Local\Clowd");
}
