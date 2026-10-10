//! Lets the binaries load on Windows 7, although some dependencies import APIs it does not have.
//!
//! The DLLs below are delay-loaded (see build.rs), so a missing DLL or function is only resolved when first
//! called, at which point the delay-load helper asks this failure hook for a replacement instead of the
//! loader refusing to start the process. On Windows 8+ everything resolves normally and the hook never runs.

use std::ffi::{c_char, c_void, CStr};
use windows::core::w;
use windows::Win32::System::LibraryLoader::LoadLibraryW;

// dliNotify values from delayimp.h
const DLI_FAIL_LOAD_LIBRARY: u32 = 3;
const DLI_FAIL_GET_PROC: u32 = 4;

#[repr(C)]
struct DelayLoadProc {
    import_by_name: i32,
    // union of the procedure name (LPCSTR) and its ordinal (DWORD)
    name_or_ordinal: *const c_char,
}

#[repr(C)]
struct DelayLoadInfo {
    cb: u32,
    pidd: *const c_void,
    ppfn: *mut *const c_void,
    dll_name: *const c_char,
    dlp: DelayLoadProc,
    hmod_cur: *mut c_void,
    pfn_cur: *const c_void,
    last_error: u32,
}

type DelayLoadHook = unsafe extern "system" fn(u32, *const DelayLoadInfo) -> *const c_void;

/// Read by the delay-load helper in delayimp.lib; defining it replaces the library's null default.
#[unsafe(no_mangle)]
#[used]
#[allow(non_upper_case_globals)]
static __pfnDliFailureHook2: DelayLoadHook = delay_load_failure_hook;

/// Referenced from the binaries' startup code, so the linker cannot drop the hook.
pub fn ensure_failure_hook_linked() {
    std::hint::black_box(&__pfnDliFailureHook2);
}

unsafe extern "system" fn delay_load_failure_hook(notify: u32, info: *const DelayLoadInfo) -> *const c_void {
    let info = &*info;
    match notify {
        DLI_FAIL_LOAD_LIBRARY => {
            // windows-rs links CoTaskMemAlloc and friends from combase.dll (Windows 8+);
            // Windows 7 exports the same functions from ole32.dll, a KnownDLL.
            if CStr::from_ptr(info.dll_name).to_bytes().eq_ignore_ascii_case(b"combase.dll") {
                if let Ok(module) = LoadLibraryW(w!("ole32.dll")) {
                    return module.0 as *const c_void;
                }
            }
            std::ptr::null()
        }
        DLI_FAIL_GET_PROC if info.dlp.import_by_name != 0 => match CStr::from_ptr(info.dlp.name_or_ordinal).to_bytes() {
            // xdialog sets per-monitor DPI awareness per thread (Windows 10 1607+) and treats a null
            // previous context as "unsupported".
            b"SetThreadDpiAwarenessContext" => set_thread_dpi_awareness_context_unsupported as *const c_void,
            _ => std::ptr::null(),
        },
        _ => std::ptr::null(),
    }
}

unsafe extern "system" fn set_thread_dpi_awareness_context_unsupported(_context: isize) -> isize {
    0
}
