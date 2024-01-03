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
    let root_dir = root_dir.as_ref().to_string_lossy().to_string();
    let command = format!("quit app \"{}\"", root_dir);
    Process::new("/usr/bin/osascript").arg("-e").arg(command).spawn().map_err(|z| anyhow!("Failed to stop application ({}).", z))?;
    Ok(())
}

pub fn start_package<P: AsRef<Path>>(_app: &Manifest, root_dir: P, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    let root_dir = root_dir.as_ref().to_string_lossy().to_string();
    let mut args = vec!["-n", &root_dir];
    if let Some(a) = exe_args {
        args.push("--args");
        args.extend(a);
    }
    let mut psi = Process::new("/usr/bin/open");
    psi.args(args);
    if let Some(env) = set_env {
        psi.env(env, "true");
    }
    psi.spawn().map_err(|z| anyhow!("Failed to start application ({}).", z))?;
    Ok(())
}

pub fn detect_manifest_from_update_path(update_exe: &PathBuf) -> Result<(PathBuf, Manifest)> {
    let mut manifest_path = update_exe.clone();
    manifest_path.pop();
    manifest_path.push("sq.version");
    let manifest = load_manifest(&manifest_path)?;

    let my_path = std::env::current_exe()?;
    let my_path = my_path.to_string_lossy();
    let app_idx = my_path.find(".app/");
    if app_idx.is_none() {
        bail!("Unable to find .app/ directory in path: {}", my_path);
    }

    let root_dir = &my_path[..app_idx.unwrap()];
    let root_dir = root_dir.to_owned() + ".app";

    debug!("Detected Root: {}", root_dir);
    debug!("Detected AppId: {}", manifest.id);
    Ok((Path::new(&root_dir).to_path_buf(), manifest))
}

pub fn detect_current_manifest() -> Result<(PathBuf, Manifest)> {
    let me = std::env::current_exe()?;
    detect_manifest_from_update_path(&me)
}

fn load_manifest(nuspec_path: &PathBuf) -> Result<Manifest> {
    if Path::new(&nuspec_path).exists() {
        if let Ok(nuspec) = super::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
            return Ok(bundle::read_manifest_from_string(&nuspec)?);
        }
    }
    bail!("Unable to read nuspec file in current directory.")
}

#[test]
#[ignore]
fn test_start_and_stop_package() {
    let mani = Manifest::default();
    let root_dir = "/Applications/Calcbot.app";
    let _ = force_stop_package(root_dir);

    fn is_running() -> bool {
        let output = Process::new("pgrep").arg("-f").arg("Calcbot.app").output().unwrap();
        output.stdout.len() > 0
    }

    std::thread::sleep(Duration::from_secs(1));
    assert!(!is_running());
    std::thread::sleep(Duration::from_secs(1));
    start_package(&mani, root_dir, None, None).unwrap();
    std::thread::sleep(Duration::from_secs(1));
    assert!(is_running());
    std::thread::sleep(Duration::from_secs(1));
    force_stop_package(root_dir).unwrap();
    std::thread::sleep(Duration::from_secs(1));
    assert!(!is_running());
}
