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
        return wait_for_pid_to_exit(id, dur);
    }
    Ok(WaitResult::NoWaitRequired)
}

pub fn kill_process(mut process: Child) -> IoResult<()> {
    process.kill()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::OsString;
    use std::path::{Path, PathBuf};
    use std::time::Duration;

    // ===== WaitResult tests =====

    #[test]
    fn test_wait_result_code() {
        assert_eq!(WaitResult::ExitCode(42).code(), Some(42));
        assert_eq!(WaitResult::ExitCode(0).code(), Some(0));
        assert_eq!(WaitResult::WaitTimeout.code(), None);
        assert_eq!(WaitResult::NoWaitRequired.code(), None);
    }

    // ===== run_process tests =====

    #[test]
    fn test_run_process_with_all_options() {
        let temp_dir = tempfile::tempdir().unwrap();
        let mut env = std::collections::HashMap::new();
        env.insert("MY_VAR".to_string(), "value".to_string());

        let mut child = run_process(
            "/bin/sh",
            vec![OsString::from("-c"), OsString::from("exit 0")],
            Some(temp_dir.path()),
            true,
            Some(env),
        )
        .expect("failed to run process");

        let status = child.wait().expect("failed to wait");
        assert!(status.success());
    }

    #[test]
    fn test_run_process_nonexistent_exe() {
        let result = run_process("/nonexistent/binary", vec![], None::<PathBuf>, false, None);
        assert!(result.is_err());
    }

    #[test]
    fn test_run_process_nonexistent_work_dir() {
        let result = run_process("/bin/echo", vec![], Some(Path::new("/nonexistent/dir")), false, None);
        assert!(result.is_err());
    }

    // ===== kill_process =====

    #[test]
    fn test_kill_process_running() {
        let child = run_process("/bin/sleep", vec![OsString::from("60")], None::<PathBuf>, false, None).expect("failed to run process");
        kill_process(child).expect("failed to kill process");
    }

    // ===== wait_for_process_exit_with_timeout =====

    #[test]
    fn test_wait_for_process_exit_no_timeout() {
        let mut child = run_process(
            "/bin/sh",
            vec![OsString::from("-c"), OsString::from("exit 0")],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();
        let result = wait_for_process_exit_with_timeout(&mut child, None).unwrap();
        assert_eq!(result.code(), Some(0));
    }

    #[test]
    fn test_wait_for_process_exit_timeout_expires() {
        let mut child = run_process("/bin/sleep", vec![OsString::from("60")], None::<PathBuf>, false, None).unwrap();
        let result = wait_for_process_exit_with_timeout(&mut child, Some(Duration::from_millis(100))).unwrap();
        assert!(matches!(result, WaitResult::WaitTimeout));
        let _ = child.kill();
        let _ = child.wait();
    }

    #[test]
    fn test_wait_for_process_exit_nonzero_code() {
        let mut child = run_process(
            "/bin/sh",
            vec![OsString::from("-c"), OsString::from("exit 42")],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();
        let result = wait_for_process_exit_with_timeout(&mut child, Some(Duration::from_secs(10))).unwrap();
        assert_eq!(result.code(), Some(42));
    }

    // ===== wait_for_pid_to_exit =====

    #[test]
    fn test_wait_for_pid_to_exit_success() {
        let child = run_process(
            "/bin/sh",
            vec![OsString::from("-c"), OsString::from("sleep 1 && exit 0")],
            None::<PathBuf>,
            false,
            None,
        )
        .unwrap();
        let pid = child.id();
        let result = wait_for_pid_to_exit(pid, Some(Duration::from_secs(30))).unwrap();
        assert_eq!(result.code(), Some(0));
    }

    #[test]
    fn test_wait_for_pid_to_exit_timeout() {
        let child = run_process("/bin/sleep", vec![OsString::from("60")], None::<PathBuf>, false, None).unwrap();
        let pid = child.id();
        let result = wait_for_pid_to_exit(pid, Some(Duration::from_millis(100)));
        assert!(result.is_err());
        let _ = kill_process(child);
    }

    // ===== wait_for_parent_to_exit =====

    #[test]
    fn test_wait_for_parent_to_exit_smoke() {
        // Just verify it doesn't panic; parent is still alive so expect timeout or NoWaitRequired
        let _ = wait_for_parent_to_exit(Some(Duration::from_millis(100)));
    }
}
