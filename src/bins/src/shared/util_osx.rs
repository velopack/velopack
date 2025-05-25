use anyhow::{anyhow, bail, Result};
use std::{path::Path, process::Command as Process, time::Duration};
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

pub fn force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref();
    let command = format!("quit app \"{}\"", root_dir);
    Process::new("/usr/bin/osascript").arg("-e").arg(command).spawn().map_err(|z| anyhow!("Failed to stop application ({}).", z))?;
    Ok(())
}

pub fn start_package(locator: &VelopackLocator, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    let root_dir = locator.get_root_dir_as_string();
    let mut args = vec!["-n", &root_dir];
    if let Some(a) = exe_args {
        args.push("--args");
        args.extend(a);
    }
    info!("Starting application: open {:?}", args);
    let mut psi = Process::new("/usr/bin/open");
    psi.args(args);
    if let Some(env) = set_env {
        psi.env(env, "true");
    }
    psi.spawn().map_err(|z| anyhow!("Failed to start application ({}).", z))?;
    Ok(())
}

#[test]
#[ignore]
fn test_start_and_stop_package() {
    let mani = velopack::bundle::Manifest::default();
    let mut paths = velopack::locator::VelopackLocatorConfig::default();
    paths.RootAppDir = std::path::PathBuf::from("/Applications/Calcbot.app");
    let locator = VelopackLocator::new_with_manifest(paths, mani);
    
    let _ = force_stop_package(locator.get_root_dir());

    fn is_running() -> bool {
        let output = Process::new("pgrep").arg("-f").arg("Calcbot.app").output().unwrap();
        output.stdout.len() > 0
    }

    std::thread::sleep(Duration::from_secs(1));
    assert!(!is_running());
    std::thread::sleep(Duration::from_secs(1));
    start_package(&locator, None, None).unwrap();
    std::thread::sleep(Duration::from_secs(1));
    assert!(is_running());
    std::thread::sleep(Duration::from_secs(1));
    force_stop_package(locator.get_root_dir()).unwrap();
    std::thread::sleep(Duration::from_secs(1));
    assert!(!is_running());
}
