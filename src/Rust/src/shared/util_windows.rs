use anyhow::{anyhow, bail, Result};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Command as Process,
};
use winsafe::{self as w, co, prelude::*};

use super::bundle::{self, Manifest, EntryNameInfo};

pub fn wait_for_parent_to_exit(ms_to_wait: u32) -> Result<()> {
    info!("Reading parent process information.");
    let basic_info = windows_sys::Wdk::System::Threading::ProcessBasicInformation;
    let handle = unsafe { windows_sys::Win32::System::Threading::GetCurrentProcess() };
    let mut return_length: u32 = 0;
    let return_length_ptr: *mut u32 = &mut return_length as *mut u32;

    let mut info = windows_sys::Win32::System::Threading::PROCESS_BASIC_INFORMATION {
        AffinityMask: 0,
        BasePriority: 0,
        ExitStatus: 0,
        InheritedFromUniqueProcessId: 0,
        PebBaseAddress: std::ptr::null_mut(),
        UniqueProcessId: 0,
    };

    let info_ptr: *mut ::core::ffi::c_void = &mut info as *mut _ as *mut ::core::ffi::c_void;
    let info_size = std::mem::size_of::<windows_sys::Win32::System::Threading::PROCESS_BASIC_INFORMATION>() as u32;
    let hr = unsafe { windows_sys::Wdk::System::Threading::NtQueryInformationProcess(handle, basic_info, info_ptr, info_size, return_length_ptr) };

    if hr != 0 {
        return Err(anyhow!("Failed to query process information: {}", hr));
    }

    if info.InheritedFromUniqueProcessId <= 1 {
        // the parent process has exited
        info!("The parent process ({}) has already exited", info.InheritedFromUniqueProcessId);
        return Ok(());
    }

    fn get_pid_start_time(process: w::HPROCESS) -> Result<u64> {
        let mut creation = w::FILETIME::default();
        let mut exit = w::FILETIME::default();
        let mut kernel = w::FILETIME::default();
        let mut user = w::FILETIME::default();
        process.GetProcessTimes(&mut creation, &mut exit, &mut kernel, &mut user)?;
        Ok(((creation.dwHighDateTime as u64) << 32) | creation.dwLowDateTime as u64)
    }

    let parent_handle = w::HPROCESS::OpenProcess(co::PROCESS::QUERY_LIMITED_INFORMATION, false, info.InheritedFromUniqueProcessId as u32)?;
    let parent_start_time = get_pid_start_time(unsafe { parent_handle.raw_copy() })?;
    let myself_start_time = get_pid_start_time(w::HPROCESS::GetCurrentProcess())?;

    if parent_start_time > myself_start_time {
        // the parent process has exited and the id has been re-used
        info!(
            "The parent process ({}) has already exited. parent_start={}, my_start={}",
            info.InheritedFromUniqueProcessId, parent_start_time, myself_start_time
        );
        return Ok(());
    }

    info!("Waiting {}ms for parent process ({}) to exit.", ms_to_wait, info.InheritedFromUniqueProcessId);
    match parent_handle.WaitForSingleObject(Some(ms_to_wait)) {
        Ok(co::WAIT::OBJECT_0) => Ok(()),
        // Ok(co::WAIT::TIMEOUT) => Ok(()),
        _ => Err(anyhow!("Failed to wait for parent process to exit.")),
    }
}

fn get_processes_running_in_directory<P: AsRef<Path>>(dir: P) -> Result<HashMap<u32, PathBuf>> {
    let dir = dir.as_ref();
    let mut oup = HashMap::new();
    let mut hpl = w::HPROCESSLIST::CreateToolhelp32Snapshot(co::TH32CS::SNAPPROCESS, None)?;
    for proc_entry in hpl.iter_processes() {
        if let Ok(proc) = proc_entry {
            let process = w::HPROCESS::OpenProcess(co::PROCESS::QUERY_LIMITED_INFORMATION, false, proc.th32ProcessID);
            if process.is_err() {
                continue;
            }

            let process = process.unwrap();
            let full_path = process.QueryFullProcessImageName(co::PROCESS_NAME::WIN32);
            if full_path.is_err() {
                continue;
            }

            let full_path = full_path.unwrap();
            let full_path = Path::new(&full_path);
            if let Ok(is_subpath) = crate::windows::is_sub_path(full_path, dir) {
                if is_subpath {
                    oup.insert(proc.th32ProcessID, full_path.to_path_buf());
                }
            }
        }
    }
    Ok(oup)
}

fn kill_pid(pid: u32) -> Result<()> {
    let process = w::HPROCESS::OpenProcess(co::PROCESS::TERMINATE, false, pid)?;
    process.TerminateProcess(1)?;
    Ok(())
}

pub fn force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref();
    super::retry_io(|| {
        _force_stop_package(root_dir)
    })?;
    Ok(())
}

fn _force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let dir = root_dir.as_ref();
    info!("Checking for running processes in: {}", dir.display());
    let processes = get_processes_running_in_directory(dir)?;
    let my_pid = std::process::id();
    for (pid, exe) in processes.iter() {
        if *pid == my_pid {
            warn!("Skipping killing self: {} ({})", exe.display(), pid);
            continue;
        }
        warn!("Killing process: {} ({})", exe.display(), pid);
        kill_pid(*pid)?;
    }
    Ok(())
}

pub fn start_package<P: AsRef<Path>>(app: &Manifest, root_dir: P, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    let root_dir = root_dir.as_ref().to_path_buf();
    let current = app.get_current_path(&root_dir);
    let exe = app.get_main_exe_path(&root_dir);

    let exe_to_execute = std::path::Path::new(&exe);
    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{}'", exe_to_execute.to_string_lossy());
    }

    crate::windows::assert_can_run_binary_authenticode(&exe_to_execute)?;

    let mut psi = Process::new(&exe_to_execute);
    psi.current_dir(&current);
    if let Some(args) = exe_args {
        psi.args(args);
    }
    if let Some(env) = set_env {
        debug!("Setting environment variable: {}={}", env, "true");
        psi.env(env, "true");
    }

    info!("About to launch: '{}' in dir '{}'", exe_to_execute.to_string_lossy(), current);
    info!("Args: {:?}", psi.get_args());
    psi.spawn().map_err(|z| anyhow!("Failed to start application ({}).", z))?;

    Ok(())
}

pub fn detect_manifest_from_update_path(update_exe: &PathBuf) -> Result<(PathBuf, Manifest)> {
    let root_path = update_exe.parent().unwrap().to_path_buf();
    let app = find_manifest_from_root_dir(&root_path)
        .map_err(|m| anyhow!("Unable to read application manifest ({}). Is this a properly installed application?", m))?;
    info!("Loaded manifest for application: {}", app.id);
    info!("Root Directory: {}", root_path.to_string_lossy());
    Ok((root_path, app))
}

pub fn detect_current_manifest() -> Result<(PathBuf, Manifest)> {
    let me = std::env::current_exe()?;
    detect_manifest_from_update_path(&me)
}

fn find_manifest_from_root_dir(root_path: &PathBuf) -> Result<Manifest> {
    // default to checking current/sq.version
    let cm = find_current_manifest(root_path);
    if cm.is_ok() {
        return cm;
    }

    // if that fails, check for latest full package
    let latest = find_latest_full_package(root_path);
    if let Some(latest) = latest {
        let mani = latest.load_manifest()?;
        return Ok(mani);
    }

    bail!("Unable to locate manifest or package.");
}

fn find_current_manifest(root_path: &PathBuf) -> Result<Manifest> {
    let m = Manifest::default();
    let nuspec_path = m.get_nuspec_path(root_path);
    if Path::new(&nuspec_path).exists() {
        if let Ok(nuspec) = super::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
            return Ok(bundle::read_manifest_from_string(&nuspec)?);
        }
    }
    bail!("Unable to read nuspec file in current directory.")
}

fn find_latest_full_package(root_path: &PathBuf) -> Option<EntryNameInfo> {
    let packages = get_all_packages(root_path);
    let mut latest: Option<EntryNameInfo> = None;
    for pkg in packages {
        if pkg.is_delta {
            continue;
        }
        if latest.is_none() {
            latest = Some(pkg);
        } else {
            let latest_ver = latest.clone().unwrap().version;
            if pkg.version > latest_ver {
                latest = Some(pkg);
            }
        }
    }
    latest
}

fn get_all_packages(root_path: &PathBuf) -> Vec<EntryNameInfo> {
    let m = Manifest::default();
    let packages = m.get_packages_path(root_path);
    let mut vec = Vec::new();
    debug!("Scanning for packages in {:?}", packages);
    if let Ok(entries) = std::fs::read_dir(packages) {
        for entry in entries {
            if let Ok(entry) = entry {
                if let Some(pkg) = super::bundle::parse_package_file_path(entry.path()) {
                    debug!("Found package: {}", entry.path().to_string_lossy());
                    vec.push(pkg);
                }
            }
        }
    }
    vec
}

#[test]
fn test_get_running_processes_finds_cargo() {
    let profile = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Profile, co::KF::DONT_UNEXPAND, None).unwrap();
    let path = Path::new(&profile);
    let rustup = path.join(".rustup");

    let processes = get_processes_running_in_directory(&rustup).unwrap();
    assert!(processes.len() > 0);

    let mut found = false;
    for (_pid, exe) in processes.iter() {
        if exe.ends_with("cargo.exe") {
            found = true;
        }
    }
    assert!(found);
}
