use std::io::Write;
use std::path::Path;
use std::sync::Mutex;
use velopack::chunks::{assemble, AssembleContext, AssembleError};
use velopack::locator::{auto_locate_app_manifest, LocationContext};

/// Writes one protocol line (`VPK:PROGRESS n`, `VPK:MISSING n`, `VPK:ERROR msg`) to stdout, holding the
/// stdout lock so the terminal logger cannot interleave with it, and flushes it.
fn print_protocol(line: &str) {
    let mut out = std::io::stdout().lock();
    let _ = writeln!(out, "{}", line);
    let _ = out.flush();
}

fn single_line(msg: &str) -> String {
    msg.lines().map(|l| l.trim()).filter(|l| !l.is_empty()).collect::<Vec<_>>().join(" ")
}

/// Runs `on_exit` on a background thread as soon as process `pid` has exited (or cannot be waited on).
pub(crate) fn spawn_parent_watchdog(pid: u32, on_exit: impl FnOnce() + Send + 'static) {
    std::thread::spawn(move || {
        let result = velopack::process::wait_for_pid_to_exit(pid, None);
        debug!("Watched process {} has exited ({:?})", pid, result);
        on_exit();
    });
}

/// The `assemble --staged <DIR> --parentPid <PID>` command (§7.1). Builds the staged tree from local
/// files and the chunk blob and returns the process exit code: 0 success, 2 invalid input, 3 chunks
/// missing (`missing.bin` written), 4 IO error, 1 anything else. Takes no lock: the caller holds it
/// and this process exits as soon as the parent is gone.
pub fn assemble_cli(context: LocationContext, staged: Option<&Path>, parent_pid: Option<u32>) -> i32 {
    let staged = match staged {
        Some(s) => s,
        None => {
            print_protocol("VPK:ERROR --staged is required");
            return 2;
        }
    };
    let parent_pid = match parent_pid {
        Some(p) => p,
        None => {
            print_protocol("VPK:ERROR --parentPid is required");
            return 2;
        }
    };
    if !cfg!(windows) {
        print_protocol("VPK:ERROR assemble is only supported on Windows");
        return 2;
    }

    // Die with the parent: nothing may keep writing into a staged directory once the lock holder is gone.
    // The parent is checked synchronously first, so a parent that is already gone never starts assembly.
    match velopack::process::wait_for_pid_to_exit(parent_pid, Some(std::time::Duration::ZERO)) {
        Ok(velopack::process::WaitResult::WaitTimeout) => {}
        other => {
            print_protocol(&format!("VPK:ERROR parent process {} is not running ({:?})", parent_pid, other));
            return 1;
        }
    }
    spawn_parent_watchdog(parent_pid, move || {
        warn!("Parent process {} is gone, exiting.", parent_pid);
        std::process::exit(1);
    });

    let locator = match auto_locate_app_manifest(context) {
        Ok(l) => l,
        Err(e) => {
            print_protocol(&format!("VPK:ERROR {}", single_line(&e.to_string())));
            return 2;
        }
    };
    // The running updater is, by construction, the one next to `current/sq.chunks`.
    let updater_local_source = std::env::current_exe().unwrap_or_else(|_| locator.get_update_path());
    let ctx = AssembleContext {
        staged_dir: staged.to_path_buf(),
        current_dir: locator.get_current_bin_dir(),
        updater_local_source,
        root_dir: locator.get_root_dir(),
    };
    info!("Assembling staged update in {:?}", ctx.staged_dir);

    let last = Mutex::new(-1i16);
    let progress = move |p: i16| {
        let mut last = last.lock().unwrap();
        if p > *last {
            *last = p;
            print_protocol(&format!("VPK:PROGRESS {}", p));
        }
    };

    match assemble(&ctx, &progress) {
        Ok(()) => {
            info!("Staged update assembled successfully.");
            0
        }
        Err(AssembleError::Missing(n)) => {
            info!("Assembly needs {} more chunks.", n);
            print_protocol(&format!("VPK:MISSING {}", n));
            print_protocol(&format!("VPK:ERROR {} chunks are missing", n));
            3
        }
        Err(e) => {
            error!("Assembly failed: {}", e);
            print_protocol(&format!("VPK:ERROR {}", single_line(&e.to_string())));
            e.exit_code()
        }
    }
}

#[cfg(all(test, windows))]
mod tests {
    use super::*;
    use std::time::{Duration, Instant};

    #[test]
    fn watchdog_fires_when_the_parent_exits_mid_assembly() {
        // a parent that lives about 2 seconds
        let mut parent = std::process::Command::new("cmd")
            .args(["/c", "ping -n 3 127.0.0.1 >nul"])
            .spawn()
            .unwrap();
        let (tx, rx) = std::sync::mpsc::channel();
        let started = Instant::now();
        spawn_parent_watchdog(parent.id(), move || {
            let _ = tx.send(Instant::now());
        });
        assert!(rx.recv_timeout(Duration::from_millis(500)).is_err(), "fired while the parent was alive");
        let fired = rx.recv_timeout(Duration::from_secs(15)).expect("fired after the parent exited");
        let _ = parent.wait();
        assert!(
            fired.duration_since(started) >= Duration::from_secs(1),
            "fired after {:?}",
            fired.duration_since(started)
        );
    }
}
