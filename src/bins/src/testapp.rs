use anyhow::Result;
use chrono::Local;
use velopack_bins::windows::splash;
use std::{collections::HashMap, fs::OpenOptions, io::Write, path::PathBuf, thread};

fn log_message(message: &str) -> Result<()> {
    // println!("{}", message);

    // let exe_path = std::env::current_exe()?;
    // let binding = PathBuf::from(".");
    // let exe_dir = exe_path.parent().unwrap_or(&binding);
    // let log_path = exe_dir.join("testapp.log");

    // let mut file = OpenOptions::new().create(true).append(true).open(log_path)?;

    // writeln!(file, "[{}] {}", std::process::id(), message)?;

    log::info!("[{}] {}", std::process::id(), message);
    Ok(())
}

fn main() -> Result<()> {
    if let Err(e) = run() {
        let error_msg = format!("Error: {}", e);
        let _ = log_message(&error_msg);
        return Err(e);
    }
    Ok(())
}

fn run() -> Result<()> {
    let args = std::env::args().collect::<Vec<_>>();

    let exe_path = std::env::current_exe()?;
    let binding = PathBuf::from(".");
    let exe_dir = exe_path.parent().unwrap_or(&binding);
    let log_path = exe_dir.join("testapp.log");
    velopack::logging::init_logging("testapp", Some(&log_path), false, true, None);

    // Common paths
    let exe_path = r"D:\Dev\Velopack\velopack\target\debug\testapp.exe";
    let working_dir = r"D:\Dev\Velopack\velopack\target\debug";

    // Common arguments
    let step0_arg = "--step0";
    let step1_arg = "--step1";
    //let step2_arg = "--step2";

    let mut set_env = HashMap::new();
    set_env.insert("TEST_ENV".to_string(), "true".to_string());

    
    if args.iter().any(|arg| arg == step1_arg) {
        log_message("showing progress (launch self FROM elevated process)...")?;
        let tx = splash::show_progress_dialog("Title", "Message");
        let _ = tx.send(splash::MSG_INDEFINITE);
        thread::sleep(std::time::Duration::from_secs(3));
        let _ = tx.send(splash::MSG_CLOSE);
    //} else if args.iter().any(|arg| arg == step2_arg) {
    //    log_message("We have successfully launched from an elevated process!")?;
    } else if args.iter().any(|arg| arg == step0_arg) {
        log_message("About to run step 1 (launch self as admin)...")?;
        velopack::process::run_process_as_admin(exe_path, vec![step1_arg.into()], Some(working_dir), true)?;
    } else if args.len() < 2 {
        // log_message(&format!("Args: {:?}", args))?;
        log_message("About to run step 0 (relaunch self as NOT admin)")?;
        velopack::process::run_process(exe_path, vec![step0_arg.into()], Some(working_dir), true, Some(set_env))?;
    }

    // thread::sleep(std::time::Duration::from_secs(60));

    Ok(())
}
