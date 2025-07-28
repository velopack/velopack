use anyhow::{bail, Result};
use std::path::PathBuf;
use velopack::wide_strings::wide_to_os_string;
use windows::{
    core::GUID,
    Win32::UI::Shell::{
        FOLDERID_Desktop, FOLDERID_Downloads, FOLDERID_LocalAppData, FOLDERID_Profile, FOLDERID_ProgramFilesX64, FOLDERID_ProgramFilesX86,
        FOLDERID_RoamingAppData, FOLDERID_StartMenu, FOLDERID_Startup, SHGetKnownFolderPath,
    },
};

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

pub fn get_local_app_data() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_LocalAppData)
}

pub fn get_roaming_app_data() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_RoamingAppData)
}

pub fn get_user_desktop() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_Desktop)
}

pub fn get_user_profile() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_Profile)
}

pub fn get_start_menu() -> Result<PathBuf> {
    let start_menu = get_known_folder(&FOLDERID_StartMenu)?;
    let programs_path = start_menu.join("Programs");
    Ok(programs_path)
}

pub fn get_startup() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_Startup)
}

pub fn get_downloads() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_Downloads)
}

pub fn get_program_files_x64() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_ProgramFilesX64)
}

pub fn get_program_files_x86() -> Result<PathBuf> {
    get_known_folder(&FOLDERID_ProgramFilesX86)
}

pub fn get_user_pinned() -> Result<PathBuf> {
    let pinned_str = get_roaming_app_data()?;
    let pinned_path = pinned_str.join("Microsoft").join("Internet Explorer").join("Quick Launch").join("User Pinned");
    Ok(pinned_path)
}
