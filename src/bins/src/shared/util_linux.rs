use anyhow::{anyhow, bail, Result};
use std::{ffi::OsString, process::Command as Process, time::Duration};
use velopack::locator::VelopackLocator;

pub fn wait_for_pid_to_exit(pid: u32, ms_to_wait: u32) -> Result<()> {
    info!("Waiting {}ms for process ({}) to exit.", ms_to_wait, pid);
    let mut handle = waitpid_any::WaitHandle::open(pid.try_into()?)?;
    let result = handle.wait_timeout(Duration::from_millis(ms_to_wait as u64))?;
    if result.is_some() {
        info!("Parent process exited.");
        Ok(())
    } else {
        bail!("Parent process timed out.");
    }
}

pub fn wait_for_parent_to_exit(ms_to_wait: u32) -> Result<()> {
    let id = std::os::unix::process::parent_id();
    info!("Attempting to wait for parent process ({}) to exit.", id);
    if id > 1 {
        wait_for_pid_to_exit(id, ms_to_wait)?;
    }
    Ok(())
}

pub fn start_package(locator: &VelopackLocator, exe_args: Option<Vec<OsString>>, set_env: Option<&str>) -> Result<()> {
    let root_dir = locator.get_root_dir();
    let mut cmd = Process::new(root_dir);
    if let Some(args) = exe_args {
        cmd.args(args);
    }
    if let Some(env) = set_env {
        cmd.env(env, "true");
    }
    cmd.spawn().map_err(|z| anyhow!("Failed to start_package ({}).", z))?;
    Ok(())
}
