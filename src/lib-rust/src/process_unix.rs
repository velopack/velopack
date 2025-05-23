use std::{
    collections::HashMap,
    ffi::{OsStr, OsString},
    io::{Error as IoError, ErrorKind as IoErrorKind, Result as IoResult},
    os::{raw::c_void, windows::ffi::OsStrExt},
    process::{Child, Command},
    time::Duration,
};

use crate::process;

pub fn is_current_process_elevated() -> bool {
    false
}

fn string_to_u16<P: AsRef<str>>(input: P) -> Vec<u16> {
    let input = input.as_ref();
    input.encode_utf16().chain(Some(0)).collect::<Vec<u16>>()
}

pub fn run_process_as_admin(
    exe_path: String,
    args: Vec<String>,
    work_dir: Option<String>,
    show_window: bool,
) -> IoResult<SafeProcessHandle> {

    // let mut cmd = Command::new(exe_path).args(args);

    // if let Some(dir) = work_dir {
    //     cmd.current_dir(dir);
    // }
}

pub fn run_process(
    exe_path: String,
    args: Vec<String>,
    work_dir: Option<String>,
    _show_window: bool,
    set_env: Option<HashMap<String, String>>,
) -> IoResult<Child> {
    let mut cmd = Command::new(exe_path);
    cmd.args(args);
    if let Some(dir) = work_dir {
        cmd.current_dir(dir);
    }
    if let Some(env) = set_env {
        for (key, value) in env {
            cmd.env(key, value);
        }
    }
    cmd.spawn()
}

pub fn wait_for_process_exit_with_timeout(process: Child, dur: Duration) -> IoResult<Option<u32>> {
    let mut status = process.wait_timeout(dur)?;
    if status.is_none() {
        return Err(IoError::new(IoErrorKind::TimedOut, "Process timed out"));
    }
    Ok(status.unwrap().code())
}

pub fn wait_for_pid_to_exit(pid: u32, dur: Duration) -> IoResult<()> {
    info!("Waiting {}ms for process ({}) to exit.", ms_to_wait, pid);
    let mut handle = waitpid_any::WaitHandle::open(pid.try_into()?)?;
    let result = handle.wait_timeout(Duration::from_millis(ms_to_wait as u64))?;
    if result.is_some() {
        info!("Parent process exited.");
        Ok(())
    } else {
        bail!("Parent process timed out.");
    }
}

pub fn wait_for_parent_to_exit(dur: Duration) -> IoResult<()> {
    let id = std::os::unix::process::parent_id();
    info!("Attempting to wait for parent process ({}) to exit.", id);
    if id > 1 {
        wait_for_pid_to_exit(id, ms_to_wait)?;
    }
    Ok(())
}

pub fn kill_process(mut process: Child) -> IoResult<()> {
    process.kill()
}
