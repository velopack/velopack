use std::{
    ffi::{OsStr, OsString},
    os::windows::ffi::OsStrExt,
};

use anyhow::{bail, Result};
use windows::{
    core::PCWSTR,
    Win32::{
        Foundation::HANDLE,
        System::Threading::CreateProcessW,
        UI::Shell::{ShellExecuteExW, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW},
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

    Ok(cmd)
}

pub fn run_process_as_admin(exe_path: String, args: Vec<String>, work_dir: Option<String>) -> Result<HANDLE> {
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
    }
    Ok(exe_info.hProcess)
}

// pub fn run_process_no_window(exe_path: String, args: Vec<String>, work_dir: Option<String>) -> Result<HANDLE> {
//     CreateProcessW(
//         lpapplicationname,
//         lpcommandline,
//         lpprocessattributes,
//         lpthreadattributes,
//         binherithandles,
//         dwcreationflags,
//         lpenvironment,
//         lpcurrentdirectory,
//         lpstartupinfo,
//         lpprocessinformation,
//     )
// }
