use windows_sys::Win32::System::LibraryLoader::{SetDefaultDllDirectories, LOAD_LIBRARY_SEARCH_SYSTEM32};
/// This attempts to defend against malicious DLLs that may sit alongside
/// our binary in the user's download folder.
#[cfg(windows)]
pub fn pre_main_sideload_mitigation() {
    // Default to loading delay loaded DLLs from the system directory.
    // For DLLs loaded at load time, this relies on the `delayload` linker flag.
    // This is only necessary prior to Windows 10 RS1. See build.rs for details.
    unsafe {
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    }
}
