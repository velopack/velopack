use std::{
    collections::HashMap,
    ffi::OsString,
    io::{Error as IoError, ErrorKind as IoErrorKind, Result as IoResult},
    path::Path,
    process::{Child, Command},
    time::Duration,
};

pub fn run_process<P1: AsRef<Path>, P2: AsRef<Path>>(
    exe_path: P1,
    args: Vec<OsString>,
    work_dir: Option<P2>,
    _show_window: bool,
    set_env: Option<HashMap<String, String>>,
) -> IoResult<Child> {
    let exe_path = exe_path.as_ref();
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

pub fn wait_for_process_exit_with_timeout(process: &mut Child, dur: Option<Duration>) -> IoResult<WaitResult> {
    if let Some(dur) = dur {
        let status = wait_timeout::ChildExt::wait_timeout(process, dur)?;
        match status {
            Some(status) => Ok(WaitResult::ExitCode(status.code().unwrap_or(0) as u32)),
            None => Ok(WaitResult::WaitTimeout),
        }
    } else {
        let code = process.wait()?;
        Ok(WaitResult::ExitCode(code.code().unwrap_or(0) as u32))
    }
}

pub fn wait_for_pid_to_exit(pid: u32, dur: Option<Duration>) -> IoResult<WaitResult> {
    info!("Waiting {:?} for process ({}) to exit.", dur, pid);
    let mut handle = waitpid_any::WaitHandle::open(pid as i32)?;
    if let Some(dur) = dur {
        let result = handle.wait_timeout(dur)?;
        if result.is_some() {
            info!("Parent process exited.");
            Ok(WaitResult::ExitCode(0))
        } else {
            Err(IoError::new(IoErrorKind::TimedOut, "Parent process timed out."))
        }
    } else {
        handle.wait()?;
        Ok(WaitResult::ExitCode(0))
    }
}

pub fn wait_for_parent_to_exit(dur: Option<Duration>) -> IoResult<WaitResult> {
    let id = std::os::unix::process::parent_id();
    info!("Attempting to wait for parent process ({}) to exit.", id);
    if id > 1 {
        return Ok(wait_for_pid_to_exit(id, dur)?);
    }
    Ok(WaitResult::NoWaitRequired)
}

pub fn kill_process(mut process: Child) -> IoResult<()> {
    process.kill()
}
