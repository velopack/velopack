//! Platform-specific stuff

use std::fs::Metadata;

#[cfg(target_os = "windows")]
/// OS - Windows, id 11 per Info-Zip spec
/// Specification version 6.2
pub(crate) const VERSION_MADE_BY: u16 = (11 << 8) + 62;

#[cfg(target_os = "macos")]
/// OS - MacOS darwin, id 19
/// Specification version 6.2
pub(crate) const VERSION_MADE_BY: u16 = (19 << 8) + 62;

#[cfg(not(any(target_os = "windows", target_os = "macos")))]
// Fallback
/// OS - Unix assumed, id 3
/// Specification version 6.2
pub(crate) const VERSION_MADE_BY: u16 = (3 << 8) + 62;

#[allow(dead_code)]
pub(crate) const DEFAULT_UNIX_FILE_ATTRS: u16 = 0o100644;
#[allow(dead_code)]
pub(crate) const DEFAULT_UNIX_DIR_ATTRS: u16 = 0o040755;

#[cfg(target_os = "windows")]
pub(crate) const DEFAULT_WINDOWS_FILE_ATTRS: u16 = 128;
#[cfg(target_os = "windows")]
pub(crate) const DEFAULT_WINDOWS_DIR_ATTRS: u16 = 16;

#[inline]
#[allow(dead_code)]
const fn convert_attrs(attrs: u32) -> u16 {
    attrs as u16
}

pub(crate) fn attributes_from_fs(metadata: &Metadata) -> u16 {
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::fs::MetadataExt;
        return convert_attrs(metadata.file_attributes());
    }

    #[cfg(target_os = "linux")]
    {
        use std::os::linux::fs::MetadataExt;
        return convert_attrs(metadata.st_mode());
    }

    #[cfg(target_os = "macos")]
    {
        use std::os::darwin::fs::MetadataExt;
        return convert_attrs(metadata.st_mode());
    }

    #[cfg(all(unix, not(target_os = "linux"), not(target_os = "macos")))]
    {
        use std::os::unix::fs::PermissionsExt;
        return convert_attrs(metadata.permissions().mode());
    }

    #[cfg(not(any(target_os = "windows", target_os = "linux", target_os = "macos", unix)))]
    {
        if metadata.is_dir() {
            return DEFAULT_UNIX_DIR_ATTRS;
        } else {
            return DEFAULT_UNIX_FILE_ATTRS;
        }
    }
}

#[cfg(target_os = "windows")]
pub(crate) const fn default_file_attrs() -> u16 {
    DEFAULT_WINDOWS_FILE_ATTRS
}

#[cfg(not(windows))]
pub(crate) const fn default_file_attrs() -> u16 {
    DEFAULT_UNIX_FILE_ATTRS
}

#[cfg(target_os = "windows")]
pub(crate) const fn default_dir_attrs() -> u16 {
    DEFAULT_WINDOWS_DIR_ATTRS
}

#[cfg(any(target_os = "linux", unix))]
#[cfg(not(target_os = "windows"))]
pub(crate) const fn default_dir_attrs() -> u16 {
    DEFAULT_UNIX_DIR_ATTRS
}

#[cfg(not(any(target_os = "windows", target_os = "linux", unix)))]
pub(crate) const fn default_dir_attrs() -> u16 {
    0
}
