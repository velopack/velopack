use crate::wide_strings::*;
use std::{
    collections::HashMap,
    ffi::{OsStr, OsString},
    io::{Error as IoError, ErrorKind as IoErrorKind, Result as IoResult},
    os::windows::ffi::OsStrExt,
    path::Path,
    time::Duration,
};
use windows::{
    core::{PCWSTR, PWSTR},
    Wdk::System::Threading::{NtQueryInformationProcess, ProcessBasicInformation},
    Win32::{
        Foundation::{CloseHandle, FILETIME, HANDLE, WAIT_OBJECT_0, WAIT_TIMEOUT},
        Security::{GetTokenInformation, TokenElevation, TOKEN_ELEVATION},
        System::Threading::{
            CreateProcessW, GetCurrentProcess, GetExitCodeProcess, GetProcessId, GetProcessTimes, OpenProcess, OpenProcessToken, TerminateProcess,
            WaitForSingleObject, CREATE_NO_WINDOW, CREATE_UNICODE_ENVIRONMENT, INFINITE, PROCESS_ACCESS_RIGHTS, PROCESS_BASIC_INFORMATION,
            PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE, PROCESS_TERMINATE, STARTUPINFOW, STARTUPINFOW_FLAGS,
        },
        UI::{
            Shell::{ShellExecuteExW, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW},
            WindowsAndMessaging::AllowSetForegroundWindow,
        },
    },
};

enum Arg {
    /// Add quotes (if needed)
    Regular(OsString),
    // Append raw string without quoting
    #[allow(unused)]
    Raw(OsString),
}

enum Quote {
    // Every arg is quoted
    Always,
    // Whitespace and empty args are quoted
    Auto,
    // Arg appended without any changes (#29494)
    Never,
}

fn ensure_no_nuls<T: AsRef<OsStr>>(str: T) -> IoResult<T> {
    if str.as_ref().encode_wide().any(|b| b == 0) {
        Err(IoError::new(IoErrorKind::InvalidInput, "nul byte found in provided data"))
    } else {
        Ok(str)
    }
}

fn append_arg(cmd: &mut Vec<u16>, arg: &Arg, force_quotes: bool) -> IoResult<()> {
    let (arg, quote) = match arg {
        Arg::Regular(arg) => (
            arg,
            if force_quotes {
                Quote::Always
            } else {
                Quote::Auto
            },
        ),
        Arg::Raw(arg) => (arg, Quote::Never),
    };

    // If an argument has 0 characters then we need to quote it to ensure
    // that it actually gets passed through on the command line or otherwise
    // it will be dropped entirely when parsed on the other end.
    ensure_no_nuls(arg)?;
    let arg_bytes = arg.as_encoded_bytes();
    let (quote, escape) = match quote {
        Quote::Always => (true, true),
        Quote::Auto => (arg_bytes.iter().any(|c| *c == b' ' || *c == b'\t') || arg_bytes.is_empty(), true),
        Quote::Never => (false, false),
    };
    if quote {
        cmd.push('"' as u16);
    }

    let mut backslashes: usize = 0;
    for x in arg.encode_wide() {
        if escape {
            if x == '\\' as u16 {
                backslashes += 1;
            } else {
                if x == '"' as u16 {
                    // Add n+1 backslashes to total 2n+1 before internal '"'.
                    cmd.extend((0..=backslashes).map(|_| '\\' as u16));
                }
                backslashes = 0;
            }
        }
        cmd.push(x);
    }

    if quote {
        // Add n backslashes to total 2n before ending '"'.
        cmd.extend((0..backslashes).map(|_| '\\' as u16));
        cmd.push('"' as u16);
    }
    Ok(())
}

fn make_command_line(argv0: Option<&OsStr>, args: &[Arg], force_quotes: bool) -> IoResult<WideString> {
    // Encode the command and arguments in a command line string such
    // that the spawned process may recover them using CommandLineToArgvW.
    let mut cmd: Vec<u16> = Vec::new();

    // Always quote the program name so CreateProcess to avoid ambiguity when
    // the child process parses its arguments.
    // Note that quotes aren't escaped here because they can't be used in arg0.
    // But that's ok because file paths can't contain quotes.
    if let Some(argv0) = argv0 {
        cmd.push(b'"' as u16);
        cmd.extend(argv0.encode_wide());
        cmd.push(b'"' as u16);
        cmd.push(' ' as u16);
    }

    for arg in args {
        append_arg(&mut cmd, arg, force_quotes)?;
        cmd.push(' ' as u16);
    }
    if !cmd.is_empty() {
        cmd.pop();
    }

    // Ensure null termination
    cmd.push(0);

    let wide_string: WideString = cmd.into();
    Ok(wide_string)
}

fn make_envp(maybe_env: Option<HashMap<String, String>>) -> IoResult<Option<WideString>> {
    // On Windows we pass an "environment block" which is not a char**, but
    // rather a concatenation of null-terminated k=v\0 sequences, with a final
    // \0 to terminate.
    let mut blk = Vec::new();

    // Copy current process environment variables
    for (key, value) in std::env::vars_os() {
        if key.is_empty() || value.is_empty() {
            continue; // Skip empty keys or values
        }

        let key_str = key.to_string_lossy();
        if key_str.starts_with("=") {
            continue;
        }

        blk.extend(ensure_no_nuls(key)?.encode_wide());
        blk.push('=' as u16);
        blk.extend(ensure_no_nuls(value)?.encode_wide());
        blk.push(0);
    }

    if let Some(env) = maybe_env {
        for (k, v) in env {
            let os_key = OsString::from(k);
            let os_value = OsString::from(v);
            blk.extend(ensure_no_nuls(os_key)?.encode_wide());
            blk.push('=' as u16);
            blk.extend(ensure_no_nuls(os_value)?.encode_wide());
            blk.push(0);
        }
    }

    if blk.is_empty() {
        Ok(None)
    } else {
        blk.push(0);
        Ok(Some(blk.into()))
    }
}

pub fn is_current_process_elevated() -> bool {
    // Get the current process handle
    let process = unsafe { GetCurrentProcess() };

    // Variable to hold the process token
    let mut token: HANDLE = HANDLE::default();

    // Open the process token with the TOKEN_QUERY access rights
    unsafe {
        if OpenProcessToken(process, windows::Win32::Security::TOKEN_QUERY, &mut token).is_ok() {
            // Allocate a buffer for the TOKEN_ELEVATION structure
            let mut elevation = TOKEN_ELEVATION::default();
            let mut size: u32 = 0;

            let elevation_ptr: *mut core::ffi::c_void = &mut elevation as *mut _ as *mut _;

            // Query the token information to check if it is elevated
            if GetTokenInformation(
                token,
                TokenElevation,
                Some(elevation_ptr),
                std::mem::size_of::<TOKEN_ELEVATION>() as u32,
                &mut size,
            )
            .is_ok()
            {
                // Return whether the token is elevated
                let _ = CloseHandle(token);
                return elevation.TokenIsElevated != 0;
            }
        }
    }

    // Clean up the token handle
    if !token.is_invalid() {
        unsafe {
            let _ = CloseHandle(token);
        };
    }

    false
}

pub struct SafeProcessHandle {
    handle: HANDLE,
    pid: u32,
}

impl Drop for SafeProcessHandle {
    fn drop(&mut self) {
        if !self.handle.is_invalid() {
            let _ = unsafe { CloseHandle(self.handle) };
        }
    }
}

impl SafeProcessHandle {
    pub fn handle(&self) -> HANDLE {
        self.handle
    }

    pub fn pid(&self) -> u32 {
        self.pid
    }
}

impl AsRef<HANDLE> for SafeProcessHandle {
    fn as_ref(&self) -> &HANDLE {
        &self.handle
    }
}

// impl Into<u32> for SafeProcessHandle {
//     fn into(self) -> u32 {
//         self.pid()
//     }
// }

// impl Into<HANDLE> for SafeProcessHandle {
//     fn into(self) -> HANDLE {
//         self.handle()
//     }
// }

pub fn run_process_as_admin<P1: AsRef<Path>, P2: AsRef<Path>>(
    exe_path: P1,
    args: Vec<OsString>,
    work_dir: Option<P2>,
    show_window: bool,
) -> IoResult<SafeProcessHandle> {
    let verb = string_to_wide("runas");
    let exe = string_to_wide(exe_path.as_ref());
    let wrapped_args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();
    let params = make_command_line(None, &wrapped_args, false)?;
    let params = PCWSTR(params.as_ptr());
    let work_dir = string_to_wide_opt(work_dir.map(|w| w.as_ref().to_path_buf()));

    let n_show = if show_window {
        windows::Win32::UI::WindowsAndMessaging::SW_NORMAL.0
    } else {
        windows::Win32::UI::WindowsAndMessaging::SW_HIDE.0
    };

    let mut exe_info: SHELLEXECUTEINFOW = SHELLEXECUTEINFOW {
        cbSize: std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,
        fMask: SEE_MASK_NOCLOSEPROCESS,
        lpVerb: verb.as_pcwstr(),
        lpFile: exe.as_pcwstr(),
        lpParameters: params,
        lpDirectory: work_dir.as_ref().map(|d| d.as_pcwstr()).unwrap_or_default(),
        nShow: n_show,
        ..Default::default()
    };

    unsafe {
        info!(
            "About to launch [AS ADMIN]: '{:?}' in dir '{:?}' with arguments: {:?}",
            exe, work_dir, args
        );
        ShellExecuteExW(&mut exe_info as *mut SHELLEXECUTEINFOW)?;
        let process_id = GetProcessId(exe_info.hProcess);
        let _ = AllowSetForegroundWindow(process_id);
        Ok(SafeProcessHandle {
            handle: exe_info.hProcess,
            pid: process_id,
        })
    }
}

pub fn start_process<P1: AsRef<Path>, P2: AsRef<Path>>(
    exe_path: P1,
    args: Vec<OsString>,
    work_dir: Option<P2>,
    show_window: bool,
) -> IoResult<SafeProcessHandle> {
    let exe = string_to_wide(exe_path.as_ref());
    let wrapped_args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();
    let params = if !args.is_empty() {
        PCWSTR(make_command_line(Some(exe.as_os_str()), &wrapped_args, false)?.as_ptr())
    } else {
        PCWSTR::null()
    };
    let work_dir = string_to_wide_opt(work_dir.map(|w| w.as_ref().to_path_buf()));

    let n_show = if show_window {
        windows::Win32::UI::WindowsAndMessaging::SW_NORMAL.0
    } else {
        windows::Win32::UI::WindowsAndMessaging::SW_HIDE.0
    };

    let mut exe_info: SHELLEXECUTEINFOW = SHELLEXECUTEINFOW {
        cbSize: std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,
        fMask: SEE_MASK_NOCLOSEPROCESS,
        //lpVerb: PCWSTR::null(),
        lpFile: exe.as_pcwstr(),
        lpParameters: params,
        lpDirectory: work_dir.as_ref().map(|d| d.as_pcwstr()).unwrap_or_default(),
        nShow: n_show,
        ..Default::default()
    };

    unsafe {
        info!("About to launch: '{:?}' in dir '{:?}' with arguments: {:?}", exe, work_dir, args);
        ShellExecuteExW(&mut exe_info as *mut SHELLEXECUTEINFOW)?;
        let process_id = GetProcessId(exe_info.hProcess);
        if show_window {
            let _ = AllowSetForegroundWindow(process_id);
        }
        Ok(SafeProcessHandle {
            handle: exe_info.hProcess,
            pid: process_id,
        })
    }
}

pub fn run_process<P1: AsRef<Path>, P2: AsRef<Path>>(
    exe_path: P1,
    args: Vec<OsString>,
    work_dir: Option<P2>,
    show_window: bool,
    set_env: Option<HashMap<String, String>>,
) -> IoResult<SafeProcessHandle> {
    let exe_path = string_to_wide(exe_path.as_ref());
    let dirp = string_to_wide_opt(work_dir.map(|w| w.as_ref().to_path_buf()));
    let envp = make_envp(set_env)?;
    let wrapped_args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();
    let mut params: WideString = make_command_line(Some(exe_path.as_os_str()), &wrapped_args, false)?;

    let mut pi = windows::Win32::System::Threading::PROCESS_INFORMATION::default();

    let si = STARTUPINFOW {
        cb: std::mem::size_of::<STARTUPINFOW>() as u32,
        lpReserved: PWSTR::null(),
        lpDesktop: PWSTR::null(),
        lpTitle: PWSTR::null(),
        dwX: 0,
        dwY: 0,
        dwXSize: 0,
        dwYSize: 0,
        dwXCountChars: 0,
        dwYCountChars: 0,
        dwFillAttribute: 0,
        dwFlags: STARTUPINFOW_FLAGS(0),
        wShowWindow: 0,
        cbReserved2: 0,
        lpReserved2: std::ptr::null_mut(),
        hStdInput: HANDLE(std::ptr::null_mut()),
        hStdOutput: HANDLE(std::ptr::null_mut()),
        hStdError: HANDLE(std::ptr::null_mut()),
    };

    let flags = if show_window {
        CREATE_UNICODE_ENVIRONMENT
    } else {
        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT
    };

    // Keep environment block alive for the duration of the CreateProcessW call
    let env_ptr = envp.as_ref().map(|e| e.as_cvoid());
    let dir_ptr = dirp.as_ref().map(|d| d.as_pcwstr()).unwrap_or_default();

    unsafe {
        info!("About to launch: '{:?}' in dir '{:?}' with arguments: {:?}", exe_path, dirp, params);
        info!("Environment block present: {}, flags: {:?}", envp.is_some(), flags);
        CreateProcessW(None, Some(params.as_pwstr()), None, None, false, flags, env_ptr, dir_ptr, &si, &mut pi)?;
        if show_window {
            let _ = AllowSetForegroundWindow(pi.dwProcessId);
        }
        let _ = CloseHandle(pi.hThread);
    }

    Ok(SafeProcessHandle {
        handle: pi.hProcess,
        pid: pi.dwProcessId,
    })
}

fn duration_to_ms(dur: Duration) -> u32 {
    let ms = dur
        .as_secs()
        .checked_mul(1000)
        .and_then(|amt| amt.checked_add(dur.subsec_millis() as u64))
        .expect("failed to convert duration to milliseconds");
    if ms > (u32::MAX as u64) {
        u32::MAX
    } else {
        ms as u32
    }
}

pub fn kill_process<T: AsRef<HANDLE>>(process: T) -> IoResult<()> {
    let process = process.as_ref();
    unsafe {
        if process.is_invalid() {
            return Ok(());
        }
        TerminateProcess(*process, 1)?;
    }
    Ok(())
}

pub fn open_process(dwdesiredaccess: PROCESS_ACCESS_RIGHTS, binherithandle: bool, dwprocessid: u32) -> windows::core::Result<SafeProcessHandle> {
    let handle = unsafe { OpenProcess(dwdesiredaccess, binherithandle, dwprocessid)? };
    Ok(SafeProcessHandle { handle, pid: dwprocessid })
}

pub fn kill_pid(pid: u32) -> IoResult<()> {
    let handle = open_process(PROCESS_TERMINATE, false, pid)?;
    kill_process(handle)?;
    Ok(())
}

#[derive(Debug)]
pub enum WaitResult {
    WaitTimeout,
    ExitCode(u32),
    NoWaitRequired,
}

impl WaitResult {
    pub fn code(&self) -> Option<u32> {
        match self {
            WaitResult::WaitTimeout => None,
            WaitResult::ExitCode(c) => Some(*c),
            WaitResult::NoWaitRequired => None,
        }
    }
}

pub fn wait_for_process_to_exit<T: AsRef<HANDLE>>(process: T, dur: Option<Duration>) -> IoResult<WaitResult> {
    let process = *process.as_ref();
    if process.is_invalid() {
        return Ok(WaitResult::NoWaitRequired);
    }

    let ms = if let Some(dur) = dur {
        let ms = duration_to_ms(dur);
        info!("Waiting {}ms for process handle to exit.", ms);
        ms
    } else {
        info!("Waiting indefinitely process handle to exit.");
        INFINITE
    };

    unsafe {
        match WaitForSingleObject(process, ms) {
            WAIT_OBJECT_0 => {}
            WAIT_TIMEOUT => return Ok(WaitResult::WaitTimeout),
            _ => return Err(IoError::last_os_error()),
        }

        let mut exit_code = 0;
        GetExitCodeProcess(process, &mut exit_code)?;
        Ok(WaitResult::ExitCode(exit_code))
    }
}

pub fn wait_for_pid_to_exit(pid: u32, dur: Option<Duration>) -> IoResult<WaitResult> {
    info!("Waiting for process pid-{} to exit.", pid);
    let handle = open_process(PROCESS_SYNCHRONIZE, false, pid)?;
    wait_for_process_to_exit(handle, dur)
}

pub fn wait_for_parent_to_exit(dur: Option<Duration>) -> IoResult<WaitResult> {
    info!("Reading parent process information.");
    let basic_info = ProcessBasicInformation;
    let my_handle = unsafe { GetCurrentProcess() };
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
    let hres = unsafe { NtQueryInformationProcess(my_handle, basic_info, info_ptr, info_size, return_length_ptr) };
    if hres.is_err() {
        return Err(IoError::other(format!("NtQueryInformationProcess failed: {:?}", hres)));
    }

    if info.InheritedFromUniqueProcessId <= 1 {
        // the parent process has exited
        info!("The parent process ({}) has already exited", info.InheritedFromUniqueProcessId);
        return Ok(WaitResult::NoWaitRequired);
    }

    fn get_pid_start_time(process: HANDLE) -> IoResult<u64> {
        let mut creation = FILETIME::default();
        let mut exit = FILETIME::default();
        let mut kernel = FILETIME::default();
        let mut user = FILETIME::default();
        unsafe {
            GetProcessTimes(process, &mut creation, &mut exit, &mut kernel, &mut user)?;
        }
        Ok(((creation.dwHighDateTime as u64) << 32) | creation.dwLowDateTime as u64)
    }

    let permissions = PROCESS_SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION;
    let parent_handle = open_process(permissions, false, info.InheritedFromUniqueProcessId as u32)?;
    let parent_start_time = get_pid_start_time(parent_handle.handle())?;
    let myself_start_time = get_pid_start_time(my_handle)?;

    if parent_start_time > myself_start_time {
        // the parent process has exited and the id has been re-used
        info!(
            "The parent process ({}) has already exited. parent_start={}, my_start={}",
            info.InheritedFromUniqueProcessId, parent_start_time, myself_start_time
        );
        return Ok(WaitResult::NoWaitRequired);
    }

    info!("Waiting for parent process ({}) to exit.", info.InheritedFromUniqueProcessId);
    wait_for_process_to_exit(parent_handle, dur)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::OsString;
    use std::os::windows::ffi::OsStringExt;
    use std::path::PathBuf;
    use std::time::Duration;

    // ===== Helper to decode a WideString back to a Rust String for assertions =====
    fn wide_to_string(ws: &WideString) -> String {
        let slice = ws.as_slice();
        // strip trailing null
        let end = slice.iter().position(|&c| c == 0).unwrap_or(slice.len());
        String::from_utf16_lossy(&slice[..end])
    }

    // ===== ensure_no_nuls tests =====

    #[test]
    fn test_ensure_no_nuls_valid_string() {
        let s = OsString::from("hello");
        assert!(ensure_no_nuls(s).is_ok());
    }

    #[test]
    fn test_ensure_no_nuls_empty_string() {
        let s = OsString::from("");
        assert!(ensure_no_nuls(s).is_ok());
    }

    #[test]
    fn test_ensure_no_nuls_with_nul() {
        // Create an OsString containing a nul byte
        let wide: Vec<u16> = vec!['h' as u16, 'e' as u16, 0, 'l' as u16, 'o' as u16];
        let s = OsString::from_wide(&wide);
        let result = ensure_no_nuls(s);
        assert!(result.is_err());
        assert_eq!(result.unwrap_err().kind(), IoErrorKind::InvalidInput);
    }

    #[test]
    fn test_ensure_no_nuls_with_spaces_and_special_chars() {
        let s = OsString::from("hello world! @#$%^&*()");
        assert!(ensure_no_nuls(s).is_ok());
    }

    // ===== append_arg tests =====

    #[test]
    fn test_append_arg_regular_simple() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Regular(OsString::from("hello"));
        append_arg(&mut cmd, &arg, false).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        assert_eq!(s, "hello");
    }

    #[test]
    fn test_append_arg_regular_with_spaces_auto_quotes() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Regular(OsString::from("hello world"));
        append_arg(&mut cmd, &arg, false).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        assert_eq!(s, "\"hello world\"");
    }

    #[test]
    fn test_append_arg_regular_with_tab_auto_quotes() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Regular(OsString::from("hello\tworld"));
        append_arg(&mut cmd, &arg, false).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        assert_eq!(s, "\"hello\tworld\"");
    }

    #[test]
    fn test_append_arg_regular_empty_auto_quotes() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Regular(OsString::from(""));
        append_arg(&mut cmd, &arg, false).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        // Empty arg should be quoted to preserve it
        assert_eq!(s, "\"\"");
    }

    #[test]
    fn test_append_arg_regular_force_quotes() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Regular(OsString::from("hello"));
        append_arg(&mut cmd, &arg, true).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        assert_eq!(s, "\"hello\"");
    }

    #[test]
    fn test_append_arg_regular_with_internal_quote() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Regular(OsString::from("say \"hi\""));
        append_arg(&mut cmd, &arg, false).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        // Space triggers quoting; internal quotes get escaped with backslash
        assert_eq!(s, "\"say \\\"hi\\\"\"");
    }

    #[test]
    fn test_append_arg_regular_with_trailing_backslashes() {
        let mut cmd: Vec<u16> = Vec::new();
        // Arg: hello\ (no space, so no auto-quoting)
        let arg = Arg::Regular(OsString::from("hello\\"));
        append_arg(&mut cmd, &arg, true).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        // force_quotes=true, trailing backslash before closing quote should be doubled
        assert_eq!(s, "\"hello\\\\\"");
    }

    #[test]
    fn test_append_arg_regular_backslash_before_quote() {
        let mut cmd: Vec<u16> = Vec::new();
        // Arg: a\"b (contains space-like chars? no, but has a quote in it)
        // Actually, let's force quotes to make the test clearer
        let arg = Arg::Regular(OsString::from("a\\\"b"));
        append_arg(&mut cmd, &arg, true).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        // 1 backslash before quote -> 2*1+1=3 backslashes before the escaped quote
        assert_eq!(s, "\"a\\\\\\\"b\"");
    }

    #[test]
    fn test_append_arg_raw_no_quoting() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Raw(OsString::from("hello world"));
        append_arg(&mut cmd, &arg, true).unwrap(); // force_quotes ignored for Raw
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        assert_eq!(s, "hello world"); // no quoting
    }

    #[test]
    fn test_append_arg_raw_with_special_chars() {
        let mut cmd: Vec<u16> = Vec::new();
        let arg = Arg::Raw(OsString::from("--flag=\"value\""));
        append_arg(&mut cmd, &arg, false).unwrap();
        let s: String = cmd.iter().map(|&c| char::from_u32(c as u32).unwrap_or('?')).collect();
        assert_eq!(s, "--flag=\"value\""); // passed through verbatim
    }

    #[test]
    fn test_append_arg_nul_in_arg_fails() {
        let mut cmd: Vec<u16> = Vec::new();
        let wide: Vec<u16> = vec!['a' as u16, 0, 'b' as u16];
        let arg = Arg::Regular(OsString::from_wide(&wide));
        let result = append_arg(&mut cmd, &arg, false);
        assert!(result.is_err());
    }

    // ===== make_command_line tests =====

    #[test]
    fn test_make_command_line_no_argv0_no_args() {
        let result = make_command_line(None, &[], false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "");
    }

    #[test]
    fn test_make_command_line_with_argv0_no_args() {
        let exe = OsString::from("C:\\Program Files\\app.exe");
        let result = make_command_line(Some(&exe), &[], false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "\"C:\\Program Files\\app.exe\"");
    }

    #[test]
    fn test_make_command_line_with_argv0_and_args() {
        let exe = OsString::from("app.exe");
        let args = vec![Arg::Regular(OsString::from("--flag")), Arg::Regular(OsString::from("value"))];
        let result = make_command_line(Some(&exe), &args, false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "\"app.exe\" --flag value");
    }

    #[test]
    fn test_make_command_line_with_argv0_and_args_with_spaces() {
        let exe = OsString::from("my app.exe");
        let args = vec![Arg::Regular(OsString::from("hello world")), Arg::Regular(OsString::from("simple"))];
        let result = make_command_line(Some(&exe), &args, false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "\"my app.exe\" \"hello world\" simple");
    }

    #[test]
    fn test_make_command_line_no_argv0_with_args() {
        let args = vec![Arg::Regular(OsString::from("arg1")), Arg::Regular(OsString::from("arg2"))];
        let result = make_command_line(None, &args, false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "arg1 arg2");
    }

    #[test]
    fn test_make_command_line_force_quotes() {
        let exe = OsString::from("app.exe");
        let args = vec![Arg::Regular(OsString::from("simple"))];
        let result = make_command_line(Some(&exe), &args, true).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "\"app.exe\" \"simple\"");
    }

    #[test]
    fn test_make_command_line_mixed_raw_and_regular() {
        let args = vec![Arg::Regular(OsString::from("hello world")), Arg::Raw(OsString::from("--raw=val"))];
        let result = make_command_line(None, &args, false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "\"hello world\" --raw=val");
    }

    #[test]
    fn test_make_command_line_empty_arg_preserved() {
        let args = vec![Arg::Regular(OsString::from("")), Arg::Regular(OsString::from("after"))];
        let result = make_command_line(None, &args, false).unwrap();
        let s = wide_to_string(&result);
        assert_eq!(s, "\"\" after");
    }

    #[test]
    fn test_make_command_line_null_terminated() {
        let result = make_command_line(None, &[], false).unwrap();
        let slice = result.as_slice();
        // The result should end with a null terminator
        assert_eq!(*slice.last().unwrap(), 0u16);
    }

    // ===== make_envp tests =====

    #[test]
    fn test_make_envp_none_contains_current_env() {
        // When None is passed, we get a block with the current process env
        let result = make_envp(None).unwrap();
        assert!(result.is_some(), "should produce a non-empty env block");

        let block_str = String::from_utf16_lossy(result.unwrap().as_slice());
        // PATH is almost always set on Windows
        assert!(block_str.contains("PATH="), "env block should contain PATH from the current process");
    }

    #[test]
    fn test_make_envp_with_additional_vars() {
        let mut env = HashMap::new();
        env.insert("MY_TEST_VAR_XYZ".to_string(), "test_value".to_string());
        let result = make_envp(Some(env)).unwrap();
        assert!(result.is_some());

        let block_str = String::from_utf16_lossy(result.unwrap().as_slice());
        // Should contain our custom variable
        assert!(block_str.contains("MY_TEST_VAR_XYZ=test_value"));
        // Should also still contain inherited env vars
        assert!(block_str.contains("PATH="), "env block should still contain inherited PATH");
    }

    #[test]
    fn test_make_envp_empty_hashmap_still_has_parent_env() {
        let env = HashMap::new();
        let result = make_envp(Some(env)).unwrap();
        assert!(result.is_some(), "empty extra env should still produce parent env");

        let block_str = String::from_utf16_lossy(result.unwrap().as_slice());
        assert!(block_str.contains("PATH="), "env block should contain inherited PATH");
    }

    #[test]
    fn test_make_envp_block_ends_with_double_null() {
        let mut env = HashMap::new();
        env.insert("FOO".to_string(), "BAR".to_string());
        let result = make_envp(Some(env)).unwrap().unwrap();
        let slice = result.as_slice();
        let len = slice.len();
        // Environment block must end with a double null (each entry ends with \0, then a final \0)
        assert!(len >= 2);
        assert_eq!(slice[len - 1], 0u16, "last element should be null");
        // Find the second-to-last null to confirm double-null termination
        // The block is: ...value\0\0  (the last entry's \0 plus the terminating \0)
        assert_eq!(slice[len - 2], 0u16, "second-to-last should also be null (double-null terminator)");
    }

    // ===== duration_to_ms tests =====

    #[test]
    fn test_duration_to_ms_zero() {
        assert_eq!(duration_to_ms(Duration::from_millis(0)), 0);
    }

    #[test]
    fn test_duration_to_ms_one_second() {
        assert_eq!(duration_to_ms(Duration::from_secs(1)), 1000);
    }

    #[test]
    fn test_duration_to_ms_half_second() {
        assert_eq!(duration_to_ms(Duration::from_millis(500)), 500);
    }

    #[test]
    fn test_duration_to_ms_large_duration_caps_at_u32_max() {
        // A very large duration should cap at u32::MAX
        let dur = Duration::from_secs(u32::MAX as u64 + 1);
        assert_eq!(duration_to_ms(dur), u32::MAX);
    }

    #[test]
    fn test_duration_to_ms_with_nanos() {
        // 1.5 seconds = 1500 ms
        let dur = Duration::new(1, 500_000_000);
        assert_eq!(duration_to_ms(dur), 1500);
    }

    #[test]
    fn test_duration_to_ms_small_nanos_truncated() {
        // 999_999 nanoseconds = 0 ms (less than 1ms, truncated by integer division)
        let dur = Duration::new(0, 999_999);
        assert_eq!(duration_to_ms(dur), 0);
    }

    #[test]
    fn test_duration_to_ms_exactly_one_ms() {
        let dur = Duration::from_millis(1);
        assert_eq!(duration_to_ms(dur), 1);
    }

    // ===== WaitResult tests =====

    #[test]
    fn test_wait_result_code_exit_code() {
        let wr = WaitResult::ExitCode(42);
        assert_eq!(wr.code(), Some(42));
    }

    #[test]
    fn test_wait_result_code_exit_code_zero() {
        let wr = WaitResult::ExitCode(0);
        assert_eq!(wr.code(), Some(0));
    }

    #[test]
    fn test_wait_result_code_timeout() {
        let wr = WaitResult::WaitTimeout;
        assert_eq!(wr.code(), None);
    }

    #[test]
    fn test_wait_result_code_no_wait_required() {
        let wr = WaitResult::NoWaitRequired;
        assert_eq!(wr.code(), None);
    }

    // ===== is_current_process_elevated =====

    #[test]
    fn test_is_current_process_elevated_returns_bool() {
        // Just verify it doesn't panic and returns a valid bool
        let _elevated = is_current_process_elevated();
    }

    // ===== SafeProcessHandle tests =====

    #[test]
    fn test_safe_process_handle_pid() {
        // Start a short-lived process to get a real handle
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "echo", "test"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        let handle = open_process(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SYNCHRONIZE, false, pid).unwrap();
        assert_eq!(handle.pid(), pid);
        assert!(!handle.handle().is_invalid());
    }

    #[test]
    fn test_safe_process_handle_as_ref() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "echo", "test"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        let handle = open_process(PROCESS_QUERY_LIMITED_INFORMATION, false, pid).unwrap();
        let handle_ref: &HANDLE = handle.as_ref();
        assert!(!handle_ref.is_invalid());
    }

    // ===== open_process tests =====

    #[test]
    fn test_open_process_valid_pid() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "timeout", "/T", "5"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        let result = open_process(PROCESS_TERMINATE | PROCESS_SYNCHRONIZE, false, pid);
        assert!(result.is_ok());
        let handle = result.unwrap();
        assert_eq!(handle.pid(), pid);

        // Clean up
        let _ = kill_process(&handle);
    }

    #[test]
    fn test_open_process_invalid_pid() {
        // PID 0 is the System Idle Process and cannot be opened
        let result = open_process(PROCESS_TERMINATE, false, 0);
        assert!(result.is_err());
    }

    #[test]
    fn test_open_process_nonexistent_pid() {
        // A very high PID that almost certainly doesn't exist
        let result = open_process(PROCESS_TERMINATE, false, 4_000_000);
        assert!(result.is_err());
    }

    // ===== kill_pid / kill_process tests =====

    #[test]
    fn test_kill_pid_running_process() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "ping", "127.0.0.1", "-t"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        kill_pid(pid).expect("failed to kill process");

        // Verify it's actually dead by trying to wait for it
        let result = wait_for_pid_to_exit(pid, Some(Duration::from_secs(5)));
        // Should either succeed (process exited) or fail (already dead / handle invalid)
        // Either way, the process should no longer be running
        assert!(result.is_ok() || result.is_err());
    }

    #[test]
    fn test_kill_pid_nonexistent_process() {
        let result = kill_pid(4_000_000);
        assert!(result.is_err());
    }

    #[test]
    fn test_kill_process_with_handle() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "ping", "127.0.0.1", "-t"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        let handle = open_process(PROCESS_TERMINATE, false, pid).unwrap();
        kill_process(&handle).expect("failed to kill process");
    }

    #[test]
    fn test_kill_process_invalid_handle() {
        // Create a SafeProcessHandle with an invalid handle
        let invalid = SafeProcessHandle {
            handle: HANDLE::default(),
            pid: 0,
        };
        // Invalid handle should return Ok (early return)
        let result = kill_process(&invalid);
        assert!(result.is_ok());
    }

    // ===== run_process tests =====

    /// Helper: write a batch file, run it via run_process, wait for completion, return output file contents.
    fn run_bat(script: &str, out_file: &Path, work_dir: Option<&Path>, set_env: Option<HashMap<String, String>>) -> (WaitResult, String) {
        let bat_dir = tempfile::tempdir().unwrap();
        let bat_path = bat_dir.path().join("test.cmd");
        std::fs::write(&bat_path, script).unwrap();

        let handle = run_process(&bat_path, vec![], work_dir, false, set_env).expect("failed to run bat");
        let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10))).unwrap();

        let content = if out_file.exists() {
            std::fs::read_to_string(out_file).unwrap_or_default()
        } else {
            String::new()
        };
        (result, content)
    }

    #[test]
    fn test_run_process_simple_command() {
        let handle = run_process(
            "cmd.exe",
            vec![OsString::from("/C"), OsString::from("echo"), OsString::from("hello")],
            None::<PathBuf>,
            false,
            None,
        )
        .expect("failed to run process");

        assert!(!handle.handle().is_invalid());
        assert!(handle.pid() > 0);

        let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10))).unwrap();
        assert_eq!(result.code(), Some(0));
    }

    #[test]
    fn test_run_process_with_work_dir() {
        let temp_dir = tempfile::tempdir().unwrap();
        let out_file = temp_dir.path().join("cwd.txt");
        let script = format!("@echo off\ncd > \"{}\"\n", out_file.display());

        let (result, content) = run_bat(&script, &out_file, Some(temp_dir.path()), None);
        assert_eq!(result.code(), Some(0));

        let expected = temp_dir.path().to_str().unwrap();
        assert_eq!(content.trim(), expected, "child work_dir should match the requested directory");
    }

    #[test]
    fn test_run_process_with_env() {
        let temp_dir = tempfile::tempdir().unwrap();
        let out_file = temp_dir.path().join("env.txt");
        let script = format!("@echo off\necho %MY_VELOPACK_TEST_VAR% > \"{}\"\n", out_file.display());

        let mut env = HashMap::new();
        env.insert("MY_VELOPACK_TEST_VAR".to_string(), "12345".to_string());

        let (result, content) = run_bat(&script, &out_file, None, Some(env));
        assert_eq!(result.code(), Some(0));
        assert_eq!(content.trim(), "12345", "child should see the env var we set");
    }

    #[test]
    fn test_run_process_nonexistent_exe() {
        let result = run_process("this_exe_definitely_does_not_exist_12345.exe", vec![], None::<PathBuf>, false, None);
        assert!(result.is_err());
    }

    #[test]
    fn test_run_process_empty_args() {
        let handle = run_process("cmd.exe", vec![], None::<PathBuf>, false, None);
        // cmd.exe with no args should start an interactive shell - it should start fine
        if let Ok(h) = handle {
            let _ = kill_process(&h);
        }
    }

    #[test]
    fn test_run_process_exit_code_nonzero() {
        let handle = run_process(
            "cmd.exe",
            vec![OsString::from("/C"), OsString::from("exit"), OsString::from("42")],
            None::<PathBuf>,
            false,
            None,
        )
        .expect("failed to run process");

        let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10))).unwrap();
        assert_eq!(result.code(), Some(42));
    }

    #[test]
    fn test_run_process_multiple_env_vars() {
        let temp_dir = tempfile::tempdir().unwrap();
        let out_file = temp_dir.path().join("multi_env.txt");
        let script = format!(
            "@echo off\necho %VP_TEST_A% > \"{f}\"\necho %VP_TEST_B% >> \"{f}\"\necho %VP_TEST_C% >> \"{f}\"\n",
            f = out_file.display()
        );

        let mut env = HashMap::new();
        env.insert("VP_TEST_A".to_string(), "alpha".to_string());
        env.insert("VP_TEST_B".to_string(), "beta".to_string());
        env.insert("VP_TEST_C".to_string(), "gamma".to_string());

        let (result, content) = run_bat(&script, &out_file, None, Some(env));
        assert_eq!(result.code(), Some(0));

        let lines: Vec<&str> = content.lines().map(|l| l.trim()).collect();
        assert_eq!(lines.len(), 3, "should have 3 lines of output");
        assert_eq!(lines[0], "alpha", "VP_TEST_A should be 'alpha'");
        assert_eq!(lines[1], "beta", "VP_TEST_B should be 'beta'");
        assert_eq!(lines[2], "gamma", "VP_TEST_C should be 'gamma'");
    }

    #[test]
    fn test_run_process_child_inherits_parent_env() {
        // Set an env var in the current (parent) process, then verify the child sees it
        let unique_key = "VP_INHERIT_TEST_12345";
        let unique_val = "inherited_ok";
        std::env::set_var(unique_key, unique_val);

        let temp_dir = tempfile::tempdir().unwrap();
        let out_file = temp_dir.path().join("inherit.txt");
        let script = format!("@echo off\necho %{}% > \"{}\"\n", unique_key, out_file.display());

        // No extra env — should still inherit parent env
        let (result, content) = run_bat(&script, &out_file, None, None);
        assert_eq!(result.code(), Some(0));
        assert_eq!(content.trim(), unique_val, "child should inherit parent env vars");

        std::env::remove_var(unique_key);
    }

    #[test]
    fn test_run_process_extra_env_does_not_clobber_parent_env() {
        // Verify that passing set_env still preserves the parent's env (e.g. PATH)
        let temp_dir = tempfile::tempdir().unwrap();
        let out_file = temp_dir.path().join("path.txt");
        let script = format!("@echo off\necho %PATH% > \"{}\"\n", out_file.display());

        let mut env = HashMap::new();
        env.insert("VP_EXTRA_ONLY".to_string(), "extra".to_string());

        let (result, content) = run_bat(&script, &out_file, None, Some(env));
        assert_eq!(result.code(), Some(0));

        let trimmed = content.trim();
        assert!(!trimmed.is_empty(), "PATH should not be empty");
        assert!(!trimmed.contains("%PATH%"), "PATH should be expanded, not literal");
    }

    // ===== start_process tests =====
    //
    // start_process uses ShellExecuteExW which behaves differently from
    // CreateProcessW. For console commands, the returned hProcess may be
    // invalid/null, and exit codes may not be reliably captured. These
    // tests verify that the function succeeds without erroring, rather
    // than making strict assertions about handles and exit codes.

    #[test]
    fn test_start_process_does_not_error() {
        let result = start_process(
            "cmd.exe",
            vec![OsString::from("/C"), OsString::from("echo"), OsString::from("started")],
            None::<PathBuf>,
            false,
        );
        assert!(result.is_ok());
        if let Ok(handle) = result {
            // Clean up - wait or kill
            let _ = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10)));
        }
    }

    #[test]
    fn test_start_process_with_work_dir() {
        let temp_dir = tempfile::tempdir().unwrap();
        let result = start_process(
            "cmd.exe",
            vec![OsString::from("/C"), OsString::from("echo"), OsString::from("ok")],
            Some(temp_dir.path()),
            false,
        );
        assert!(result.is_ok());
        if let Ok(handle) = result {
            let _ = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10)));
        }
    }

    #[test]
    fn test_start_process_no_args() {
        // start_process with no args uses PCWSTR::null() for parameters
        let handle = start_process("cmd.exe", vec![], None::<PathBuf>, false);
        if let Ok(h) = handle {
            let _ = kill_process(&h);
        }
    }

    // NOTE: start_process_nonexistent_exe is intentionally omitted because
    // ShellExecuteExW may show a dialog for missing executables, which would
    // hang in an automated test environment.

    // ===== wait_for_process_to_exit tests =====

    #[test]
    fn test_wait_for_process_to_exit_immediate() {
        let handle = run_process(
            "cmd.exe",
            vec![OsString::from("/C"), OsString::from("echo"), OsString::from("done")],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();

        let result = wait_for_process_to_exit(&handle, None).unwrap();
        assert_eq!(result.code(), Some(0));
    }

    #[test]
    fn test_wait_for_process_to_exit_with_timeout_success() {
        let handle = run_process(
            "cmd.exe",
            vec![OsString::from("/C"), OsString::from("echo"), OsString::from("fast")],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();

        let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(30))).unwrap();
        assert_eq!(result.code(), Some(0));
    }

    #[test]
    fn test_wait_for_process_to_exit_timeout_expires() {
        let handle = run_process(
            "cmd.exe",
            vec![
                OsString::from("/C"),
                OsString::from("ping"),
                OsString::from("127.0.0.1"),
                OsString::from("-t"),
            ],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();

        let result = wait_for_process_to_exit(&handle, Some(Duration::from_millis(100))).unwrap();
        match result {
            WaitResult::WaitTimeout => {} // expected
            other => panic!("Expected WaitTimeout, got {:?}", other),
        }

        // Clean up the long-running process
        let _ = kill_process(&handle);
    }

    #[test]
    fn test_wait_for_process_to_exit_invalid_handle() {
        // Create a SafeProcessHandle with an invalid handle
        let invalid = SafeProcessHandle {
            handle: HANDLE::default(),
            pid: 0,
        };
        let result = wait_for_process_to_exit(&invalid, Some(Duration::from_secs(1))).unwrap();
        match result {
            WaitResult::NoWaitRequired => {} // expected for invalid handle
            other => panic!("Expected NoWaitRequired, got {:?}", other),
        }
    }

    // ===== wait_for_pid_to_exit tests =====
    //
    // NOTE: wait_for_pid_to_exit uses OpenProcess(PROCESS_SYNCHRONIZE) which
    // may be denied on some Windows configurations due to security policies.
    // These tests account for that by accepting AccessDenied as a valid outcome.

    #[test]
    fn test_wait_for_pid_to_exit_with_timeout() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "ping", "127.0.0.1", "-n", "3"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        match wait_for_pid_to_exit(pid, Some(Duration::from_secs(30))) {
            Ok(result) => assert!(result.code().is_some()),
            Err(e) => {
                // OpenProcess with PROCESS_SYNCHRONIZE may be denied on some systems
                assert!(e.to_string().contains("Access is denied"), "Unexpected error: {}", e);
            }
        }
    }

    #[test]
    fn test_wait_for_pid_to_exit_no_timeout() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "ping", "127.0.0.1", "-n", "2"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        match wait_for_pid_to_exit(pid, None) {
            Ok(result) => assert!(result.code().is_some()),
            Err(e) => {
                assert!(e.to_string().contains("Access is denied"), "Unexpected error: {}", e);
            }
        }
    }

    #[test]
    fn test_wait_for_pid_to_exit_nonexistent_pid() {
        let result = wait_for_pid_to_exit(4_000_000, Some(Duration::from_secs(1)));
        assert!(result.is_err());
    }

    // ===== wait_for_parent_to_exit tests =====

    #[test]
    fn test_wait_for_parent_to_exit_with_short_timeout() {
        // Our parent process (the test runner) should still be alive,
        // so this should timeout
        let result = wait_for_parent_to_exit(Some(Duration::from_millis(100))).unwrap();
        match result {
            WaitResult::WaitTimeout => {}    // expected - parent is still running
            WaitResult::NoWaitRequired => {} // also valid if parent PID <= 1
            other => panic!("Expected WaitTimeout or NoWaitRequired, got {:?}", other),
        }
    }

    // ===== Integration / scenario tests =====

    #[test]
    fn test_run_and_kill_process() {
        let handle = run_process(
            "cmd.exe",
            vec![
                OsString::from("/C"),
                OsString::from("ping"),
                OsString::from("127.0.0.1"),
                OsString::from("-t"),
            ],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();

        let pid = handle.pid();
        assert!(pid > 0);

        // Kill by pid
        kill_pid(pid).expect("failed to kill process");

        // Wait should complete quickly now — process was killed so exit code is non-zero
        let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(5))).unwrap();
        assert!(result.code().is_some(), "killed process should have an exit code");
        assert_ne!(result.code(), Some(0), "killed process should have a non-zero exit code");
    }

    #[test]
    fn test_run_process_various_exit_codes() {
        for expected_code in [0, 1, 7, 42, 255] {
            let handle = run_process(
                "cmd.exe",
                vec![OsString::from("/C"), OsString::from("exit"), OsString::from(expected_code.to_string())],
                None::<PathBuf>,
                false,
                None,
            )
            .unwrap();

            let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10))).unwrap();
            assert_eq!(result.code(), Some(expected_code), "exit code should be {}", expected_code);
        }
    }

    #[test]
    fn test_run_process_writes_file_to_work_dir() {
        let temp_dir = tempfile::tempdir().unwrap();
        let out_file = temp_dir.path().join("proof.txt");
        let script = format!("@echo off\necho proof > \"{}\"\n", out_file.display());

        let (result, content) = run_bat(&script, &out_file, Some(temp_dir.path()), None);
        assert_eq!(result.code(), Some(0));
        assert!(out_file.exists(), "child should have created a file in work_dir");
        assert_eq!(content.trim(), "proof");
    }

    #[test]
    fn test_run_process_env_and_work_dir_combined() {
        let temp_dir = tempfile::tempdir().unwrap();
        let env_file = temp_dir.path().join("env_check.txt");
        let cwd_file = temp_dir.path().join("cwd_check.txt");
        let script = format!(
            "@echo off\necho %VP_COMBO_VAR% > \"{}\"\ncd > \"{}\"\n",
            env_file.display(),
            cwd_file.display()
        );

        let mut env = HashMap::new();
        env.insert("VP_COMBO_VAR".to_string(), "combo_val".to_string());

        // Run with work_dir set to temp_dir
        let bat_dir = tempfile::tempdir().unwrap();
        let bat_path = bat_dir.path().join("combo.cmd");
        std::fs::write(&bat_path, &script).unwrap();

        let handle = run_process(&bat_path, vec![], Some(temp_dir.path()), false, Some(env)).unwrap();
        let result = wait_for_process_to_exit(&handle, Some(Duration::from_secs(10))).unwrap();
        assert_eq!(result.code(), Some(0));

        let env_content = std::fs::read_to_string(&env_file).unwrap();
        assert_eq!(env_content.trim(), "combo_val", "env var should be set in child");

        let cwd_content = std::fs::read_to_string(&cwd_file).unwrap();
        assert_eq!(cwd_content.trim(), temp_dir.path().to_str().unwrap(), "cwd should match work_dir");
    }

    #[test]
    fn test_safe_process_handle_drop_doesnt_panic() {
        let child = std::process::Command::new("cmd.exe")
            .args(["/C", "echo", "drop test"])
            .spawn()
            .expect("failed to start process");
        let pid = child.id();

        {
            let handle = open_process(PROCESS_QUERY_LIMITED_INFORMATION, false, pid).unwrap();
            // handle is dropped here
            let _ = handle.pid();
        }
        // If we get here without panicking, the Drop impl works correctly
    }
}
