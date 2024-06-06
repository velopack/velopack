#[derive(PartialEq, Debug, Clone, strum::IntoStaticStr)]
pub enum RuntimeArch {
    X86,
    X64,
    Arm64,
}

impl RuntimeArch {
    #[cfg(target_os = "windows")]
    fn from_u32(value: u32) -> Option<Self> {
        match value {
            0x014c => Some(RuntimeArch::X86),
            0x8664 => Some(RuntimeArch::X64),
            0xAA64 => Some(RuntimeArch::Arm64),
            _ => None,
        }
    }

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
        return check_arch_windows();
    }

    #[cfg(not(target_os = "windows"))]
    pub fn from_current_system() -> Option<Self> {
        let info = os_info::get();
        let machine = info.architecture();
        if machine.is_none() {
            return None;
        }
        let machine = machine.unwrap();
        if machine.is_empty() {
            return None;
        }
        Self::from_str(machine)
    }
}

#[cfg(target_os = "windows")]
fn check_arch_windows() -> Option<RuntimeArch> {
    use windows::Win32::Foundation::{FALSE, TRUE};
    use windows::Win32::System::SystemInformation::IMAGE_FILE_MACHINE;
    use windows::Win32::System::Threading::{GetCurrentProcess, IsWow64Process, IsWow64Process2};

    unsafe {
        let handle = GetCurrentProcess();

        let mut process_machine: IMAGE_FILE_MACHINE = Default::default();
        let mut native_machine: IMAGE_FILE_MACHINE = Default::default();
        if let Ok(()) = IsWow64Process2(handle, &mut process_machine, Some(&mut native_machine)) {
            return RuntimeArch::from_u32(native_machine.0.into());
        }

        let mut iswow64 = FALSE;
        if let Ok(()) = IsWow64Process(handle, &mut iswow64) {
            if iswow64 == TRUE {
                return Some(RuntimeArch::X64);
            } else {
                return Some(RuntimeArch::X86);
            }
        }

        #[cfg(target_arch = "x86_64")]
        return Some(RuntimeArch::X64);

        #[cfg(not(target_arch = "x86_64"))]
        return Some(RuntimeArch::X86);
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
