use anyhow::Result;
use std::{
    os::windows::process::CommandExt,
    path::Path,
    process::{Command as Process, Stdio},
};

pub fn register_intent_to_delete_self(delay_seconds: usize, root_dir: &Path) -> Result<()> {
    info!("Scheduling removal of install directory...");
    let root_str = root_dir.to_string_lossy().to_string();
    let command = format!("choice /C Y /N /D Y /T {} & rmdir /s /q \"{}\"", delay_seconds, root_str);
    info!("Running: cmd.exe /C {}", command);

    let parent_dir = root_dir.parent().unwrap_or(root_dir);

    // Don't inherit our std handles: on Windows 7 they can be console pseudo-handles, which fail
    // CreateProcess with ERROR_INVALID_HANDLE (e.g. when run from a console with redirected output).
    const CREATE_NO_WINDOW: u32 = 0x08000000;
    Process::new("cmd.exe")
        .arg("/C")
        .raw_arg(command)
        .current_dir(parent_dir)
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;
    Ok(())
}
