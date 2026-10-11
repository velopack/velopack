//! Lets the binaries load on Windows 7, although windows-rs links some COM functions from combase.dll.
//!
//! combase.dll (Windows 8+) is delay-loaded (see build.rs), so it is only loaded when one of its functions is
//! first called, at which point the delay-load helper asks this failure hook for a replacement module instead
//! of the loader refusing to start the process. On Windows 8+ it loads normally and the hook never runs.

use std::ffi::{c_char, c_void, CStr};
use windows::core::w;
use windows::Win32::System::LibraryLoader::LoadLibraryW;

// dliNotify value from delayimp.h
const DLI_FAIL_LOAD_LIBRARY: u32 = 3;

// DelayLoadInfo from delayimp.h; only the fields up to dll_name are read, the rest keep the layout honest
#[repr(C)]
#[allow(dead_code)]
struct DelayLoadInfo {
    cb: u32,
    pidd: *const c_void,
    ppfn: *mut *const c_void,
    dll_name: *const c_char,
    import_by_name: i32,
    // union of the procedure name (LPCSTR) and its ordinal (DWORD)
    name_or_ordinal: *const c_char,
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
    // windows-rs links CoTaskMemAlloc and friends from combase.dll (Windows 8+);
    // Windows 7 exports the same functions from ole32.dll, a KnownDLL.
    if notify == DLI_FAIL_LOAD_LIBRARY && CStr::from_ptr(info.dll_name).to_bytes().eq_ignore_ascii_case(b"combase.dll") {
        if let Ok(module) = LoadLibraryW(w!("ole32.dll")) {
            return module.0 as *const c_void;
        }
    }
    std::ptr::null()
}
