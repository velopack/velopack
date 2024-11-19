use std::{
    ffi::c_void,
    os::windows::process::CommandExt,
    path::{Path, PathBuf},
    process::Command as Process,
    time::Duration,
};

use velopack::locator::VelopackLocator;

use anyhow::{anyhow, Result};
use normpath::PathExt;
use wait_timeout::ChildExt;
use windows::Win32::Storage::FileSystem::GetLongPathNameW;
use windows::Win32::System::SystemInformation::{VerSetConditionMask, VerifyVersionInfoW, OSVERSIONINFOEXW, VER_FLAGS};
use windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow;
use windows::{
    core::PCWSTR,
    Win32::{Foundation::HWND, UI::Shell::ShellExecuteW},
};

use crate::shared::{self, runtime_arch::RuntimeArch};
use crate::windows::strings::{string_to_u16, u16_to_string};

pub fn run_hook(locator: &VelopackLocator, hook_name: &str, timeout_secs: u64) -> bool {
    let sw = simple_stopwatch::Stopwatch::start_new();
    let root_dir = locator.get_root_dir();
    let current_path = locator.get_current_bin_dir();
    let main_exe_path = locator.get_main_exe_path();
    let ver_string = locator.get_manifest_version_full_string();
    let args = vec![hook_name, &ver_string];
    let mut success = false;

    info!("Running {} hook...", hook_name);
    const CREATE_NO_WINDOW: u32 = 0x08000000;
    let cmd = Process::new(&main_exe_path).args(args).current_dir(&current_path).creation_flags(CREATE_NO_WINDOW).spawn();

    if let Err(e) = cmd {
        warn!("Failed to start hook {}: {}", hook_name, e);
        return false;
    }

    let mut cmd = cmd.unwrap();
    let _ = unsafe { AllowSetForegroundWindow(cmd.id()) };

    match cmd.wait_timeout(Duration::from_secs(timeout_secs)) {
        Ok(Some(status)) => {
            if status.success() {
                info!("Hook executed successfully (took {}ms)", sw.ms());
                success = true;
            } else {
                warn!("Hook exited with non-zero exit code: {}", status.code().unwrap_or(0));
            }
        }
        Ok(None) => {
            let _ = cmd.kill();
            error!("Process timed out after {}s", timeout_secs);
        }
        Err(e) => {
            error!("Error waiting for process to finish: {}", e);
        }
    }

    // in case the hook left running processes
    let _ = shared::force_stop_package(&root_dir);
    success
}

pub fn relaunch_self_as_admin(args: Vec<String>) -> Result<()> {
    let exe = std::env::current_exe()?;

    let verb = string_to_u16("runas");
    let verb = PCWSTR(verb.as_ptr());

    let exe = string_to_u16(exe.to_string_lossy());
    let exe = PCWSTR(exe.as_ptr());

    let mut params = String::new();
    for arg in args.iter() {
        let arg = arg.to_string();
        params.push(' ');
        if arg.len() == 0 {
            params.push_str("\"\"");
        } else if arg.find(&[' ', '\t', '"'][..]).is_none() {
            params.push_str(&arg);
        } else {
            params.push('"');
            for c in arg.chars() {
                match c {
                    // '\\' => params.push_str("\\\\"),
                    '"' => params.push_str("\\\""),
                    c => params.push(c),
                }
            }
            params.push('"');
        }
    }

    let args = string_to_u16(params);
    let args = PCWSTR(args.as_ptr());

    unsafe {
        let h_instance =
            ShellExecuteW(HWND(std::ptr::null_mut()), verb, exe, args, PCWSTR::null(), windows::Win32::UI::WindowsAndMessaging::SW_NORMAL);
        let min_result = 32;
        let min_ptr = min_result as *mut c_void;
        if h_instance.0 <= min_ptr {
            return Err(anyhow!(windows::core::Error::from_win32()));
        }
    }

    Ok(())
}

pub fn expand_environment_strings<P: AsRef<str>>(input: P) -> Result<String> {
    use windows::Win32::System::Environment::ExpandEnvironmentStringsW;
    let encoded_u16 = super::strings::string_to_u16(input);
    let encoded = PCWSTR(encoded_u16.as_ptr());
    let mut buffer_size = unsafe { ExpandEnvironmentStringsW(encoded, None) };
    if buffer_size == 0 {
        return Err(anyhow!(windows::core::Error::from_win32()));
    }

    let mut buffer: Vec<u16> = vec![0; buffer_size as usize];
    buffer_size = unsafe { ExpandEnvironmentStringsW(encoded, Some(&mut buffer)) };
    if buffer_size == 0 {
        return Err(anyhow!(windows::core::Error::from_win32()));
    }

    super::strings::u16_to_string(buffer)
}

#[test]
fn test_expand_environment_strings() {
    assert_eq!(expand_environment_strings("%windir%").unwrap(), "C:\\Windows");
    assert_eq!(expand_environment_strings("%windir%\\system32").unwrap(), "C:\\Windows\\system32");
    assert_eq!(expand_environment_strings("%windir%\\system32\\").unwrap(), "C:\\Windows\\system32\\");
}

pub fn get_long_path<P: AsRef<str>>(str: P) -> Result<String> {
    let str = str.as_ref().to_string();
    let str = string_to_u16(str);
    let str = PCWSTR(str.as_ptr());
    // SAFETY: str is a valid wide string, this call will return required size of buffer
    let len = unsafe { GetLongPathNameW(str, None) };
    if len == 0 {
        return Err(anyhow!(windows::core::Error::from_win32()));
    }

    let mut vec = vec![0u16; len as usize];
    let len = unsafe { GetLongPathNameW(str, Some(vec.as_mut_slice())) };
    if len == 0 {
        return Err(anyhow!(windows::core::Error::from_win32()));
    }

    let result = u16_to_string(vec)?;
    Ok(result)
}

pub fn is_directory_writable<P1: AsRef<Path>>(path: P1) -> bool {
    use std::os::windows::fs::OpenOptionsExt;
    let path = path.as_ref();
    let path = path.join(".velopack_dir_test");
    let result = std::fs::File::options()
        .create(true)
        .write(true)
        .custom_flags(0x04000000) // FILE_FLAG_DELETE_ON_CLOSE
        .open(&path);

    if let Err(e) = result {
        warn!("Failed to open directory for writing {:?}: {}", path, e);
        return false;
    }

    result.is_ok()
}

pub fn is_sub_path<P1: AsRef<Path>, P2: AsRef<Path>>(path: P1, parent: P2) -> Result<bool> {
    let path = path.as_ref().to_string_lossy().to_lowercase();
    let parent = parent.as_ref().to_string_lossy().to_lowercase();
    let parent = parent.trim_end_matches('\\').trim_end_matches('/').to_owned() + "\\";

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

    let path = expand_environment_strings(&path)?;
    let parent = expand_environment_strings(&parent)?;

    let path = Path::new(&path);
    let parent = Path::new(&parent);

    // we just bail if paths are not absolute. in the cases where we use this function,
    // we should have absolute paths from the file system (e.g. iterating running processes, reading shortcuts)
    // if we receive a relative path, it's likely coming from a shortcut target/working directory
    // that we can't resolve with ExpandEnvironmentStrings
    if !path.is_absolute() || !parent.is_absolute() {
        return Ok(false);
    }

    // calls GetFullPathNameW
    let path = path.normalize().or_else(|_| path.normalize_virtually())?;
    let parent = parent.normalize().or_else(|_| parent.normalize_virtually())?;

    let mut path = path.as_path().to_string_lossy().to_string();
    let mut parent = parent.as_path().to_string_lossy().to_string();

    // calls GetLongPathNameW
    match get_long_path(&path) {
        Ok(p) => path = p,
        Err(e) => warn!("Failed to get long path for '{}': {}", path, e),
    }
    match get_long_path(&parent) {
        Ok(p) => parent = p,
        Err(e) => warn!("Failed to get long path for '{}': {}", parent, e),
    }

    path = path.to_lowercase();
    parent = parent.to_lowercase();

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

    let path = PathBuf::from(r"C:\AppData\JamLogic");
    let parent = PathBuf::from(r"C:\AppData\JamLogicDev");
    assert!(!is_sub_path(&path, &parent).unwrap());
    assert!(!is_sub_path(&parent, &path).unwrap());
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

// Version condition mask constants defined as per Windows SDK
const VER_GREATER_EQUAL: u8 = 3;
const VER_MINORVERSION: VER_FLAGS = VER_FLAGS(0x0000001);
const VER_MAJORVERSION: VER_FLAGS = VER_FLAGS(0x0000002);
const VER_BUILDNUMBER: VER_FLAGS = VER_FLAGS(0x0000004);
const VER_SERVICEPACKMAJOR: VER_FLAGS = VER_FLAGS(0x0000020);

fn is_os_version_or_greater_internal(major: u16, minor: u16, build: u16, service_pack: u16) -> bool {
    let flags = VER_MAJORVERSION | VER_MINORVERSION | VER_SERVICEPACKMAJOR;

    unsafe {
        let mut mask: u64 = 0;
        mask = VerSetConditionMask(mask, VER_MAJORVERSION, VER_GREATER_EQUAL);
        mask = VerSetConditionMask(mask, VER_MINORVERSION, VER_GREATER_EQUAL);
        mask = VerSetConditionMask(mask, VER_BUILDNUMBER, VER_GREATER_EQUAL);
        mask = VerSetConditionMask(mask, VER_SERVICEPACKMAJOR, VER_GREATER_EQUAL);

        let mut osvi: OSVERSIONINFOEXW = Default::default();
        osvi.dwMajorVersion = major.into();
        osvi.dwMinorVersion = minor.into();
        osvi.dwBuildNumber = build.into();
        osvi.wServicePackMajor = service_pack.into();

        VerifyVersionInfoW(&mut osvi, flags, mask).is_ok()
    }
}

pub fn is_windows_10_or_greater() -> bool {
    is_os_version_or_greater_internal(10, 0, 0, 0)
}

pub fn is_windows_7_sp1_or_greater() -> bool {
    is_os_version_or_greater_internal(6, 1, 0, 1)
}

pub fn is_windows_8_or_greater() -> bool {
    is_os_version_or_greater_internal(6, 2, 0, 0)
}

pub fn is_windows_8_1_or_greater() -> bool {
    is_os_version_or_greater_internal(6, 3, 0, 0)
}

pub fn is_os_version_or_greater(version: &str) -> Result<bool> {
    let (mut major, mut minor, mut build, _) = shared::parse_version(version)?;

    if major < 8 {
        return Ok(is_windows_7_sp1_or_greater());
    }

    if major == 8 {
        return Ok(if minor >= 1 { is_windows_8_or_greater() } else { is_windows_8_1_or_greater() });
    }

    // https://en.wikipedia.org/wiki/List_of_Microsoft_Windows_versions
    if major == 11 {
        if build < 22000 {
            build = 22000;
        }
        major = 10;
        minor = 0;
    }

    Ok(is_os_version_or_greater_internal(major.try_into()?, minor.try_into()?, build.try_into()?, 0))
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

pub fn is_cpu_architecture_supported(architecture: &str) -> Result<bool> {
    let machine = RuntimeArch::from_current_system();
    if machine.is_none() {
        // we can't detect current os arch so try installing anyway
        return Ok(true);
    }

    let architecture = RuntimeArch::from_str(architecture);
    if architecture.is_none() {
        // no arch specified in this package, so install on any arch
        return Ok(true);
    }

    let machine = machine.unwrap();
    let architecture = architecture.unwrap();
    let is_win_11 = is_os_version_or_greater("11")?;

    if machine == RuntimeArch::X86 {
        // windows x86 only supports x86
        Ok(architecture == RuntimeArch::X86)
    } else if machine == RuntimeArch::X64 {
        // windows x64 only supports x86 and x64
        Ok(architecture == RuntimeArch::X86 || architecture == RuntimeArch::X64)
    } else if machine == RuntimeArch::Arm64 {
        // windows arm64 supports x86, and arm64, and only on Windows 11 does it support x64
        Ok(architecture == RuntimeArch::X86 || (architecture == RuntimeArch::X64 && is_win_11) || architecture == RuntimeArch::Arm64)
    } else {
        // we don't know what this is, so try installing anyway
        Ok(true)
    }
}

#[test]
pub fn test_x64_and_x86_is_supported_but_not_arm64_or_invalid() {
    assert!(!is_cpu_architecture_supported("arm64").unwrap());
    assert!(is_cpu_architecture_supported("invalid").unwrap());
    assert!(is_cpu_architecture_supported("x64").unwrap());
    assert!(is_cpu_architecture_supported("x86").unwrap());
}
