use anyhow::{bail, Result};
use std::path::PathBuf;
use crate::wide_strings::wide_to_os_string;
use windows::{
    core::GUID,
    Win32::UI::Shell::{
        FOLDERID_LocalAppData, SHGetKnownFolderPath
    },
};

#[cfg(windows)]
fn get_known_folder(rfid: *const GUID) -> Result<PathBuf> {
    unsafe {
        let flag = windows::Win32::UI::Shell::KNOWN_FOLDER_FLAG(0);
        let result = SHGetKnownFolderPath(rfid, flag, None)?;
        if result.is_null() {
            bail!("Failed to get known folder path (SHGetKnownFolderPath returned null)");
        }

        let str = wide_to_os_string(result);
        let path = PathBuf::from(str);
        Ok(path)
    }
}

#[cfg(windows)]
pub fn get_local_app_data() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_LocalAppData)
}



