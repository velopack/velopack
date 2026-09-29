#[derive(PartialEq, Debug, Clone, strum::IntoStaticStr)]
pub enum RuntimeArch {
    X86,
    X64,
    Arm64,
}

impl RuntimeArch {
    #[cfg(target_os = "windows")]
    fn from_u16(value: u16) -> Option<Self> {
        match value {
            0x014c => Some(RuntimeArch::X86),
            0x8664 => Some(RuntimeArch::X64),
            0xAA64 => Some(RuntimeArch::Arm64),
            _ => None,
        }
    }

    #[allow(clippy::should_implement_trait)]
    pub fn from_str(arch_str: &str) -> Option<Self> {
        match arch_str.to_lowercase().as_str() {
            "x86" => Some(RuntimeArch::X86),
            "i386" => Some(RuntimeArch::X86),
            "x64" => Some(RuntimeArch::X64),
            "x86_64" => Some(RuntimeArch::X64),
            "arm64" => Some(RuntimeArch::Arm64),
            "aarch64" => Some(RuntimeArch::Arm64),
            _ => None,
        }
    }

    #[cfg(target_os = "windows")]
    pub fn from_current_system() -> Option<Self> {
        check_arch_windows()
    }

    #[cfg(not(target_os = "windows"))]
    pub fn from_current_system() -> Option<Self> {
        let info = os_info::get();
        let machine = info.architecture()?;
        if machine.is_empty() {
            return None;
        }
        Self::from_str(machine)
    }
}

/// The architecture this binary was compiled for.
#[cfg(target_os = "windows")]
const CURRENT_PROCESS_ARCH: RuntimeArch = if cfg!(target_arch = "x86_64") {
    RuntimeArch::X64
} else if cfg!(target_arch = "aarch64") {
    RuntimeArch::Arm64
} else {
    RuntimeArch::X86
};

#[cfg(target_os = "windows")]
fn check_arch_windows() -> Option<RuntimeArch> {
    use windows::Win32::Foundation::{FALSE, TRUE};
    use windows::Win32::System::Threading::{GetCurrentProcess, IsWow64Process};

    unsafe {
        let handle = GetCurrentProcess();
        if let Ok(x) = is_wow64_process2(handle) {
            return RuntimeArch::from_u16(x);
        }

        // IsWow64Process2 is only available since Windows 10 1709 (build 16299), so older
        // systems (eg. Server 2016 / Windows 10 1607) fall back to IsWow64Process. That only
        // tells us whether we're an x86 process emulated on a 64-bit OS - it reports FALSE for
        // a native process, in which case the OS architecture is our own. ARM64 Windows always
        // has IsWow64Process2, so TRUE here can only mean an x86 process on an x64 OS.
        let mut iswow64 = FALSE;
        if let Ok(()) = IsWow64Process(handle, &mut iswow64) {
            if iswow64 == TRUE {
                return Some(RuntimeArch::X64);
            } else {
                return Some(CURRENT_PROCESS_ARCH);
            }
        }

        Some(CURRENT_PROCESS_ARCH)
    }
}

#[cfg(target_os = "windows")]
type IsWow64Process2Fn = unsafe extern "system" fn(
    hprocess: windows::Win32::Foundation::HANDLE,
    pprocessmachine: *mut windows::Win32::System::SystemInformation::IMAGE_FILE_MACHINE,
    pnativemachine: *mut windows::Win32::System::SystemInformation::IMAGE_FILE_MACHINE,
) -> windows::core::BOOL;

#[cfg(target_os = "windows")]
unsafe fn is_wow64_process2(handle: windows::Win32::Foundation::HANDLE) -> anyhow::Result<u16> {
    use windows::Win32::Foundation::TRUE;
    use windows::Win32::System::SystemInformation::IMAGE_FILE_MACHINE;

    let lib = libloading::Library::new("kernel32.dll")?;
    let func: libloading::Symbol<IsWow64Process2Fn> = lib.get(b"IsWow64Process2")?;
    let mut process_machine: IMAGE_FILE_MACHINE = Default::default();
    let mut native_machine: IMAGE_FILE_MACHINE = Default::default();

    let result = func(handle, &mut process_machine, &mut native_machine);
    if result == TRUE {
        Ok(native_machine.0)
    } else {
        Err(anyhow::anyhow!("IsWow64Process2 failed"))
    }
}

#[test]
#[cfg(target_os = "windows")]
fn test_current_architecture() {
    let arch = check_arch_windows().unwrap();
    // a 64-bit process can only run on a 64-bit OS, so we must never report x86 here
    // (this used to happen when IsWow64Process2 was unavailable, see issue #1071)
    if CURRENT_PROCESS_ARCH != RuntimeArch::X86 {
        assert_eq!(arch, CURRENT_PROCESS_ARCH);
    }
}

#[test]
fn test_cpu_arch_from_str() {
    assert_eq!(RuntimeArch::from_str("x86"), Some(RuntimeArch::X86));
    assert_eq!(RuntimeArch::from_str("x64"), Some(RuntimeArch::X64));
    assert_eq!(RuntimeArch::from_str("arm64"), Some(RuntimeArch::Arm64));
    assert_eq!(RuntimeArch::from_str("foo"), None);
    assert_eq!(RuntimeArch::from_str("X86"), Some(RuntimeArch::X86));
    assert_eq!(RuntimeArch::from_str("X64"), Some(RuntimeArch::X64));
    assert_eq!(RuntimeArch::from_str("ARM64"), Some(RuntimeArch::Arm64));
}
