//! windows-rs links a few COM functions from combase.dll, which only exists on Windows 8+. build.rs marks
//! combase.dll as delay-loaded, so instead of the OS loader refusing to start the exe on Windows 7, the DLL is
//! only loaded the first time one of its functions is called. If that load fails, the delay-load helper
//! (delayimp.lib) calls `__pfnDliFailureHook2` before giving up; returning ole32.dll there, which exports the
//! same functions on Windows 7, makes the call succeed. On Windows 8+ combase.dll loads and the hook never runs.
//!
//! As far as we know nothing calls these functions on Windows 7 (xdialog uses its TaskDialog backend there, and
//! WebView2 does not support it), so this is a safety net against a crash if that changes.

use std::ffi::c_void;
use windows::core::w;
use windows::Win32::System::LibraryLoader::LoadLibraryW;
use windows::Win32::System::WindowsProgramming::DELAYLOAD_INFO;

/// `dliFailLoadLib` from delayimp.h: the helper could not load the DLL.
const DLI_FAIL_LOAD_LIB: u32 = 3;

/// delayimp.lib reads this symbol; defining it replaces the library's default (no hook).
#[unsafe(no_mangle)]
#[used]
#[allow(non_upper_case_globals)]
static __pfnDliFailureHook2: unsafe extern "system" fn(u32, *const DELAYLOAD_INFO) -> *const c_void = failure_hook;

/// Called from the binaries' startup code: the linker only pulls this module in if something references it.
pub fn ensure_failure_hook_linked() {
    std::hint::black_box(&__pfnDliFailureHook2);
}

unsafe extern "system" fn failure_hook(notify: u32, info: *const DELAYLOAD_INFO) -> *const c_void {
    if notify == DLI_FAIL_LOAD_LIB && (*info).TargetDllName.as_bytes().eq_ignore_ascii_case(b"combase.dll") {
        if let Ok(ole32) = LoadLibraryW(w!("ole32.dll")) {
            return ole32.0;
        }
    }
    std::ptr::null()
}
