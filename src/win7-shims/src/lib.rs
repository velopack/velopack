//! Windows 7 support for our own final artifacts built on the regular `*-pc-windows-msvc` targets: the exes
//! (update/setup/stub/testapp) and velopack_wix.dll, which our MSI loads. Never link this into a library that end users
//! link or load themselves (velopack_libc, velopack_nodeffi).
//!
//! On those targets std assumes Windows 10 and statically imports five functions Windows 7 does not have:
//! `WaitOnAddress`/`WakeByAddressSingle`/`WakeByAddressAll` (api-ms-win-core-synch-l1-2-0.dll, used by every std
//! Mutex/RwLock/Condvar/park), `ProcessPrng` (bcryptprimitives.dll) and `GetSystemTimePreciseAsFileTime` (kernel32.dll).
//! windows-core also links `CoTaskMemAlloc`/`CoTaskMemFree`/`CoCreateFreeThreadedMarshaler` from combase.dll, which only
//! exists since Windows 8 (Windows 7 has them in ole32.dll). With any of them missing the loader refuses to start the
//! exe or load the dll at all.
//!
//! [`link_win7_shims!`] defines the `__imp_` pointers those imports are called through. The linker resolves a symbol
//! defined in an object file before searching import libraries, so the imports disappear from the PE and every call
//! goes through the shims below instead. Each shim resolves the real function on first use, so Windows 8+ behaves
//! exactly as before, and falls back to a Windows 7 implementation (or ole32.dll) when it is missing.
//!
//! Only x86/x64 need this: there is no Windows 7 for arm64, so on every other target the macro expands to nothing.

#![allow(clippy::missing_safety_doc)]

#[cfg(all(windows, any(target_arch = "x86", target_arch = "x86_64")))]
mod shims;
#[cfg(all(windows, any(target_arch = "x86", target_arch = "x86_64")))]
#[doc(hidden)]
pub use shims::*;

/// Routes the Windows 8+ imports through this crate's shims. Invoke it once in the root of each final artifact: the
/// `__imp_` symbols must live in that crate's own object file, because a definition inside an rlib is only used if the
/// linker happens to search it before the import library.
///
/// The symbols are defined with `global_asm!` rather than `#[export_name]` statics, because a cdylib exports every
/// `#[export_name]` item, which would put `__imp_WaitOnAddress` etc. in velopack_wix.dll's export table.
#[cfg(all(windows, target_arch = "x86_64"))]
#[macro_export]
macro_rules! link_win7_shims {
    () => {
        ::core::arch::global_asm!(
            ".section .rdata,\"dr\"",
            ".p2align 3",
            ".globl __imp_GetSystemTimePreciseAsFileTime",
            "__imp_GetSystemTimePreciseAsFileTime:",
            ".quad {get_system_time_precise_as_file_time}",
            ".globl __imp_ProcessPrng",
            "__imp_ProcessPrng:",
            ".quad {process_prng}",
            ".globl __imp_WaitOnAddress",
            "__imp_WaitOnAddress:",
            ".quad {wait_on_address}",
            ".globl __imp_WakeByAddressSingle",
            "__imp_WakeByAddressSingle:",
            ".quad {wake_by_address_single}",
            ".globl __imp_WakeByAddressAll",
            "__imp_WakeByAddressAll:",
            ".quad {wake_by_address_all}",
            ".globl __imp_CoTaskMemAlloc",
            "__imp_CoTaskMemAlloc:",
            ".quad {co_task_mem_alloc}",
            ".globl __imp_CoTaskMemFree",
            "__imp_CoTaskMemFree:",
            ".quad {co_task_mem_free}",
            ".globl __imp_CoCreateFreeThreadedMarshaler",
            "__imp_CoCreateFreeThreadedMarshaler:",
            ".quad {co_create_free_threaded_marshaler}",
            ".text",
            get_system_time_precise_as_file_time = sym $crate::get_system_time_precise_as_file_time,
            process_prng = sym $crate::process_prng,
            wait_on_address = sym $crate::wait_on_address,
            wake_by_address_single = sym $crate::wake_by_address_single,
            wake_by_address_all = sym $crate::wake_by_address_all,
            co_task_mem_alloc = sym $crate::co_task_mem_alloc,
            co_task_mem_free = sym $crate::co_task_mem_free,
            co_create_free_threaded_marshaler = sym $crate::co_create_free_threaded_marshaler,
        );
    };
}

/// x86 variant of the macro above: std and windows-core reference the stdcall-decorated names (`__imp__Name@argbytes`).
#[cfg(all(windows, target_arch = "x86"))]
#[macro_export]
macro_rules! link_win7_shims {
    () => {
        ::core::arch::global_asm!(
            ".section .rdata,\"dr\"",
            ".p2align 2",
            ".globl \"__imp__GetSystemTimePreciseAsFileTime@4\"",
            "\"__imp__GetSystemTimePreciseAsFileTime@4\":",
            ".long {get_system_time_precise_as_file_time}",
            ".globl \"__imp__ProcessPrng@8\"",
            "\"__imp__ProcessPrng@8\":",
            ".long {process_prng}",
            ".globl \"__imp__WaitOnAddress@16\"",
            "\"__imp__WaitOnAddress@16\":",
            ".long {wait_on_address}",
            ".globl \"__imp__WakeByAddressSingle@4\"",
            "\"__imp__WakeByAddressSingle@4\":",
            ".long {wake_by_address_single}",
            ".globl \"__imp__WakeByAddressAll@4\"",
            "\"__imp__WakeByAddressAll@4\":",
            ".long {wake_by_address_all}",
            ".globl \"__imp__CoTaskMemAlloc@4\"",
            "\"__imp__CoTaskMemAlloc@4\":",
            ".long {co_task_mem_alloc}",
            ".globl \"__imp__CoTaskMemFree@4\"",
            "\"__imp__CoTaskMemFree@4\":",
            ".long {co_task_mem_free}",
            ".globl \"__imp__CoCreateFreeThreadedMarshaler@8\"",
            "\"__imp__CoCreateFreeThreadedMarshaler@8\":",
            ".long {co_create_free_threaded_marshaler}",
            ".text",
            get_system_time_precise_as_file_time = sym $crate::get_system_time_precise_as_file_time,
            process_prng = sym $crate::process_prng,
            wait_on_address = sym $crate::wait_on_address,
            wake_by_address_single = sym $crate::wake_by_address_single,
            wake_by_address_all = sym $crate::wake_by_address_all,
            co_task_mem_alloc = sym $crate::co_task_mem_alloc,
            co_task_mem_free = sym $crate::co_task_mem_free,
            co_create_free_threaded_marshaler = sym $crate::co_create_free_threaded_marshaler,
        );
    };
}

/// Nothing to do on targets that never run on Windows 7.
#[cfg(not(all(windows, any(target_arch = "x86", target_arch = "x86_64"))))]
#[macro_export]
macro_rules! link_win7_shims {
    () => {};
}

// The test harness itself runs on the shims, so every std lock in the tests goes through them.
#[cfg(test)]
link_win7_shims!();
