use anyhow::{anyhow, Result};
use std::{
    env,
    ffi::{OsStr, OsString},
    mem::size_of,
    os::windows::ffi::OsStrExt,
    path::Path,
};
use windows::{
    core::{PCWSTR, PWSTR},
    Win32::System::Threading::{CreateProcessW, PROCESS_CREATION_FLAGS, PROCESS_INFORMATION, STARTUPINFOW},
};

pub fn register_intent_to_delete_self(delay_seconds: usize, current_directory: &Path) -> Result<()> {
    info!("Deleting self...");
    let my_self = env::current_exe()?.to_string_lossy().to_string();
    let command = format!("cmd.exe /C choice /C Y /N /D Y /T {} & Del \"{}\"", delay_seconds, my_self);
    info!("Running: {}", command);
    const CREATE_NO_WINDOW: u32 = 0x08000000;
    new_process(&OsString::from(command), false, Some(current_directory), CREATE_NO_WINDOW)?;
    Ok(())
}

fn new_process(command: &OsStr, inherit_handles: bool, current_directory: Option<&Path>, process_creation_flags: u32) -> Result<PROCESS_INFORMATION> {
    let mut startup_info = STARTUPINFOW::default();
    let mut process_info = PROCESS_INFORMATION::default();

    startup_info.cb = size_of::<STARTUPINFOW>() as u32;

    let process_creation_flags = PROCESS_CREATION_FLAGS(process_creation_flags);

    let current_directory_ptr =
        current_directory.map(|path| path.as_os_str().encode_wide().collect::<Vec<_>>()).map(|wide_path| wide_path.as_ptr()).unwrap_or(std::ptr::null_mut());

    let mut command = command.encode_wide().collect::<Vec<_>>();

    let res = unsafe {
        CreateProcessW(
            PCWSTR::null(),
            PWSTR(command.as_mut_ptr()),
            None,
            None,
            inherit_handles,
            process_creation_flags,
            None,
            PCWSTR(current_directory_ptr),
            &startup_info,
            &mut process_info,
        )
    };

    if res.is_ok() {
        Ok(process_info)
    } else {
        Err(anyhow!("Failed to create process."))
    }
}
