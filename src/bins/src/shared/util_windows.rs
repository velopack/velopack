use crate::windows::strings;
use ::windows::{
    core::PWSTR,
    Win32::{
        System::ProcessStatus::EnumProcesses,
        System::Threading::{QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_TERMINATE},
    },
};
use anyhow::{bail, Result};
use regex::Regex;
use semver::Version;
use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
};
use velopack::{locator::VelopackLocator, process};

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

unsafe fn get_processes_running_in_directory<P: AsRef<Path>>(dir: P) -> Result<Vec<(u32, PathBuf, process::SafeProcessHandle)>> {
    let dir = dir.as_ref();
    let mut oup = Vec::new();

    let mut full_path_vec = vec![0; i16::MAX as usize];
    let full_path_ptr = PWSTR(full_path_vec.as_mut_ptr());

    for pid in get_pids()? {
        let process = process::open_process(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, false, pid);
        if process.is_err() {
            continue;
        }

        let process = process.unwrap();
        if process.handle().is_invalid() {
            continue;
        }

        let mut full_path_len = full_path_vec.len() as u32;
        if QueryFullProcessImageNameW(process.handle(), PROCESS_NAME_WIN32, full_path_ptr, &mut full_path_len).is_err() {
            continue;
        }

        let full_path = strings::u16_to_string(&full_path_vec);
        if let Err(_) = full_path {
            continue;
        }

        let full_path = PathBuf::from(full_path.unwrap());
        if let Ok(is_subpath) = crate::windows::is_sub_path(&full_path, dir) {
            if is_subpath {
                oup.push((pid, full_path, process));
            }
        }
    }

    Ok(oup)
}

pub fn force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref();
    super::retry_io(|| _force_stop_package(root_dir))?;
    Ok(())
}

fn _force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let dir = root_dir.as_ref();
    info!("Checking for running processes in: {}", dir.display());
    let processes = unsafe { get_processes_running_in_directory(dir)? };
    let my_pid = std::process::id();
    for (pid, path, handle) in processes.iter() {
        if *pid == my_pid {
            warn!("Skipping killing self: {} ({})", path.display(), pid);
            continue;
        }
        warn!("Killing process: {} ({})", path.display(), pid);
        process::kill_process(handle)?;
    }
    Ok(())
}

pub fn start_package(locator: &VelopackLocator, exe_args: Option<Vec<&str>>, set_env: Option<&str>) -> Result<()> {
    let current = locator.get_current_bin_dir();
    let exe_to_execute = locator.get_main_exe_path();

    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{}'", exe_to_execute.to_string_lossy());
    }

    let args: Vec<String> = exe_args.unwrap_or_default().iter().map(|s| s.to_string()).collect();
    let mut environment = HashMap::new();
    if let Some(env_var) = set_env {
        debug!("Setting environment variable: {}={}", env_var, "true");
        environment.insert(env_var.to_string(), "true".to_string());
    }
    process::run_process(exe_to_execute, args, Some(current), true, Some(environment))?;
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

pub fn delete_app_prefixed_folders<P: AsRef<Path>>(parent_path: P) {
    match get_app_prefixed_folders(parent_path) {
        Ok(folders) => {
            for folder in folders {
                if let Err(e) = super::retry_io(|| remove_dir_all::remove_dir_all(&folder)) {
                    warn!("Failed to delete app-prefixed folder: {} ({})", folder.display(), e);
                } else {
                    info!("Deleted app-prefixed folder: {}", folder.display());
                }
            }
        }
        Err(e) => {
            warn!("Failed to find app-prefixed folders: {}", e);
        }
    }
}

fn parse_version_from_folder_name(folder_name: &str) -> Option<Version> {
    folder_name.strip_prefix("app-").and_then(|v| Version::parse(v).ok())
}

#[test]
fn test_get_running_processes_finds_cargo() {
    let profile = crate::windows::known_path::get_user_profile().unwrap();
    let path = Path::new(&profile);
    let rustup = path.join(".rustup");

    let processes = unsafe { get_processes_running_in_directory(&rustup).unwrap() };
    assert!(processes.len() > 0);

    let mut found = false;
    for (_pid, exe, _handle) in processes.iter() {
        if exe.ends_with("cargo.exe") {
            found = true;
        }
    }
    assert!(found);
}
