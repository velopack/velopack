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

#[cfg(target_os = "windows")]
fn check_arch_windows() -> Option<RuntimeArch> {
    use windows::Win32::System::Threading::GetCurrentProcess;

    unsafe {
        let handle = GetCurrentProcess();
        if let Ok(x) = is_wow64_process2(handle) {
            return RuntimeArch::from_u16(x);
        }

        // Everything below is the pre Windows 10 1511 path, where IsWow64Process2 does not
        // exist.
        //
        // IsWow64Process answers only "is this process running under WOW64", meaning a 32 bit
        // process on 64 bit Windows. A 64 bit process is never under WOW64, so it answers FALSE
        // on 64 bit Windows and the old code read that as a 32 bit machine. That is why the x64
        // setup.exe refused to install on 64 bit Windows 7 with "this application (x64) does not
        // support your CPU architecture": it decided the machine was x86.
        //
        // A 64 bit process can only be running on a 64 bit OS, so when this binary is 64 bit
        // there is nothing to probe.
        #[cfg(target_arch = "x86_64")]
        return Some(RuntimeArch::X64);

        #[cfg(target_arch = "aarch64")]
        return Some(RuntimeArch::Arm64);

        // Only a 32 bit process needs to ask, to tell 32 bit Windows apart from WOW64 on a
        // 64 bit machine.
        #[cfg(target_arch = "x86")]
        {
            use windows::Win32::Foundation::{FALSE, TRUE};
            use windows::Win32::System::Threading::IsWow64Process;

            let mut iswow64 = FALSE;
            if let Ok(()) = IsWow64Process(handle, &mut iswow64) {
                if iswow64 == TRUE {
                    return Some(RuntimeArch::X64);
                }
            }

            return Some(RuntimeArch::X86);
        }
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
    let arch = check_arch_windows();
    assert!(arch.is_some());
    let arch = arch.unwrap();
    assert!(arch == RuntimeArch::X64);
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
