use std::ffi::c_uint;
use windows::Win32::{Foundation::ERROR_SUCCESS, Storage::FileSystem::GetDriveTypeW, System::ApplicationInstallationAndServicing::MSIHANDLE};

use crate::msi::*;

const DRIVE_UNKNOWN: u32 = 0;
const DRIVE_REMOVABLE: u32 = 2;
const DRIVE_REMOTE: u32 = 4;
const DRIVE_CDROM: u32 = 5;
const DRIVE_RAMDISK: u32 = 6;

pub fn validate_path(h_install: MSIHANDLE) -> c_uint {
    let path = msi_get_property(h_install, "WIXUI_INSTALLDIR").unwrap_or_default();

    let valid = if path.is_empty() || path.starts_with("\\\\") {
        false
    } else {
        !is_invalid_drive(&path)
    };

    msi_set_property_string(h_install, "WIXUI_INSTALLDIR_VALID", if valid { "1" } else { "0" });
    ERROR_SUCCESS.0
}

fn is_invalid_drive(path: &str) -> bool {
    let root = match path.find('\\') {
        Some(idx) => format!("{}\\", &path[..idx]),
        None => {
            if path.len() >= 2 && path.as_bytes()[1] == b':' {
                format!("{}\\", path)
            } else {
                return true;
            }
        }
    };

    let wide: Vec<u16> = root.encode_utf16().chain(std::iter::once(0)).collect();
    let drive_type = unsafe { GetDriveTypeW(windows::core::PCWSTR(wide.as_ptr())) };
    matches!(drive_type, DRIVE_UNKNOWN | DRIVE_REMOVABLE | DRIVE_REMOTE | DRIVE_CDROM | DRIVE_RAMDISK)
}
