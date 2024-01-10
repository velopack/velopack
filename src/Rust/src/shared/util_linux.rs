use super::bundle::Manifest;
use crate::shared::bundle;
use anyhow::{anyhow, bail, Result};
use std::fs::File;
use std::io::{Read, Write};
use std::{path::Path, path::PathBuf, process::Command as Process, time::Duration};
use std::os::unix::io::FromRawFd;

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

pub fn copy_own_fd_to_file(output_path: &str) -> Result<()> {
    let path = std::ffi::CString::new("/proc/self/exe")?;
    let fd = unsafe { libc::open(path.as_ptr(), libc::O_RDONLY) };
    let result = copy_fd_to_file(fd, &output_path);
    unsafe { libc::close(fd) };
    result?;
    Ok(())
}

fn copy_fd_to_file(fd: i32, output_path: &str) -> Result<()> {
    // SAFETY: Assuming the FD is valid and open for reading
    let mut source_file = unsafe { File::from_raw_fd(fd) };
    let mut target_file = File::create(output_path)?;
 
    // Buffer to hold data while copying
    let mut buffer = [0; 4096];
 
    loop {
        let bytes_read = source_file.read(&mut buffer)?;
        if bytes_read == 0 {
            break; // End of file reached
        }
        target_file.write_all(&buffer[..bytes_read])?;
    }
 
    Ok(())
}

// pub fn force_stop_package<P: AsRef<Path>>(_root_dir: P) -> Result<()> {
//     // not supported on linux / no-op
//     Ok(())
// }

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

// pub fn detect_manifest_from_update_path(update_exe: &PathBuf) -> Result<(PathBuf, Manifest)> {
//     let mut manifest_path = update_exe.clone();
//     manifest_path.pop();
//     manifest_path.push("sq.version");
//     let manifest = load_manifest(&manifest_path)?;

//     let my_path = std::env::current_exe()?;
//     let my_path = my_path.to_string_lossy();
//     let app_idx = my_path.find("/usr/bin/");
//     if app_idx.is_none() {
//         bail!("Unable to find /usr/bin/ directory in path: {}", my_path);
//     }

//     let root_dir = &my_path[..app_idx.unwrap()];

//     debug!("Detected Root: {}", root_dir);
//     debug!("Detected AppId: {}", manifest.id);
//     Ok((Path::new(&root_dir).to_path_buf(), manifest))
// }

pub fn detect_current_manifest(package: &PathBuf) -> Result<(PathBuf, Manifest)> {
    let bundle = bundle::load_bundle_from_file(package)?;
    let manifest = bundle.read_manifest()?;
    let path = std::env::var("APPIMAGE")?;
    let path = Path::new(&path).to_path_buf();
    if !path.exists() {
        bail!("Unable to find AppImage at: {}", path.to_string_lossy());
    }
    Ok((path, manifest))
}

// fn load_manifest(nuspec_path: &PathBuf) -> Result<Manifest> {
//     if Path::new(&nuspec_path).exists() {
//         if let Ok(nuspec) = super::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
//             return Ok(bundle::read_manifest_from_string(&nuspec)?);
//         }
//     }
//     bail!("Unable to read nuspec file in current directory.")
// }
