//! The shims `link_win7_shims!` points the Win8+ imports at. They run underneath std's own locks, so they must never
//! use std synchronization themselves; everything here is atomics and raw Win32 calls.

use core::cell::UnsafeCell;
use core::ffi::c_void;
use core::sync::atomic::{AtomicUsize, Ordering};
use windows_sys::core::{BOOL, HRESULT, PCSTR, PCWSTR};
use windows_sys::Win32::Foundation::{GetLastError, SetLastError, ERROR_INVALID_PARAMETER, FILETIME, TRUE};
use windows_sys::Win32::Security::Authentication::Identity::RtlGenRandom;
use windows_sys::Win32::System::LibraryLoader::{GetModuleHandleW, GetProcAddress, LoadLibraryExW, LoadLibraryW, LOAD_LIBRARY_SEARCH_SYSTEM32};
use windows_sys::Win32::System::SystemInformation::GetSystemTimeAsFileTime;
use windows_sys::Win32::System::Threading::{
    AcquireSRWLockExclusive, ReleaseSRWLockExclusive, SleepConditionVariableSRW, WakeAllConditionVariable, CONDITION_VARIABLE,
    CONDITION_VARIABLE_INIT, SRWLOCK, SRWLOCK_INIT,
};
use windows_sys::{s, w};

/// A function or module address resolved on first use. Racing threads resolve the same value, so no locking is needed.
struct LazyAddress(AtomicUsize);

const UNRESOLVED: usize = 0;
const MISSING: usize = 1;

impl LazyAddress {
    const fn new() -> Self {
        Self(AtomicUsize::new(UNRESOLVED))
    }

    fn get(&self, resolve: impl FnOnce() -> Option<usize>) -> Option<usize> {
        let mut address = self.0.load(Ordering::Acquire);
        if address == UNRESOLVED {
            address = resolve().unwrap_or(MISSING);
            self.0.store(address, Ordering::Release);
        }
        (address != MISSING).then_some(address)
    }
}

unsafe fn find_export(module: *mut c_void, name: PCSTR) -> Option<usize> {
    if module.is_null() {
        return None;
    }
    GetProcAddress(module, name).map(|f| f as usize)
}

/// Loads a DLL from System32 only. Without the search flag a DLL missing on Windows 7 would be searched for next to
/// the exe (e.g. Setup.exe in Downloads), where anyone could plant it. Windows 7 without KB2533623 rejects the flag,
/// which just means we use the fallback.
unsafe fn find_system32_export(dll: PCWSTR, name: PCSTR) -> Option<usize> {
    find_export(LoadLibraryExW(dll, core::ptr::null_mut(), LOAD_LIBRARY_SEARCH_SYSTEM32), name)
}

/// WaitOnAddress and the two WakeByAddress functions must be all real or all fallback: a thread blocked in the real
/// WaitOnAddress is never woken by the fallback's condition variables, and vice versa. So one decision covers all
/// three: the real ones are used only if the DLL exports every one of them.
static SYNCH_MODULE: LazyAddress = LazyAddress::new();

/// The real synchronization function `name`, or None when all three must use the fallback.
unsafe fn find_synch_export(function: &LazyAddress, name: PCSTR) -> Option<usize> {
    let module = SYNCH_MODULE.get(|| {
        let module = LoadLibraryExW(
            w!("api-ms-win-core-synch-l1-2-0.dll"),
            core::ptr::null_mut(),
            LOAD_LIBRARY_SEARCH_SYSTEM32,
        );
        let exports_all = [s!("WaitOnAddress"), s!("WakeByAddressSingle"), s!("WakeByAddressAll")]
            .into_iter()
            .all(|name| find_export(module, name).is_some());
        exports_all.then_some(module as usize)
    })?;
    // The module was checked to export it, and falling back now would mix the real and fallback implementations.
    Some(
        function
            .get(|| find_export(module as *mut c_void, name))
            .unwrap_or_else(|| std::process::abort()),
    )
}

/// windows-core links its COM functions from combase.dll (Windows 8+); Windows 7 exports the same functions from
/// ole32.dll. ole32 is a KnownDLL, so plain LoadLibraryW always loads it from System32.
unsafe fn find_com_export(function: &LazyAddress, name: PCSTR) -> usize {
    let address = function.get(|| find_system32_export(w!("combase.dll"), name).or_else(|| find_export(LoadLibraryW(w!("ole32.dll")), name)));
    // Every supported Windows has these, and their callers have no way to handle them missing.
    address.unwrap_or_else(|| std::process::abort())
}

static GET_SYSTEM_TIME_PRECISE_AS_FILE_TIME: LazyAddress = LazyAddress::new();
static PROCESS_PRNG: LazyAddress = LazyAddress::new();
static WAIT_ON_ADDRESS: LazyAddress = LazyAddress::new();
static WAKE_BY_ADDRESS_SINGLE: LazyAddress = LazyAddress::new();
static WAKE_BY_ADDRESS_ALL: LazyAddress = LazyAddress::new();
static CO_TASK_MEM_ALLOC: LazyAddress = LazyAddress::new();
static CO_TASK_MEM_FREE: LazyAddress = LazyAddress::new();
static CO_CREATE_FREE_THREADED_MARSHALER: LazyAddress = LazyAddress::new();

/// `GetSystemTimePreciseAsFileTime` (Windows 8+), falling back to the ~1-16ms resolution `GetSystemTimeAsFileTime`.
pub unsafe extern "system" fn get_system_time_precise_as_file_time(time: *mut FILETIME) {
    let real = GET_SYSTEM_TIME_PRECISE_AS_FILE_TIME.get(|| find_export(GetModuleHandleW(w!("kernel32.dll")), s!("GetSystemTimePreciseAsFileTime")));
    match real {
        Some(f) => core::mem::transmute::<usize, unsafe extern "system" fn(*mut FILETIME)>(f)(time),
        None => GetSystemTimeAsFileTime(time),
    }
}

/// `ProcessPrng`, falling back to `RtlGenRandom`, which is what std uses on the win7 targets.
pub unsafe extern "system" fn process_prng(data: *mut u8, len: usize) -> BOOL {
    match PROCESS_PRNG.get(|| find_system32_export(w!("bcryptprimitives.dll"), s!("ProcessPrng"))) {
        Some(f) => core::mem::transmute::<usize, unsafe extern "system" fn(*mut u8, usize) -> BOOL>(f)(data, len),
        None => fallback::process_prng(data, len),
    }
}

/// `WaitOnAddress`, falling back to the bucketed condition variables below.
pub unsafe extern "system" fn wait_on_address(address: *const c_void, compare: *const c_void, size: usize, timeout_ms: u32) -> BOOL {
    match find_synch_export(&WAIT_ON_ADDRESS, s!("WaitOnAddress")) {
        Some(f) => {
            let f = core::mem::transmute::<usize, unsafe extern "system" fn(*const c_void, *const c_void, usize, u32) -> BOOL>(f);
            f(address, compare, size, timeout_ms)
        }
        None => fallback::wait_on_address(address, compare, size, timeout_ms),
    }
}

/// `WakeByAddressSingle`; see [`wait_on_address`].
pub unsafe extern "system" fn wake_by_address_single(address: *const c_void) {
    match find_synch_export(&WAKE_BY_ADDRESS_SINGLE, s!("WakeByAddressSingle")) {
        Some(f) => core::mem::transmute::<usize, unsafe extern "system" fn(*const c_void)>(f)(address),
        None => fallback::wake_by_address(address),
    }
}

/// `WakeByAddressAll`; see [`wait_on_address`].
pub unsafe extern "system" fn wake_by_address_all(address: *const c_void) {
    match find_synch_export(&WAKE_BY_ADDRESS_ALL, s!("WakeByAddressAll")) {
        Some(f) => core::mem::transmute::<usize, unsafe extern "system" fn(*const c_void)>(f)(address),
        None => fallback::wake_by_address(address),
    }
}

/// `CoTaskMemAlloc` from combase.dll, or ole32.dll on Windows 7.
pub unsafe extern "system" fn co_task_mem_alloc(size: usize) -> *mut c_void {
    let f = find_com_export(&CO_TASK_MEM_ALLOC, s!("CoTaskMemAlloc"));
    core::mem::transmute::<usize, unsafe extern "system" fn(usize) -> *mut c_void>(f)(size)
}

/// `CoTaskMemFree` from combase.dll, or ole32.dll on Windows 7.
pub unsafe extern "system" fn co_task_mem_free(memory: *const c_void) {
    let f = find_com_export(&CO_TASK_MEM_FREE, s!("CoTaskMemFree"));
    core::mem::transmute::<usize, unsafe extern "system" fn(*const c_void)>(f)(memory)
}

/// `CoCreateFreeThreadedMarshaler` from combase.dll, or ole32.dll on Windows 7.
pub unsafe extern "system" fn co_create_free_threaded_marshaler(outer: *mut c_void, marshaler: *mut *mut c_void) -> HRESULT {
    let f = find_com_export(&CO_CREATE_FREE_THREADED_MARSHALER, s!("CoCreateFreeThreadedMarshaler"));
    core::mem::transmute::<usize, unsafe extern "system" fn(*mut c_void, *mut *mut c_void) -> HRESULT>(f)(outer, marshaler)
}

mod fallback {
    use super::*;

    pub unsafe fn process_prng(mut data: *mut u8, mut len: usize) -> BOOL {
        while len > 0 {
            let chunk = len.min(u32::MAX as usize);
            if !RtlGenRandom(data.cast(), chunk as u32) {
                // std treats ProcessPrng as infallible, so there is no way to report this.
                std::process::abort();
            }
            data = data.add(chunk);
            len -= chunk;
        }
        TRUE
    }

    /// Waiters are hashed by address onto a fixed set of buckets, each an SRW lock plus a condition variable
    /// (both available since Vista). A waker always changes the value before calling wake, and wake takes the bucket
    /// lock, so a waiter either sees the new value under the lock or is already asleep on the condition variable.
    struct Bucket {
        lock: UnsafeCell<SRWLOCK>,
        waiters: UnsafeCell<CONDITION_VARIABLE>,
    }

    unsafe impl Sync for Bucket {}

    const BUCKET_BITS: u32 = 6;
    #[allow(clippy::declare_interior_mutable_const)]
    const EMPTY_BUCKET: Bucket = Bucket {
        lock: UnsafeCell::new(SRWLOCK_INIT),
        waiters: UnsafeCell::new(CONDITION_VARIABLE_INIT),
    };
    static BUCKETS: [Bucket; 1 << BUCKET_BITS] = [EMPTY_BUCKET; 1 << BUCKET_BITS];

    fn bucket(address: *const c_void) -> &'static Bucket {
        // Fibonacci hashing: the top bits of the product depend on every bit of the address.
        let hash = (address as usize as u64).wrapping_mul(0x9E37_79B9_7F4A_7C15);
        &BUCKETS[(hash >> (64 - BUCKET_BITS)) as usize]
    }

    pub unsafe fn wait_on_address(address: *const c_void, compare: *const c_void, size: usize, timeout_ms: u32) -> BOOL {
        let bucket = bucket(address);
        AcquireSRWLockExclusive(bucket.lock.get());
        let unchanged = match size {
            1 => address.cast::<u8>().read_volatile() == compare.cast::<u8>().read(),
            2 => address.cast::<u16>().read_volatile() == compare.cast::<u16>().read(),
            4 => address.cast::<u32>().read_volatile() == compare.cast::<u32>().read(),
            8 => address.cast::<u64>().read_volatile() == compare.cast::<u64>().read(),
            _ => {
                ReleaseSRWLockExclusive(bucket.lock.get());
                SetLastError(ERROR_INVALID_PARAMETER);
                return 0;
            }
        };
        // Same contract as WaitOnAddress: TRUE right away if the value already changed, FALSE with
        // ERROR_TIMEOUT (set by SleepConditionVariableSRW) on timeout, and TRUE on (possibly spurious) wakeups.
        // A failed sleep's last error is re-set after unlocking: std's futex_wait checks it for ERROR_TIMEOUT,
        // and ReleaseSRWLockExclusive is not documented to leave the thread's last error untouched.
        let result = if unchanged {
            SleepConditionVariableSRW(bucket.waiters.get(), bucket.lock.get(), timeout_ms, 0)
        } else {
            TRUE
        };
        let error = if result == 0 {
            GetLastError()
        } else {
            0
        };
        ReleaseSRWLockExclusive(bucket.lock.get());
        if result == 0 {
            SetLastError(error);
        }
        result
    }

    /// Wakes every waiter in the bucket, also for `WakeByAddressSingle`: WaitOnAddress callers must already
    /// re-check the value after waking, so the extra wakeups are only a cost, not a correctness issue.
    pub unsafe fn wake_by_address(address: *const c_void) {
        let bucket = bucket(address);
        AcquireSRWLockExclusive(bucket.lock.get());
        WakeAllConditionVariable(bucket.waiters.get());
        ReleaseSRWLockExclusive(bucket.lock.get());
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicU32;
    use std::sync::Arc;
    use std::time::{Duration, Instant};
    use windows_sys::Win32::Foundation::{GetLastError, ERROR_TIMEOUT};

    fn ptr<T>(value: &T) -> *const c_void {
        (value as *const T).cast()
    }

    #[test]
    fn real_functions_resolve_on_windows_8_and_newer() {
        let mut time = FILETIME {
            dwLowDateTime: 0,
            dwHighDateTime: 0,
        };
        let mut buf = [0u8; 4];
        let value = AtomicU32::new(1);
        unsafe {
            get_system_time_precise_as_file_time(&mut time);
            process_prng(buf.as_mut_ptr(), buf.len());
            wait_on_address(ptr(&value), ptr(&0u32), 4, 0);
            wake_by_address_single(ptr(&value));
            wake_by_address_all(ptr(&value));
        }
        for f in [
            &GET_SYSTEM_TIME_PRECISE_AS_FILE_TIME,
            &PROCESS_PRNG,
            &SYNCH_MODULE,
            &WAIT_ON_ADDRESS,
            &WAKE_BY_ADDRESS_SINGLE,
            &WAKE_BY_ADDRESS_ALL,
        ] {
            assert!(f.get(|| None).is_some());
        }
    }

    #[test]
    fn com_functions_resolve_and_ole32_has_them_too() {
        unsafe {
            let memory = co_task_mem_alloc(64);
            assert!(!memory.is_null());
            co_task_mem_free(memory);
            let mut marshaler = core::ptr::null_mut();
            assert_eq!(co_create_free_threaded_marshaler(core::ptr::null_mut(), &mut marshaler), 0);
            assert!(!marshaler.is_null());
            // Release the IUnknown (vtable slot 2) so the test does not leak it.
            let vtable = *(marshaler as *const *const unsafe extern "system" fn(*mut c_void) -> u32);
            (*vtable.add(2))(marshaler);

            // The Windows 7 path: ole32 exports the same functions.
            let ole32 = LoadLibraryW(w!("ole32.dll"));
            for name in [s!("CoTaskMemAlloc"), s!("CoTaskMemFree"), s!("CoCreateFreeThreadedMarshaler")] {
                assert!(find_export(ole32, name).is_some());
            }
        }
    }

    #[test]
    fn fallback_wait_returns_immediately_when_value_differs() {
        let value = AtomicU32::new(1);
        let start = Instant::now();
        assert_eq!(unsafe { fallback::wait_on_address(ptr(&value), ptr(&0u32), 4, 5000) }, TRUE);
        assert!(start.elapsed() < Duration::from_secs(1));
    }

    #[test]
    fn fallback_wait_times_out() {
        let value = AtomicU32::new(0);
        let start = Instant::now();
        assert_eq!(unsafe { fallback::wait_on_address(ptr(&value), ptr(&0u32), 4, 100) }, 0);
        assert_eq!(unsafe { GetLastError() }, ERROR_TIMEOUT);
        assert!(start.elapsed() >= Duration::from_millis(90));
    }

    #[test]
    fn fallback_wait_rejects_invalid_size() {
        let value = 0u32;
        assert_eq!(unsafe { fallback::wait_on_address(ptr(&value), ptr(&0u32), 3, 0) }, 0);
        assert_eq!(unsafe { GetLastError() }, ERROR_INVALID_PARAMETER);
    }

    #[test]
    fn fallback_wake_releases_waiters() {
        let value = Arc::new(AtomicU32::new(0));
        let waiters: Vec<_> = (0..4)
            .map(|_| {
                let value = value.clone();
                std::thread::spawn(move || {
                    while value.load(Ordering::Acquire) == 0 {
                        unsafe { fallback::wait_on_address(ptr(&*value), ptr(&0u32), 4, u32::MAX) };
                    }
                })
            })
            .collect();
        std::thread::sleep(Duration::from_millis(50));
        value.store(1, Ordering::Release);
        unsafe { fallback::wake_by_address(ptr(&*value)) };
        for waiter in waiters {
            waiter.join().unwrap();
        }
    }

    #[test]
    fn fallback_random_fills_buffer() {
        let mut buf = [0u8; 256];
        assert_eq!(unsafe { fallback::process_prng(buf.as_mut_ptr(), buf.len()) }, TRUE);
        assert!(buf.iter().any(|&b| b != 0));
    }

    #[test]
    fn std_works_through_the_shims() {
        // The test binary links the shims (see lib.rs), so this exercises std's futex Mutex/Condvar through them.
        let pair = Arc::new((std::sync::Mutex::new(0u32), std::sync::Condvar::new()));
        let workers: Vec<_> = (0..8)
            .map(|_| {
                let pair = pair.clone();
                std::thread::spawn(move || {
                    for _ in 0..1000 {
                        *pair.0.lock().unwrap() += 1;
                        pair.1.notify_all();
                    }
                })
            })
            .collect();
        let mut count = pair.0.lock().unwrap();
        while *count < 8000 {
            count = pair.1.wait_timeout(count, Duration::from_millis(10)).unwrap().0;
        }
        drop(count);
        for worker in workers {
            worker.join().unwrap();
        }
        assert!(std::time::SystemTime::now() > std::time::UNIX_EPOCH);
    }
}
