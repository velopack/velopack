use super::bundle::Manifest;
use anyhow::{anyhow, bail, Result};
use std::{path::Path, process::Command as Process, time::Duration};

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
    let root_dir = root_dir.as_ref().to_string_lossy().to_string();
    let command = format!("quit app \"{}\"", root_dir);
    Process::new("/usr/bin/osascript").arg("-e").arg(command).spawn().map_err(|z| anyhow!("Failed to stop application ({}).", z))?;
    Ok(())
}

pub fn start_package<P: AsRef<Path>>(_app: &Manifest, root_dir: P, exe_args: Option<Vec<&str>>) -> Result<()> {
    let root_dir = root_dir.as_ref().to_string_lossy().to_string();
    let mut args = vec!["-n", &root_dir];
    if let Some(a) = exe_args {
        args.push("--args");
        args.extend(a);
    }
    Process::new("/usr/bin/open").args(args).spawn().map_err(|z| anyhow!("Failed to start application ({}).", z))?;
    Ok(())
}

#[test]
fn test_start_and_stop_package()
{
    assert!(false);
}