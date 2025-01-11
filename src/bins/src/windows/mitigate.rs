use windows::Win32::System::LibraryLoader::LOAD_LIBRARY_SEARCH_SYSTEM32;
use windows::Win32::{Foundation::BOOL, System::LibraryLoader::LOAD_LIBRARY_FLAGS};

#[cfg(target_os = "windows")]
type SetDefaultDllDirectoriesFn = unsafe extern "system" fn(DirectoryFlags: u32) -> BOOL;

#[cfg(target_os = "windows")]
unsafe fn set_default_dll_directories(flags: LOAD_LIBRARY_FLAGS) {
    if let Ok(lib) = libloading::Library::new("kernel32.dll") {
        // Try to get the symbol (function pointer). If it fails, that means we're on an
        // older version of Windows that does not export SetDefaultDllDirectories.
        let func: libloading::Symbol<SetDefaultDllDirectoriesFn> = match lib.get(b"SetDefaultDllDirectories") {
            Ok(s) => s,
            Err(_e) => {
                // Fallback or ignore if not available on older Windows
                warn!("SetDefaultDllDirectories not available on this version of Windows.");
                return;
            }
        };
        if func(flags.0) == false {
            warn!("Failed to set default DLL directories.");
        }
    } else {
        warn!("Failed to load kernel32.dll.");
    }
}

/// This attempts to defend against malicious DLLs that may sit alongside
/// our binary in the user's download folder.
#[cfg(windows)]
pub fn pre_main_sideload_mitigation() {
    // Default to loading delay loaded DLLs from the system directory.
    // For DLLs loaded at load time, this relies on the `delayload` linker flag.
    // This is only necessary prior to Windows 10 RS1. See build.rs for details.

    unsafe {
        set_default_dll_directories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    }
}
