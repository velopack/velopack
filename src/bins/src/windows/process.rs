use std::{
    collections::HashMap,
    ffi::{OsStr, OsString},
    os::{raw::c_void, windows::ffi::OsStrExt},
    time::Duration,
};

use anyhow::{bail, Result};
use windows::{
    core::{PCWSTR, PWSTR},
    Win32::{
        Foundation::{CloseHandle, HANDLE, WAIT_OBJECT_0, WAIT_TIMEOUT},
        Security::{GetTokenInformation, TokenElevation, TOKEN_ELEVATION},
        System::Threading::{
            CreateProcessW, GetCurrentProcess, GetExitCodeProcess, GetProcessId, OpenProcessToken, WaitForSingleObject, CREATE_NO_WINDOW,
            PROCESS_CREATION_FLAGS, STARTUPINFOW, STARTUPINFOW_FLAGS,
        },
        UI::{
            Shell::{ShellExecuteExW, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW},
            WindowsAndMessaging::AllowSetForegroundWindow,
        },
    },
};

use super::strings::string_to_u16;

enum Arg {
    /// Add quotes (if needed)
    Regular(OsString),
    /// Append raw string without quoting
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

fn ensure_no_nuls<T: AsRef<OsStr>>(str: T) -> Result<T> {
    if str.as_ref().encode_wide().any(|b| b == 0) {
        bail!("nul byte found in provided data");
    } else {
        Ok(str)
    }
}

pub fn append_arg(cmd: &mut Vec<u16>, arg: &Arg, force_quotes: bool) -> Result<()> {
    let (arg, quote) = match arg {
        Arg::Regular(arg) => (arg, if force_quotes { Quote::Always } else { Quote::Auto }),
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

fn make_command_line(argv0: Option<&OsStr>, args: &[Arg], force_quotes: bool) -> Result<Vec<u16>> {
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

fn make_envp(maybe_env: Option<HashMap<String, String>>) -> Result<(Option<*const c_void>, Vec<u16>)> {
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

fn make_dirp(d: Option<String>) -> Result<(PCWSTR, Vec<u16>)> {
    match d {
        Some(dir) => {
            let dir = OsString::from(dir);
            let mut dir_str: Vec<u16> = ensure_no_nuls(dir)?.encode_wide().collect();
            dir_str.push(0);
            Ok((PCWSTR(dir_str.as_ptr()), dir_str))
        }
        None => Ok((PCWSTR::null(), Vec::new())),
    }
}

pub fn is_process_elevated() -> bool {
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
                CloseHandle(token);
                return elevation.TokenIsElevated != 0;
            }
        }
    }

    // Clean up the token handle
    if !token.is_invalid() {
        unsafe { CloseHandle(token) };
    }

    false
}

struct SafeProcessHandle(HANDLE);

impl Drop for SafeProcessHandle {
    fn drop(&mut self) {
        if !self.0.is_invalid() {
            let _ = unsafe { CloseHandle(self.0) };
        }
    }
}

impl SafeProcessHandle {
    pub fn handle(&self) -> HANDLE {
        self.0
    }
}

// impl Into<u32> for SafeProcessHandle {
//     fn into(self) -> u32 {
//         self.1
//     }
// }

pub fn run_process_as_admin(exe_path: String, args: Vec<String>, work_dir: Option<String>) -> Result<SafeProcessHandle> {
    let verb = string_to_u16("runas");
    let verb = PCWSTR(verb.as_ptr());

    let exe = string_to_u16(exe_path);
    let exe = PCWSTR(exe.as_ptr());

    let args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();

    let params = make_command_line(None, &args, false)?;
    let params = PCWSTR(params.as_ptr());

    let work_dir = work_dir.map(|w| string_to_u16(w)).map(|f| PCWSTR(f.as_ptr())).unwrap_or(PCWSTR::null());

    let mut exe_info: SHELLEXECUTEINFOW = SHELLEXECUTEINFOW {
        cbSize: std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,
        fMask: SEE_MASK_NOCLOSEPROCESS,
        lpVerb: verb,
        lpFile: exe,
        lpParameters: params,
        lpDirectory: work_dir,
        nShow: windows::Win32::UI::WindowsAndMessaging::SW_NORMAL.0,
        ..Default::default()
    };

    unsafe {
        ShellExecuteExW(&mut exe_info as *mut SHELLEXECUTEINFOW)?;
        let process_id = GetProcessId(exe_info.hProcess);
        let _ = AllowSetForegroundWindow(process_id);
        Ok(SafeProcessHandle(exe_info.hProcess))
    }
}

pub fn run_process(
    exe_path: String,
    args: Vec<String>,
    work_dir: Option<String>,
    set_env: Option<HashMap<String, String>>,
    show_window: bool,
) -> Result<SafeProcessHandle> {
    let exe_path = OsString::from(exe_path);
    let exe_name = PCWSTR(exe_path.encode_wide().chain(Some(0)).collect::<Vec<_>>().as_mut_ptr());

    let args: Vec<Arg> = args.iter().map(|a| Arg::Regular(a.into())).collect();
    let mut params = make_command_line(Some(&exe_path), &args, false)?;
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
    let dirp = make_dirp(work_dir)?;

    let flags = if show_window { PROCESS_CREATION_FLAGS(0) } else { CREATE_NO_WINDOW };

    unsafe {
        CreateProcessW(exe_name, params, None, None, false, flags, envp.0, dirp.0, &si, &mut pi)?;
        let _ = AllowSetForegroundWindow(pi.dwProcessId);
        let _ = CloseHandle(pi.hThread);
    }

    Ok(SafeProcessHandle(pi.hProcess))
}

pub fn wait_process_timeout(process: HANDLE, dur: Duration) -> std::io::Result<Option<u32>> {
    let ms = dur
        .as_secs()
        .checked_mul(1000)
        .and_then(|amt| amt.checked_add((dur.subsec_nanos() / 1_000_000) as u64))
        .expect("failed to convert duration to milliseconds");
    let ms: u32 = if ms > (u32::max_value() as u64) { u32::max_value() } else { ms as u32 };
    unsafe {
        match WaitForSingleObject(process, ms) {
            WAIT_OBJECT_0 => {}
            WAIT_TIMEOUT => return Ok(None),
            _ => return Err(std::io::Error::last_os_error()),
        }

        let mut exit_code = 0;
        GetExitCodeProcess(process, &mut exit_code)?;

        Ok(Some(exit_code))
    }
}

pub fn kill_process(process: HANDLE) -> std::io::Result<()> {
    unsafe {
        let _ = CloseHandle(process);
    }
    Ok(())
}
