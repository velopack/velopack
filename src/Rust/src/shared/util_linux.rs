use crate::shared::bundle;

use super::bundle::Manifest;
use anyhow::{anyhow, bail, Result};
use std::{path::Path, path::PathBuf, process::Command as Process, time::Duration};

pub fn wait_for_parent_to_exit(ms_to_wait: u32) -> Result<()> {
    let id = std::os::unix::process::parent_id();
    info!("Attempting to wait for parent process ({}) to exit.", id);
    if id >= 1 {
        let id: i32 = id.try_into()?;
        let mut handle = waitpid_any::WaitHandle::open(id)?;
        let result = handle.wait_timeout(Duration::from_millis(ms_to_wait as u64))?;
        if result.is_some() {
            info!("Parent process exited.");
        } else {
            bail!("Parent process timed out.");
        }
    }
    Ok(())
}

pub fn force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    todo!();
}

pub fn start_package<P: AsRef<Path>>(_app: &Manifest, root_dir: P, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    todo!();
}

pub fn detect_manifest_from_update_path(update_exe: &PathBuf) -> Result<(PathBuf, Manifest)> {
    todo!();
}

pub fn detect_current_manifest() -> Result<(PathBuf, Manifest)> {
    todo!();
}
