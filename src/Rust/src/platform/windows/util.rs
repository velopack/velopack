use anyhow::{anyhow, bail, Result};
use normpath::PathExt;
use std::{
    collections::HashMap,
    ffi::OsStr,
    io::Read,
    os::windows::process::CommandExt,
    path::{Path, PathBuf},
    process::Command as Process,
    time::Duration,
};
use wait_timeout::ChildExt;
use windows::{
    core::PCWSTR,
    Win32::{
        Foundation::{self, GetLastError},
        System::Threading::CreateMutexW,
    },
};
use winsafe::{self as w, co, prelude::*};

use crate::util;

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

pub fn create_global_mutex(name: &str) -> Result<Foundation::HANDLE> {
    let encoded = name.encode_utf16().chain([0u16]).collect::<Vec<u16>>();
    let pw = PCWSTR(encoded.as_ptr());
    let mutex = unsafe { CreateMutexW(None, true, pw) }?;
    match unsafe { GetLastError() } {
        Ok(_) => Ok(mutex),
        Err(err) => {
            if err == Foundation::ERROR_ALREADY_EXISTS.into() {
                bail!("Another installer or updater for this application is running, quit that process and try again.");
            } else {
                bail!("Unable to create global mutex: {}", err);
            }
        }
    }
}

pub fn is_sub_path<P1: AsRef<Path>, P2: AsRef<Path>>(path: P1, parent: P2) -> Result<bool> {
    let path = path.as_ref().to_string_lossy().to_lowercase();
    let parent = parent.as_ref().to_string_lossy().to_lowercase();

    // some quick bails before we do the more expensive path normalization
    if path.is_empty() || parent.is_empty() {
        return Ok(false);
    }

    if path.len() < parent.len() {
        return Ok(false);
    }

    if path.starts_with(&parent) {
        return Ok(true);
    }

    let path = w::ExpandEnvironmentStrings(&path)?;
    let parent = w::ExpandEnvironmentStrings(&parent)?;

    let path = Path::new(&path);
    let parent = Path::new(&parent);

    // we just bail if paths are not absolute. in the cases where we use this function,
    // we should have absolute paths from the file system (eg. iterating running processes, reading shortcuts)
    // if we receive a relative path, it's likely coming from a shortcut target/working directory
    // that we can't resolve with ExpandEnvironmentStrings
    if !path.is_absolute() || !parent.is_absolute() {
        return Ok(false);
    }

    // calls GetFullPathNameW
    let path = path.normalize_virtually()?.as_path().to_string_lossy().to_lowercase();
    let parent = parent.normalize_virtually()?.as_path().to_string_lossy().to_lowercase();

    let path = PathBuf::from(path);
    let parent = PathBuf::from(parent);

    // use path.starts_with instead of string.starts_with because it compares by path component
    Ok(path.starts_with(parent))
}

#[test]
fn test_is_sub_path_works_with_existing_paths() {
    let path = PathBuf::from(r"C:\Windows\System32/dxdiag.exe");
    let parent = PathBuf::from(r"c:\windows/system32\");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Windows\System32/dxdiag.exe");
    let parent = PathBuf::from(r"c:\windows/");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Windows\System32/dxdiag.exe");
    let parent = PathBuf::from(r"c:\windows\");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Windows\System32/dxdiag.exe");
    let parent = PathBuf::from(r"c:\windows");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Windows\System32/dxdiag.exe");
    let parent = PathBuf::from(r"c:/");
    assert!(is_sub_path(&path, &parent).unwrap());
}

#[test]
fn test_is_sub_path_works_with_non_existing_paths() {
    let path = PathBuf::from(r"C:\Some/Non-existing\Path/Whatever.exe");
    let parent = PathBuf::from(r"c:\some\non-existing/path\");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Some/Non-existing\Path/Whatever.exe");
    let parent = PathBuf::from(r"c:\some\non-existing/path/");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Some/Non-existing\Path/Whatever.exe");
    let parent = PathBuf::from(r"c:\some/non-existing/");
    assert!(is_sub_path(&path, &parent).unwrap());
}

#[test]
fn test_is_sub_path_works_with_env_var_paths_and_avoids_current_dir() {
    let path = PathBuf::from(r"C:\Windows\System32\cmd.exe");
    let parent = PathBuf::from(r"%windir%");
    assert!(is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from(r"C:\Source\rust setup testing\install");
    let parent = PathBuf::from(r"%windir%\system32");
    assert!(!is_sub_path(&path, &parent).unwrap());

    let path = r"%windir%\system32";
    let parent = std::env::current_dir().unwrap().to_string_lossy().to_string();
    assert!(!is_sub_path(&path, &parent).unwrap());
    assert!(!is_sub_path(&parent, &path).unwrap());
}

#[test]
fn test_is_sub_path_works_with_empty_paths() {
    let path = PathBuf::from(r"C:\Windows\Path.exe");
    let parent = PathBuf::from("");
    assert!(!is_sub_path(&path, &parent).unwrap());

    let path = PathBuf::from("");
    let parent = PathBuf::from(r"c:\some\non-existing/path/");
    assert!(!is_sub_path(&path, &parent).unwrap());
}

pub fn is_os_version_or_greater(version: &str) -> Result<bool> {
    let (mut major, mut minor, mut build, _) = util::parse_version(version)?;

    if major < 8 {
        return Ok(w::IsWindows7OrGreater()?);
    }

    if major == 8 {
        return Ok(if minor >= 1 { w::IsWindows8Point1OrGreater()? } else { w::IsWindows8OrGreater()? });
    }

    // https://en.wikipedia.org/wiki/List_of_Microsoft_Windows_versions
    if major == 11 {
        if build < 22000 {
            build = 22000;
        }
        major = 10;
        minor = 0;
    }

    if major == 10 && build <= 0 {
        return Ok(w::IsWindows10OrGreater()?);
    }

    let mut mask: u64 = 0;
    mask = w::VerSetConditionMask(mask, co::VER_MASK::MAJORVERSION, co::VER_COND::GREATER_EQUAL);
    mask = w::VerSetConditionMask(mask, co::VER_MASK::MINORVERSION, co::VER_COND::GREATER_EQUAL);
    mask = w::VerSetConditionMask(mask, co::VER_MASK::BUILDNUMBER, co::VER_COND::GREATER_EQUAL);

    let mut osvi: w::OSVERSIONINFOEX = Default::default();
    osvi.dwMajorVersion = major;
    osvi.dwMinorVersion = minor;
    osvi.dwBuildNumber = build;
    return Ok(w::VerifyVersionInfo(&mut osvi, co::VER_MASK::MAJORVERSION | co::VER_MASK::MINORVERSION | co::VER_MASK::BUILDNUMBER, mask)?);
}

#[test]
#[ignore]
pub fn test_os_returns_true_for_everything_on_windows_11_and_below() {
    assert!(is_os_version_or_greater("6").unwrap());
    assert!(is_os_version_or_greater("7").unwrap());
    assert!(is_os_version_or_greater("8").unwrap());
    assert!(is_os_version_or_greater("8.1").unwrap());
    assert!(is_os_version_or_greater("10").unwrap());
    assert!(is_os_version_or_greater("10.0.20000").unwrap());
    assert!(is_os_version_or_greater("11").unwrap());
    assert!(!is_os_version_or_greater("12").unwrap());
}

pub fn get_processes_running_in_directory(dir: &PathBuf) -> Result<HashMap<u32, PathBuf>> {
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
            if let Ok(is_subpath) = is_sub_path(full_path, dir) {
                if is_subpath {
                    oup.insert(proc.th32ProcessID, full_path.to_path_buf());
                }
            }
        }
    }
    Ok(oup)
}

pub fn kill_pid(pid: u32) -> Result<()> {
    let process = w::HPROCESS::OpenProcess(co::PROCESS::TERMINATE, false, pid)?;
    process.TerminateProcess(1)?;
    Ok(())
}

pub fn kill_processes_in_directory(dir: &PathBuf) -> Result<()> {
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

const CREATE_NO_WINDOW: u32 = 0x08000000;
pub fn run_process_no_console_and_wait<S, P>(exe: S, args: Vec<&str>, work_dir: P, timeout: Option<Duration>) -> Result<String>
where
    S: AsRef<OsStr>,
    P: AsRef<Path>,
{
    let mut cmd = Process::new(exe)
        .args(args)
        .current_dir(work_dir)
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped())
        .creation_flags(CREATE_NO_WINDOW)
        .spawn()?;

    fn check_process_status_and_output(status: std::process::ExitStatus, mut cmd: std::process::Child) -> Result<String> {
        let mut stdout = cmd.stdout.take().unwrap();
        let mut stderr = cmd.stderr.take().unwrap();
        let mut stdout_buf = Vec::new();
        stdout.read_to_end(&mut stdout_buf)?;
        stderr.read_to_end(&mut stdout_buf)?;

        if !status.success() {
            warn!("Process exited with non-zero exit code: {}", status.code().unwrap_or(0));
            if stdout_buf.len() > 0 {
                warn!("    Output:\n{}", String::from_utf8_lossy(&stdout_buf));
            }
            return Err(anyhow!("Process exited with non-zero exit code: {}", status.code().unwrap_or(0)));
        }

        Ok(String::from_utf8_lossy(&stdout_buf).to_string())
    }

    if let Some(t) = timeout {
        match cmd.wait_timeout(t) {
            Ok(Some(status)) => check_process_status_and_output(status, cmd),
            Ok(None) => {
                cmd.kill()?;
                return Err(anyhow!("Process timed out after {:?}", t));
            }
            Err(e) => return Err(e.into()),
        }
    } else {
        let status = cmd.wait()?;
        check_process_status_and_output(status, cmd)
    }
}

pub fn run_process_no_console<S, P>(exe: S, args: Vec<&str>, work_dir: P) -> Result<()>
where
    S: AsRef<OsStr>,
    P: AsRef<Path>,
{
    Process::new(exe).args(args).current_dir(work_dir).creation_flags(CREATE_NO_WINDOW).spawn()?;
    Ok(())
}

pub fn run_process_no_console_raw_args<S, P>(exe: S, args: &str, work_dir: P) -> Result<()>
where
    S: AsRef<OsStr>,
    P: AsRef<Path>,
{
    Process::new(exe).raw_arg(args).current_dir(work_dir).creation_flags(CREATE_NO_WINDOW).spawn()?;
    Ok(())
}

pub fn run_process<S, P>(exe: S, args: Vec<&str>, work_dir: P) -> Result<()>
where
    S: AsRef<OsStr>,
    P: AsRef<Path>,
{
    Process::new(exe).args(args).current_dir(work_dir).spawn()?;
    Ok(())
}

pub fn run_process_raw_args<S, P>(exe: S, args: &str, work_dir: P) -> Result<()>
where
    S: AsRef<OsStr>,
    P: AsRef<Path>,
{
    Process::new(exe).raw_arg(args).current_dir(work_dir).spawn()?;
    Ok(())
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

pub fn is_cpu_architecture_supported(architecture: &str) -> Result<bool> {
    let info = os_info::get();
    let machine = info.architecture();
    if machine.is_none() {
        return Ok(true); // we can't detect current arch so try installing anyway.
    }

    let mut machine = machine.unwrap();
    let is_win_11 = is_os_version_or_greater("11")?;

    if machine.is_empty() || architecture.is_empty() {
        return Ok(true);
    }

    // https://github.com/stanislav-tkach/os_info/blob/master/os_info/src/windows/winapi.rs#L82
    if machine == "x86_64" {
        machine = "x64";
    } else if machine == "i386" {
        machine = "x86";
    } else if machine == "aarch64" {
        machine = "arm64";
    }

    if machine == "x86" {
        // windows x86 only supports x86
        Ok(architecture == "x86")
    } else if machine == "x64" {
        // windows x64 only supports x86 and x64
        Ok(architecture == "x86" || architecture == "x64")
    } else if machine == "arm64" {
        // windows arm64 supports x86, and arm64, and only on windows 11 does it support x64
        Ok(architecture == "x86" || (architecture == "x64" && is_win_11) || architecture == "arm64")
    } else {
        // we don't know what this is, so try installing anyway
        Ok(true)
    }
}

#[test]
pub fn test_x64_and_x86_is_supported_but_not_arm64_or_invalid() {
    assert!(!is_cpu_architecture_supported("arm64").unwrap());
    assert!(!is_cpu_architecture_supported("invalid").unwrap());
    assert!(is_cpu_architecture_supported("x64").unwrap());
    assert!(is_cpu_architecture_supported("x86").unwrap());
}

pub fn check_authenticode_signature<P: AsRef<Path>>(path: P) -> Result<bool> {
    let path = path.as_ref();
    let v = codesign_verify::CodeSignVerifier::for_file(path)
        .map_err(|e| anyhow!("Unable to open authenticode verifier for '{}' ({:?})", path.to_string_lossy(), e))?;
    let sig = v.verify().map_err(|e| anyhow!("Unable to verify binary signature '{}' ({:?})", path.to_string_lossy(), e))?;

    info!("Code signature for '{}' is valid", path.to_string_lossy());
    debug!("Subject Name: {:?}", sig.subject_name());
    debug!("Issuer Name: {:?}", sig.issuer_name());
    debug!("SHA1 Thumbprint: {}", sig.sha1_thumbprint());
    debug!("Serial: {:?}", sig.serial());

    Ok(true)
}

pub fn assert_can_run_binary_authenticode<P: AsRef<Path>>(path: P) -> Result<()> {
    let path = path.as_ref();

    info!("Verifying authenticode signatures of prospective launch binary...");
    let target = check_authenticode_signature(path).unwrap_or(false);
    let myself = check_authenticode_signature(std::env::current_exe()?).unwrap_or(false);

    debug!("Target ({}) Signature = {}", path.to_string_lossy(), if target { "PASS" } else { "FAIL" });
    debug!("My Signature = {}", if target { "PASS" } else { "FAIL" });

    if myself && !target {
        bail!("This binary is signed, and the target binary is not. Refusing to run.")
    }

    Ok(())
}

#[test]
#[ignore]
fn test_authenticode() {
    crate::util::trace_logger();
    assert!(verify_authenticode_against_powershell(r"C:\Windows\System32\notepad.exe"));
    assert!(verify_authenticode_against_powershell(r"C:\Windows\System32\cmd.exe"));
    assert!(verify_authenticode_against_powershell(r"C:\Users\Caelan\AppData\Local\Programs\Microsoft VS Code\Code.exe"));
    assert!(!verify_authenticode_against_powershell(r"C:\Users\Caelan\AppData\Local\Clowd\Update.exe"));
    assert!(!verify_authenticode_against_powershell(r"C:\Users\Caelan\.cargo\bin\cargo.exe"));
}

fn verify_authenticode_against_powershell(path: &str) -> bool {
    let command = format!("Get-AuthenticodeSignature \"{}\" | select Status -expandproperty Status", path);
    let args = command.split_whitespace().collect();
    let ps_output = super::run_process_no_console_and_wait("powershell", args, std::env::current_dir().unwrap(), None).unwrap();
    let ps_result = ps_output.trim() == "Valid";
    let my_result = check_authenticode_signature(path).unwrap_or(false);
    assert!(ps_result == my_result);
    return my_result;
}
