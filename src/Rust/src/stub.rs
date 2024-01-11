#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

mod logging;

#[macro_use]
extern crate log;

use std::{
    env,
    os::windows::process::CommandExt,
    process::{Command as Process, ExitCode},
};

fn main() -> ExitCode {
    let my_path = std::env::current_exe().unwrap();
    let my_name = my_path.file_name().unwrap().to_string_lossy();

    let default_log_file = {
        let mut my_dir = env::current_exe().unwrap();
        my_dir.pop();
        my_dir.join("Velopack.log")
    };

    let _ = logging::setup_logging(Some(&default_log_file), false, false, false);

    let mut update_exe = my_path.clone();
    update_exe.pop();
    update_exe.push("Update.exe");

    if !update_exe.exists() {
        error!("Update.exe not found at {:?}", update_exe);
        return ExitCode::FAILURE;
    }

    let mut args: Vec<String> = std::env::args().skip(1).collect();
    args.insert(0, "start".to_owned());
    args.insert(1, my_name.to_string());
    args.insert(2, "--".to_owned());

    info!("Stub {} about to start Update.exe ({}) with args: {:?}", my_name, update_exe.to_string_lossy(), args);

    const CREATE_NO_WINDOW: u32 = 0x08000000;
    if let Err(e) = Process::new(update_exe).args(args).creation_flags(CREATE_NO_WINDOW).spawn() {
        error!("Stub failed to start Update.exe: {}", e);
        return ExitCode::FAILURE;
    }

    ExitCode::SUCCESS
}
