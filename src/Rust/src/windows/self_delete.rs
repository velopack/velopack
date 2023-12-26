use anyhow::Result;
use std::{env, os::windows::process::CommandExt, path::Path, process::Command as Process};

pub fn register_intent_to_delete_self(delay_seconds: usize, current_directory: &Path) -> Result<()> {
    info!("Deleting self...");
    let my_self = env::current_exe()?.to_string_lossy().to_string();
    let command = format!("choice /C Y /N /D Y /T {} & Del \"{}\"", delay_seconds, my_self);
    info!("Running: cmd.exe /C {}", command);

    const CREATE_NO_WINDOW: u32 = 0x08000000;
    Process::new("cmd.exe").arg("/C").raw_arg(command).current_dir(current_directory).creation_flags(CREATE_NO_WINDOW).spawn()?;
    Ok(())
}
