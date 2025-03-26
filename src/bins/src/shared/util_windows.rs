use ::windows::Win32::System::ProcessStatus::EnumProcesses;
use ::windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow;
use anyhow::{anyhow, bail, Result};
use regex::Regex;
use semver::Version;
use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
    process::Command as Process,
};
use windows::Wdk::System::Threading::{NtQueryInformationProcess, ProcessBasicInformation};
use windows::Win32::System::Threading::{GetCurrentProcess, PROCESS_BASIC_INFORMATION};
use winsafe::{self as w, co, prelude::*};

use velopack::locator::VelopackLocator;

pub fn wait_for_pid_to_exit(pid: u32, ms_to_wait: u32) -> Result<()> {
    info!("Waiting {}ms for process ({}) to exit.", ms_to_wait, pid);
    let handle = w::HPROCESS::OpenProcess(co::PROCESS::SYNCHRONIZE, false, pid)?;
    match handle.WaitForSingleObject(Some(ms_to_wait)) {
        Ok(co::WAIT::OBJECT_0) => Ok(()),
        // Ok(co::WAIT::TIMEOUT) => Ok(()),
        _ => Err(anyhow!("WaitForSingleObject Failed.")),
    }
}

pub fn wait_for_parent_to_exit(ms_to_wait: u32) -> Result<()> {
    info!("Reading parent process information.");
    let basic_info = ProcessBasicInformation;
    let handle = unsafe { GetCurrentProcess() };
    let mut return_length: u32 = 0;
    let return_length_ptr: *mut u32 = &mut return_length as *mut u32;

    let mut info = PROCESS_BASIC_INFORMATION {
        AffinityMask: 0,
        BasePriority: 0,
        ExitStatus: Default::default(),
        InheritedFromUniqueProcessId: 0,
        PebBaseAddress: std::ptr::null_mut(),
        UniqueProcessId: 0,
    };

    let info_ptr: *mut ::core::ffi::c_void = &mut info as *mut _ as *mut ::core::ffi::c_void;
    let info_size = std::mem::size_of::<PROCESS_BASIC_INFORMATION>() as u32;
    let hres = unsafe { NtQueryInformationProcess(handle, basic_info, info_ptr, info_size, return_length_ptr) };
    if hres.is_err() {
        return Err(anyhow!("Failed to query process information: {:?}", hres));
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

    let permissions = co::PROCESS::QUERY_LIMITED_INFORMATION | co::PROCESS::SYNCHRONIZE;
    let parent_handle = w::HPROCESS::OpenProcess(permissions, false, info.InheritedFromUniqueProcessId as u32)?;
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
        _ => Err(anyhow!("WaitForSingleObject Failed.")),
    }
}

// https://github.com/nushell/nushell/blob/4458aae3d41517d74ce1507ad3e8cd94021feb16/crates/nu-system/src/windows.rs#L593
fn get_pids() -> Result<Vec<u32>> {
    let dword_size = std::mem::size_of::<u32>();
    let mut pids: Vec<u32> = Vec::with_capacity(101920);
    let mut cb_needed = 0;

    unsafe {
        pids.set_len(101920);
        let _ = EnumProcesses(pids.as_mut_ptr(), (dword_size * pids.len()) as u32, &mut cb_needed)?;
        let pids_len = cb_needed / dword_size as u32;
        pids.set_len(pids_len as usize);
    }

    Ok(pids.iter().map(|x| *x as u32).collect())
}

fn get_processes_running_in_directory<P: AsRef<Path>>(dir: P) -> Result<HashMap<u32, PathBuf>> {
    let dir = dir.as_ref();
    let mut oup = HashMap::new();

    for pid in get_pids()? {
        // I don't like using catch_unwind, but QueryFullProcessImageName seems to panic
        // when it reaches a mingw64 process. This is a workaround.
        let process_path = std::panic::catch_unwind(|| {
            let process = w::HPROCESS::OpenProcess(co::PROCESS::QUERY_LIMITED_INFORMATION, false, pid);
            if let Err(_) = process {
                // trace!("Failed to open process: {} ({})", pid, e);
                return None;
            }

            let process = process.unwrap();
            let full_path = process.QueryFullProcessImageName(co::PROCESS_NAME::WIN32);
            if let Err(_) = full_path {
                // trace!("Failed to query process path: {} ({})", pid, e);
                return None;
            }
            return Some(full_path.unwrap());
        });

        match process_path {
            Ok(Some(full_path)) => {
                let full_path = Path::new(&full_path);
                if let Ok(is_subpath) = crate::windows::is_sub_path(full_path, dir) {
                    if is_subpath {
                        oup.insert(pid, full_path.to_path_buf());
                    }
                }
            }
            Ok(None) => {}
            Err(e) => error!("Fatal panic checking process: {} ({:?})", pid, e),
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
    super::retry_io(|| _force_stop_package(root_dir))?;
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

pub fn start_package(locator: &VelopackLocator, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    let current = locator.get_current_bin_dir();
    let exe_to_execute = locator.get_main_exe_path();

    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{}'", exe_to_execute.to_string_lossy());
    }

    let mut psi = Process::new(&exe_to_execute);
    psi.current_dir(&current);
    if let Some(args) = exe_args {
        psi.args(args);
    }
    if let Some(env) = set_env {
        debug!("Setting environment variable: {}={}", env, "true");
        psi.env(env, "true");
    }

    info!("About to launch: '{:?}' in dir '{:?}'", exe_to_execute, current);
    info!("Args: {:?}", psi.get_args());
    let child = psi.spawn().map_err(|z| anyhow!("Failed to start application ({}).", z))?;
    let _ = unsafe { AllowSetForegroundWindow(child.id()) };

    Ok(())
}

pub fn get_app_prefixed_folders<P: AsRef<Path>>(parent_path: P) -> Result<Vec<PathBuf>> {
    let parent_path = parent_path.as_ref();
    let re = Regex::new(r"(?i)^app-")?;
    let mut folders = Vec::new();
    // Squirrel.Windows and Clowd.Squirrel V2
    for entry in fs::read_dir(parent_path)? {
        let entry = entry?;
        let path = entry.path();
        if path.is_dir() {
            if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                if re.is_match(name) {
                    folders.push(path);
                }
            }
        }
    }
    // Clowd.Squirrel V3
    let staging_dir = parent_path.join("staging");
    if staging_dir.exists() {
        for entry in fs::read_dir(&staging_dir)? {
            let entry = entry?;
            let path = entry.path();
            if path.is_dir() {
                if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                    if re.is_match(name) {
                        folders.push(path);
                    }
                }
            }
        }
    }
    Ok(folders)
}

pub fn get_latest_app_version_folder<P: AsRef<Path>>(parent_path: P) -> Result<Option<(PathBuf, Version)>> {
    let mut latest_version: Option<Version> = None;
    let mut latest_folder: Option<PathBuf> = None;
    for entry in get_app_prefixed_folders(&parent_path)? {
        if let Some(name) = entry.file_name().and_then(|n| n.to_str()) {
            if let Some(version) = parse_version_from_folder_name(name) {
                if latest_version.is_none() || version > latest_version.clone().unwrap() {
                    latest_version = Some(version);
                    latest_folder = Some(entry);
                }
            }
        }
    }
    Ok(latest_folder.zip(latest_version))
}

pub fn has_app_prefixed_folder<P: AsRef<Path>>(parent_path: P) -> bool {
    match get_app_prefixed_folders(parent_path) {
        Ok(folders) => !folders.is_empty(),
        Err(e) => {
            warn!("Failed to check for app-prefixed folders: {}", e);
            false
        }
    }
}

pub fn delete_app_prefixed_folders<P: AsRef<Path>>(parent_path: P) -> Result<()> {
    let folders = get_app_prefixed_folders(parent_path)?;
    for folder in folders {
        super::retry_io(|| remove_dir_all::remove_dir_all(&folder))?;
    }
    Ok(())
}

fn parse_version_from_folder_name(folder_name: &str) -> Option<Version> {
    folder_name.strip_prefix("app-").and_then(|v| Version::parse(v).ok())
}

#[test]
fn test_get_running_processes_finds_cargo() {
    let profile = crate::windows::known_path::get_user_profile().unwrap();
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
