use super::bundle::Manifest;
use crate::shared::bundle;
use anyhow::{anyhow, bail, Result};
use std::{path::Path, path::PathBuf, process::Command as Process, time::Duration};

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

pub fn start_package<P: AsRef<Path>>(_app: &Manifest, root_dir: P, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    let mut cmd = Process::new(root_dir.as_ref());
    if let Some(args) = exe_args {
        cmd.args(args);
    }
    if let Some(env) = set_env {
        cmd.env(env, "true");
    }
    cmd.spawn().map_err(|z| anyhow!("Failed to start_package ({}).", z))?;
    Ok(())
}

pub fn detect_current_manifest() -> Result<(PathBuf, Manifest)> {
    let mut manifest_path = std::env::current_exe()?;
    manifest_path.pop();
    manifest_path.push("sq.version");
    let manifest = load_manifest(&manifest_path)?;

    let path = std::env::var("APPIMAGE")?;
    let path = Path::new(&path).to_path_buf();
    if !path.exists() {
        bail!("Unable to find AppImage at: {}", path.to_string_lossy());
    }
    Ok((path, manifest))
}

fn load_manifest(nuspec_path: &PathBuf) -> Result<Manifest> {
    if Path::new(&nuspec_path).exists() {
        if let Ok(nuspec) = super::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
            return Ok(bundle::read_manifest_from_string(&nuspec)?);
        }
    }
    bail!("Unable to read nuspec file in current directory.")
}
