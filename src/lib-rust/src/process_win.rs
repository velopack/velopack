use std::{
    collections::HashMap,
    ffi::{OsStr, OsString},
    io::{Error as IoError, ErrorKind as IoErrorKind, Result as IoResult},
    os::{raw::c_void, windows::ffi::OsStrExt},
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
            CreateProcessW, GetCurrentProcess, GetExitCodeProcess, GetProcessId, GetProcessTimes, OpenProcess, OpenProcessToken,
            TerminateProcess, WaitForSingleObject, CREATE_NO_WINDOW, CREATE_UNICODE_ENVIRONMENT, INFINITE, PROCESS_ACCESS_RIGHTS,
            PROCESS_BASIC_INFORMATION, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE, PROCESS_TERMINATE, STARTUPINFOW,
            STARTUPINFOW_FLAGS,
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

fn make_command_line(argv0: Option<&OsStr>, args: &[Arg], force_quotes: bool) -> IoResult<Vec<u16>> {
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

    cmd.push(0);
    Ok(cmd)
}

fn make_envp(maybe_env: Option<HashMap<String, String>>) -> IoResult<(Option<*const c_void>, Vec<u16>)> {
    // On Windows we pass an "environment block" which is not a char**, but
    // rather a concatenation of null-terminated k=v\0 sequences, with a final
    // \0 to terminate.
    if let Some(env) = maybe_env {
        let mut blk = Vec::new();

        // If there are no environment variables to set then signal this by
        // pushing a null.
        if env.is_empty() {
            blk.push(0);
        }

        for (k, v) in env {
            let os_key = OsString::from(k);
            let os_value = OsString::from(v);
            blk.extend(ensure_no_nuls(os_key)?.encode_wide());
            blk.push('=' as u16);
            blk.extend(ensure_no_nuls(os_value)?.encode_wide());
            blk.push(0);
        }
        blk.push(0);
        Ok((Some(blk.as_ptr() as *mut c_void), blk))
    } else {
        Ok((None, Vec::new()))
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
            if GetTokenInformation(token, TokenElevation, Some(elevation_ptr), std::mem::size_of::<TOKEN_ELEVATION>() as u32, &mut size)
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

fn os_to_pcwstr<P: AsRef<OsStr>>(d: P) -> IoResult<(PCWSTR, Vec<u16>)> {
    let d = d.as_ref();
    let d = OsString::from(d);
    let mut d_str: Vec<u16> = ensure_no_nuls(d)?.encode_wide().collect();
    d_str.push(0);
    Ok((PCWSTR(d_str.as_ptr()), d_str))
}

fn pathopt_to_pcwstr<P: AsRef<Path>>(d: Option<P>) -> IoResult<(PCWSTR, Vec<u16>)> {
    match d {
        Some(dir) => {
            let dir = dir.as_ref();
            os_to_pcwstr(dir)
        }
        None => Ok((PCWSTR::null(), Vec::new())),
    }
}

pub fn run_process_as_admin<P1: AsRef<Path>, P2: AsRef<Path>>(
    exe_path: P1,
    args: Vec<String>,
    work_dir: Option<P2>,
    show_window: bool,
) -> IoResult<SafeProcessHandle> {
    let verb = os_to_pcwstr("runas")?;
    let exe = os_to_pcwstr(exe_path.as_ref())?;
    let wrapped_args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();
    let params = make_command_line(None, &wrapped_args, false)?;
    let params = PCWSTR(params.as_ptr());
    let work_dir = pathopt_to_pcwstr(work_dir.as_ref())?;

    let n_show = if show_window {
        windows::Win32::UI::WindowsAndMessaging::SW_NORMAL.0
    } else {
        windows::Win32::UI::WindowsAndMessaging::SW_HIDE.0
    };

    let mut exe_info: SHELLEXECUTEINFOW = SHELLEXECUTEINFOW {
        cbSize: std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,
        fMask: SEE_MASK_NOCLOSEPROCESS,
        lpVerb: verb.0,
        lpFile: exe.0,
        lpParameters: params,
        lpDirectory: work_dir.0,
        nShow: n_show,
        ..Default::default()
    };

    unsafe {
        info!("About to launch [AS ADMIN]: '{:?}' in dir '{:?}' with arguments: {:?}", exe, work_dir, args);
        ShellExecuteExW(&mut exe_info as *mut SHELLEXECUTEINFOW)?;
        let process_id = GetProcessId(exe_info.hProcess);
        let _ = AllowSetForegroundWindow(process_id);
        Ok(SafeProcessHandle { handle: exe_info.hProcess, pid: process_id })
    }
}

pub fn run_process<P1: AsRef<Path>, P2: AsRef<Path>>(
    exe_path: P1,
    args: Vec<String>,
    work_dir: Option<P2>,
    show_window: bool,
    set_env: Option<HashMap<String, String>>,
) -> IoResult<SafeProcessHandle> {
    let exe_path = exe_path.as_ref();
    let exe_path = OsString::from(exe_path);
    let exe_name_ptr = os_to_pcwstr(&exe_path)?;

    let work_dir = work_dir.map(|d| d.as_ref().to_path_buf());

    let wrapped_args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();
    let mut params = make_command_line(Some(&exe_path), &wrapped_args, false)?;
    let params = PWSTR(params.as_mut_ptr());

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

    let envp = make_envp(set_env)?;
    let dirp = pathopt_to_pcwstr(work_dir.as_deref())?;

    let flags = if show_window {
        CREATE_UNICODE_ENVIRONMENT
    } else {
        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT
    };

    unsafe {
        info!("About to launch: '{:?}' in dir '{:?}' with arguments: {:?}", exe_path, work_dir, args);
        CreateProcessW(exe_name_ptr.0, Option::Some(params), None, None, false, flags, envp.0, dirp.0, &si, &mut pi)?;
        let _ = AllowSetForegroundWindow(pi.dwProcessId);
        let _ = CloseHandle(pi.hThread);
    }

    Ok(SafeProcessHandle { handle: pi.hProcess, pid: pi.dwProcessId })
}

fn duration_to_ms(dur: Duration) -> u32 {
    let ms = dur
        .as_secs()
        .checked_mul(1000)
        .and_then(|amt| amt.checked_add((dur.subsec_nanos() / 1_000_000) as u64))
        .expect("failed to convert duration to milliseconds");
    if ms > (u32::max_value() as u64) {
        u32::max_value()
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

pub fn open_process(
    dwdesiredaccess: PROCESS_ACCESS_RIGHTS,
    binherithandle: bool,
    dwprocessid: u32,
) -> windows::core::Result<SafeProcessHandle> {
    let handle = unsafe { OpenProcess(dwdesiredaccess, binherithandle, dwprocessid)? };
    return Ok(SafeProcessHandle { handle, pid: dwprocessid });
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
        return Err(IoError::new(IoErrorKind::Other, format!("NtQueryInformationProcess failed: {:?}", hres)));
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

#[test]
fn test_kill_process() {
    let cmd =
        std::process::Command::new("cmd.exe").arg("/C").arg("ping").arg("8.8.8.8").arg("-t").spawn().expect("failed to start process");

    let pid = cmd.id();

    kill_pid(pid).expect("failed to kill process");
}
