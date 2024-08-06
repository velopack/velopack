use anyhow::Result;
use std::path::Path;
use windows::{
    core::GUID,
    Win32::UI::Shell::{
        FOLDERID_Desktop, FOLDERID_Downloads, FOLDERID_LocalAppData, FOLDERID_Profile, FOLDERID_ProgramFilesX64, FOLDERID_ProgramFilesX86,
        FOLDERID_RoamingAppData, FOLDERID_StartMenu, FOLDERID_Startup, SHGetKnownFolderPath,
    },
};

fn get_known_folder(rfid: *const GUID) -> Result<String> {
    unsafe {
        let flag = windows::Win32::UI::Shell::KNOWN_FOLDER_FLAG(0);
        let result = SHGetKnownFolderPath(rfid, flag, None)?;
        super::strings::pwstr_to_string(result)
    }
}

pub fn get_local_app_data() -> Result<String> {
    get_known_folder(&FOLDERID_LocalAppData)
}

pub fn get_roaming_app_data() -> Result<String> {
    get_known_folder(&FOLDERID_RoamingAppData)
}

pub fn get_user_desktop() -> Result<String> {
    get_known_folder(&FOLDERID_Desktop)
}

pub fn get_user_profile() -> Result<String> {
    get_known_folder(&FOLDERID_Profile)
}

pub fn get_start_menu() -> Result<String> {
    let start_menu = get_known_folder(&FOLDERID_StartMenu)?;
    let programs_path = Path::new(&start_menu).join("Programs");
    Ok(programs_path.to_string_lossy().to_string())
}

pub fn get_startup() -> Result<String> {
    get_known_folder(&FOLDERID_Startup)
}

pub fn get_downloads() -> Result<String> {
    get_known_folder(&FOLDERID_Downloads)
}

pub fn get_program_files_x64() -> Result<String> {
    get_known_folder(&FOLDERID_ProgramFilesX64)
}

pub fn get_program_files_x86() -> Result<String> {
    get_known_folder(&FOLDERID_ProgramFilesX86)
}

pub fn get_user_pinned() -> Result<String> {
    let pinned_str = get_roaming_app_data()?;
    let pinned_path = Path::new(&pinned_str).join("Microsoft\\Internet Explorer\\Quick Launch\\User Pinned");
    Ok(pinned_path.to_string_lossy().to_string())
}
