use crate::{shared as util, shared::runtime_arch::RuntimeArch};
use anyhow::{anyhow, bail, Result};
use regex::Regex;
use std::process::Command as Process;
use std::{collections::HashMap, fs, path::Path};
use velopack::download;
use winsafe::{self as w, co, prelude::*};

const REDIST_2015_2022_X86: &str = "https://aka.ms/vs/17/release/vc_redist.x86.exe";
const REDIST_2015_2022_X64: &str = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
const REDIST_2015_2022_ARM64: &str = "https://aka.ms/vs/17/release/vc_redist.arm64.exe";
const NDP_REG_KEY: &str = "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";
const UNINSTALL_REG_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
const WEBVIEW2_EVERGREEN: &str = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
const DOTNET_UNCACHED_FEED: &str = "https://dotnetcli.blob.core.windows.net/dotnet";
const DOTNET_CDN_FEED: &str = "https://builds.dotnet.microsoft.com/dotnet";

#[rustfmt::skip]
lazy_static! {
    static ref HM_NET_FX: HashMap<&'static str, FullFrameworkInfo> = {
        let mut net_fx: HashMap<&'static str, FullFrameworkInfo> = HashMap::new();
        // https://learn.microsoft.com/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#detect-net-framework-45-and-later-versions
        net_fx.insert("net45", FullFrameworkInfo::new(".NET Framework 4.5", "http://go.microsoft.com/fwlink/?LinkId=397707", 378389));
        net_fx.insert("net451", FullFrameworkInfo::new(".NET Framework 4.5.1", "http://go.microsoft.com/fwlink/?LinkId=397707", 378675));
        net_fx.insert("net452", FullFrameworkInfo::new(".NET Framework 4.5.2", "http://go.microsoft.com/fwlink/?LinkId=397707", 379893));
        net_fx.insert("net46", FullFrameworkInfo::new(".NET Framework 4.6", "http://go.microsoft.com/fwlink/?LinkId=780596", 393295));
        net_fx.insert("net461", FullFrameworkInfo::new(".NET Framework 4.6.1", "http://go.microsoft.com/fwlink/?LinkId=780596", 394254));
        net_fx.insert("net462", FullFrameworkInfo::new(".NET Framework 4.6.2", "http://go.microsoft.com/fwlink/?LinkId=780596", 394802));
        net_fx.insert("net47", FullFrameworkInfo::new(".NET Framework 4.7", "http://go.microsoft.com/fwlink/?LinkId=863262", 460798));
        net_fx.insert("net471", FullFrameworkInfo::new(".NET Framework 4.7.1", "http://go.microsoft.com/fwlink/?LinkId=863262", 461308));
        net_fx.insert("net472", FullFrameworkInfo::new(".NET Framework 4.7.2", "http://go.microsoft.com/fwlink/?LinkId=863262", 461808));
        net_fx.insert("net48", FullFrameworkInfo::new(".NET Framework 4.8", "http://go.microsoft.com/fwlink/?LinkId=2085155", 528040));
        net_fx.insert("net481", FullFrameworkInfo::new(".NET Framework 4.8.1", "http://go.microsoft.com/fwlink/?LinkId=2203304", 533320));
        net_fx
    };

    static ref HM_VCREDIST: HashMap<&'static str, VCRedistInfo> = {
        let mut vcredist: HashMap<&'static str, VCRedistInfo> = HashMap::new();
        vcredist.insert("vcredist100-x86", VCRedistInfo::new("Visual C++ 2010 Redist (x86)", "10.00.40219", RuntimeArch::X86, "https://download.microsoft.com/download/C/6/D/C6D0FD4E-9E53-4897-9B91-836EBA2AACD3/vcredist_x86.exe"));
        vcredist.insert("vcredist100-x64", VCRedistInfo::new("Visual C++ 2010 Redist (x64)", "10.00.40219", RuntimeArch::X64, "https://download.microsoft.com/download/A/8/0/A80747C3-41BD-45DF-B505-E9710D2744E0/vcredist_x64.exe"));
        vcredist.insert("vcredist110-x86", VCRedistInfo::new("Visual C++ 2012 Redist (x86)", "11.00.61030", RuntimeArch::X86, "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe"));
        vcredist.insert("vcredist110-x64", VCRedistInfo::new("Visual C++ 2012 Redist (x64)", "11.00.61030", RuntimeArch::X64, "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x64.exe"));
        vcredist.insert("vcredist120-x86", VCRedistInfo::new("Visual C++ 2013 Redist (x86)", "12.00.40664", RuntimeArch::X86, "https://aka.ms/highdpimfc2013x86enu"));
        vcredist.insert("vcredist120-x64", VCRedistInfo::new("Visual C++ 2013 Redist (x64)", "12.00.40664", RuntimeArch::X64, "https://aka.ms/highdpimfc2013x64enu"));
        // from 2015-2022, the binaries are all compatible, so we can always just install the latest version
        // https://docs.microsoft.com/cpp/windows/latest-supported-vc-redist?view=msvc-170#visual-studio-2015-2017-2019-and-2022
        // https://docs.microsoft.com/cpp/porting/binary-compat-2015-2017?view=msvc-170
        vcredist.insert("vcredist140-x86", VCRedistInfo::new("Visual C++ 2015 Redist (x86)", "14.00.23506", RuntimeArch::X86, REDIST_2015_2022_X86));
        vcredist.insert("vcredist140-x64", VCRedistInfo::new("Visual C++ 2015 Redist (x64)", "14.00.23506", RuntimeArch::X64, REDIST_2015_2022_X64));
        vcredist.insert("vcredist141-x86", VCRedistInfo::new("Visual C++ 2017 Redist (x86)", "14.15.26706", RuntimeArch::X86, REDIST_2015_2022_X86));
        vcredist.insert("vcredist141-x64", VCRedistInfo::new("Visual C++ 2017 Redist (x64)", "14.15.26706", RuntimeArch::X64, REDIST_2015_2022_X64));
        vcredist.insert("vcredist142-x86", VCRedistInfo::new("Visual C++ 2019 Redist (x86)", "14.20.27508", RuntimeArch::X86, REDIST_2015_2022_X86));
        vcredist.insert("vcredist142-x64", VCRedistInfo::new("Visual C++ 2019 Redist (x64)", "14.20.27508", RuntimeArch::X64, REDIST_2015_2022_X64));
        vcredist.insert("vcredist143-x86", VCRedistInfo::new("Visual C++ 2022 Redist (x86)", "14.30.30704", RuntimeArch::X86, REDIST_2015_2022_X86));
        vcredist.insert("vcredist143-x64", VCRedistInfo::new("Visual C++ 2022 Redist (x64)", "14.30.30704", RuntimeArch::X64, REDIST_2015_2022_X64));
        vcredist.insert("vcredist143-arm64", VCRedistInfo::new("Visual C++ 2022 Redist (arm64)", "14.30.30704", RuntimeArch::Arm64, REDIST_2015_2022_ARM64));
        vcredist.insert("vcredist144-x86", VCRedistInfo::new("Visual C++ 2022 Redist (x86)", "14.40.33810", RuntimeArch::X86, REDIST_2015_2022_X86));
        vcredist.insert("vcredist144-x64", VCRedistInfo::new("Visual C++ 2022 Redist (x64)", "14.40.33810", RuntimeArch::X64, REDIST_2015_2022_X64));
        vcredist.insert("vcredist144-arm64", VCRedistInfo::new("Visual C++ 2022 Redist (arm64)", "14.40.33810", RuntimeArch::Arm64, REDIST_2015_2022_ARM64));
        vcredist
    };
}

#[derive(Debug, PartialEq, Clone, strum::IntoStaticStr)]
pub enum RuntimeInstallResult {
    InstallSuccess,
    RestartRequired,
}

fn def_installer_routine(installer_path: &str, quiet: bool) -> Result<RuntimeInstallResult> {
    let mut args = Vec::new();
    args.push("/norestart");
    if quiet {
        args.push("/q");
    } else {
        args.push("/passive");
        args.push("/showrmui");
    }

    info!("Running installer: '{}', args={:?}", installer_path, args);
    let mut cmd = Process::new(installer_path).args(&args).spawn()?;
    let result: i32 = cmd.wait()?.code().ok_or_else(|| anyhow!("Unable to get installer exit code."))?;

    // https://johnkoerner.com/install/windows-installer-error-codes/
    match result {
        0 => Ok(RuntimeInstallResult::InstallSuccess), // success
        1602 => Err(anyhow!("User cancelled installation.")),
        1618 => Err(anyhow!("Another installation is already in progress.")),
        3010 => Ok(RuntimeInstallResult::RestartRequired), // success, restart required
        5100 => Err(anyhow!("System does not meet runtime requirements.")),
        1638 => Ok(RuntimeInstallResult::InstallSuccess), // a newer compatible version is already installed
        1641 => Ok(RuntimeInstallResult::RestartRequired), // installer initiated a restart
        _ => Err(anyhow!("Installer failed with exit code: {}", result)),
    }
}

pub trait RuntimeInfo {
    fn get_exe_name(&self) -> String;
    fn display_name(&self) -> &str;
    fn is_installed(&self) -> bool;
    fn get_download_url(&self) -> Result<String>;
    fn install(&self, installer_path: &str, quiet: bool) -> Result<RuntimeInstallResult>;
}

#[derive(Clone, Debug)]
pub struct FullFrameworkInfo {
    display_name: String,
    download_url: String,
    release_version: u32,
}

impl FullFrameworkInfo {
    pub fn new(display_name: &str, download_url: &str, release_version: u32) -> Self {
        FullFrameworkInfo { display_name: display_name.to_string(), download_url: download_url.to_string(), release_version }
    }
}

impl RuntimeInfo for FullFrameworkInfo {
    fn get_exe_name(&self) -> String {
        format!("inst-netfx-{}.exe", self.release_version)
    }

    fn display_name(&self) -> &str {
        &self.display_name
    }

    fn get_download_url(&self) -> Result<String> {
        Ok(self.download_url.to_owned())
    }

    fn is_installed(&self) -> bool {
        let lm = w::HKEY::LOCAL_MACHINE;
        let key = lm.RegOpenKeyEx(Some(NDP_REG_KEY), co::REG_OPTION::NoValue, co::KEY::READ);
        if key.is_err() {
            // key doesn't exist, so .net framework not installed
            return false;
        }
        let release = key.unwrap().RegGetValue(None, Some("Release"));
        if release.is_err() {
            // key doesn't exist, so .net framework not installed
            return false;
        }
        match release.unwrap() {
            w::RegistryValue::Dword(v) => return v >= self.release_version,
            _ => return false,
        }
    }

    fn install(&self, installer_path: &str, quiet: bool) -> Result<RuntimeInstallResult> {
        def_installer_routine(installer_path, quiet)
    }
}

#[derive(Clone, Debug)]
pub struct VCRedistInfo {
    display_name: String,
    download_url: String,
    min_version: String,
    architecture: RuntimeArch,
}

impl VCRedistInfo {
    pub fn new(display_name: &str, min_version: &str, architecture: RuntimeArch, download_url: &str) -> Self {
        VCRedistInfo {
            display_name: display_name.to_string(),
            min_version: min_version.to_string(),
            architecture,
            download_url: download_url.to_string(),
        }
    }
}

impl RuntimeInfo for VCRedistInfo {
    fn get_exe_name(&self) -> String {
        let my_arch: &'static str = self.clone().architecture.into();
        format!("inst-vcredist-{}-{}.exe", self.min_version, my_arch)
    }

    fn display_name(&self) -> &str {
        &self.display_name
    }

    fn get_download_url(&self) -> Result<String> {
        Ok(self.download_url.to_owned())
    }

    fn is_installed(&self) -> bool {
        let mut installed_programs = HashMap::new();
        get_installed_programs(&mut installed_programs, co::KEY::READ | co::KEY::WOW64_32KEY);
        get_installed_programs(&mut installed_programs, co::KEY::READ | co::KEY::WOW64_64KEY);
        let (my_major, my_minor, my_build, _) = util::parse_version(&self.min_version).unwrap();
        let reg = Regex::new(r"(?i)Microsoft Visual C\+\+(.*)Redistributable").unwrap();
        for (k, v) in installed_programs {
            if reg.is_match(&k) {
                let lower_name = k.to_lowercase();
                let my_arch: &'static str = self.clone().architecture.into();
                let my_arch = my_arch.to_lowercase();
                if lower_name.contains(&my_arch) {
                    // this is a vcredist of the same processor architecture. check if version satisfies our requirement
                    let (major, minor, build, _) = util::parse_version(&v).unwrap();
                    if my_major == major && minor >= my_minor && build >= my_build {
                        return true;
                    }
                }
            }
        }
        false
    }

    fn install(&self, installer_path: &str, quiet: bool) -> Result<RuntimeInstallResult> {
        def_installer_routine(installer_path, quiet)
    }
}

fn get_installed_programs(map: &mut HashMap<String, String>, access_rights: co::KEY) {
    let key = w::HKEY::LOCAL_MACHINE.RegOpenKeyEx(Some(UNINSTALL_REG_KEY), co::REG_OPTION::NoValue, access_rights);
    if let Ok(view) = key {
        if let Ok(iter) = view.RegEnumKeyEx() {
            for key_result in iter {
                if let Ok(key_name) = key_result {
                    let subkey = view.RegOpenKeyEx(Some(&key_name), co::REG_OPTION::NoValue, access_rights);
                    if subkey.is_err() {
                        continue;
                    }
                    let subkey = subkey.unwrap();
                    let name = subkey.RegQueryValueEx(Some("DisplayName"));
                    let version = subkey.RegQueryValueEx(Some("DisplayVersion"));
                    if name.is_ok() && version.is_ok() {
                        if let w::RegistryValue::Sz(display_name) = name.unwrap() {
                            if let w::RegistryValue::Sz(display_version) = version.unwrap() {
                                map.insert(display_name, display_version);
                            }
                        }
                    }
                }
            }
        }
    }
}

#[test]
fn test_get_installed_programs_returns_visual_studio() {
    let mut map = HashMap::new();
    get_installed_programs(&mut map, co::KEY::READ | co::KEY::WOW64_64KEY);
    assert!(map.contains_key("Microsoft Visual Studio Installer"));
}

#[test]
fn test_vcredist_is_installed_finds_vcredist143_but_not_arm64() {
    let vc143 = HM_VCREDIST.get("vcredist143-x64").unwrap();
    assert!(vc143.is_installed());

    let vc143 = HM_VCREDIST.get("vcredist143-arm64").unwrap();
    assert!(!vc143.is_installed());
}

#[derive(PartialEq, Debug, Clone, strum::IntoStaticStr)]
pub enum DotnetRuntimeType {
    Runtime,
    AspNetCore,
    WindowsDesktop,
    Sdk,
}

impl DotnetRuntimeType {
    pub fn from_str(runtime_str: &str) -> Option<Self> {
        match runtime_str.to_lowercase().as_str() {
            "runtime" => Some(DotnetRuntimeType::Runtime),
            "aspnetcore" => Some(DotnetRuntimeType::AspNetCore),
            "asp" => Some(DotnetRuntimeType::AspNetCore),
            "aspcore" => Some(DotnetRuntimeType::AspNetCore),
            "windowsdesktop" => Some(DotnetRuntimeType::WindowsDesktop),
            "desktop" => Some(DotnetRuntimeType::WindowsDesktop),
            "sdk" => Some(DotnetRuntimeType::Sdk),
            _ => None,
        }
    }
}

#[test]
fn test_dotnet_runtime_type_from_str() {
    assert_eq!(DotnetRuntimeType::from_str("runtime"), Some(DotnetRuntimeType::Runtime));
    assert_eq!(DotnetRuntimeType::from_str("aspnetcore"), Some(DotnetRuntimeType::AspNetCore));
    assert_eq!(DotnetRuntimeType::from_str("asp"), Some(DotnetRuntimeType::AspNetCore));
    assert_eq!(DotnetRuntimeType::from_str("aspcore"), Some(DotnetRuntimeType::AspNetCore));
    assert_eq!(DotnetRuntimeType::from_str("windowsdesktop"), Some(DotnetRuntimeType::WindowsDesktop));
    assert_eq!(DotnetRuntimeType::from_str("desktop"), Some(DotnetRuntimeType::WindowsDesktop));
    assert_eq!(DotnetRuntimeType::from_str("foo"), None);
    assert_eq!(DotnetRuntimeType::from_str("RUNTIME"), Some(DotnetRuntimeType::Runtime));
    assert_eq!(DotnetRuntimeType::from_str("ASPNETCORE"), Some(DotnetRuntimeType::AspNetCore));
    assert_eq!(DotnetRuntimeType::from_str("ASP"), Some(DotnetRuntimeType::AspNetCore));
    assert_eq!(DotnetRuntimeType::from_str("ASPCORE"), Some(DotnetRuntimeType::AspNetCore));
    assert_eq!(DotnetRuntimeType::from_str("WINDOWSDESKTOP"), Some(DotnetRuntimeType::WindowsDesktop));
    assert_eq!(DotnetRuntimeType::from_str("DESKTOP"), Some(DotnetRuntimeType::WindowsDesktop));
    assert_eq!(DotnetRuntimeType::from_str("sdk"), Some(DotnetRuntimeType::Sdk));
    assert_eq!(DotnetRuntimeType::from_str("SDk"), Some(DotnetRuntimeType::Sdk));
}

#[derive(Clone, Debug)]
pub struct DotnetInfo {
    display_name: String,
    version: String,
    architecture: RuntimeArch,
    runtime_type: DotnetRuntimeType,
}

fn get_dotnet_base_path(runtime_arch: RuntimeArch, runtime_type: DotnetRuntimeType) -> Result<String> {
    let system = RuntimeArch::from_current_system();
    if system.is_none() {
        bail!("Unable to determine system architecture.");
    }

    let system = system.unwrap();

    let dotnet_path = match runtime_type {
        DotnetRuntimeType::Runtime => "shared\\Microsoft.NETCore.App",
        DotnetRuntimeType::AspNetCore => "shared\\Microsoft.AspNetCore.App",
        DotnetRuntimeType::WindowsDesktop => "shared\\Microsoft.WindowsDesktop.App",
        DotnetRuntimeType::Sdk => "sdk",
    };

    // it's easy to check if we're looking for x86 dotnet because it's always in the same place.
    if runtime_arch == RuntimeArch::X86 {
        let pf32 = super::known_path::get_program_files_x86()?;
        let join = Path::new(&pf32).join("dotnet").join(dotnet_path);
        let result = join.to_str().ok_or_else(|| anyhow!("Unable to convert path to string."))?;
        return Ok(result.to_string());
    }

    // this only works in a 64 bit process, otherwise it throws
    #[cfg(not(target_arch = "x86"))]
    let pf64 = super::known_path::get_program_files_x64()?;

    // set by WOW64 for x86 processes. https://learn.microsoft.com/windows/win32/winprog64/wow64-implementation-details
    #[cfg(target_arch = "x86")]
    let pf64 = std::env::var("ProgramW6432")?;

    if !Path::new(&pf64).exists() {
        bail!("Unable to determine ProgramFilesX64 path.");
    }

    // if on an arm64 system, looking for an x64 dotnet, it will be in a subdirectory
    if runtime_arch == RuntimeArch::X64 && system == RuntimeArch::Arm64 {
        let join = Path::new(&pf64).join("dotnet").join("x64").join(dotnet_path);
        let result = join.to_str().ok_or_else(|| anyhow!("Unable to convert path to string."))?;
        return Ok(result.to_string());
    }

    // if we're here, we are looking for x64 on an x64 system, or arm64 on an arm64 system,
    // which will always be in %pf%\dotnet
    let join = Path::new(&pf64).join("dotnet").join(dotnet_path);
    let result = join.to_str().ok_or_else(|| anyhow!("Unable to convert path to string."))?;
    return Ok(result.to_string());
}

fn list_subfolders(path: &str) -> Vec<String> {
    let mut folders = Vec::new();
    if let Ok(entries) = fs::read_dir(path) {
        for entry in entries {
            if let Ok(entry) = entry {
                if let Ok(metadata) = entry.metadata() {
                    if metadata.is_dir() {
                        if let Some(folder_name) = entry.path().file_name() {
                            if let Some(folder_name_str) = folder_name.to_str() {
                                folders.push(folder_name_str.to_string());
                            }
                        }
                    }
                }
            }
        }
    }
    folders
}

#[test]
fn test_get_dotnet_base_path() {
    let path = get_dotnet_base_path(RuntimeArch::X86, DotnetRuntimeType::Runtime).unwrap();
    assert_eq!(path, "C:\\Program Files (x86)\\dotnet\\shared\\Microsoft.NETCore.App");

    let path = get_dotnet_base_path(RuntimeArch::X64, DotnetRuntimeType::Runtime).unwrap();
    assert_eq!(path, "C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App");

    let path = get_dotnet_base_path(RuntimeArch::X86, DotnetRuntimeType::AspNetCore).unwrap();
    assert_eq!(path, "C:\\Program Files (x86)\\dotnet\\shared\\Microsoft.AspNetCore.App");

    let path = get_dotnet_base_path(RuntimeArch::X64, DotnetRuntimeType::AspNetCore).unwrap();
    assert_eq!(path, "C:\\Program Files\\dotnet\\shared\\Microsoft.AspNetCore.App");

    let path = get_dotnet_base_path(RuntimeArch::X86, DotnetRuntimeType::WindowsDesktop).unwrap();
    assert_eq!(path, "C:\\Program Files (x86)\\dotnet\\shared\\Microsoft.WindowsDesktop.App");

    let path = get_dotnet_base_path(RuntimeArch::X64, DotnetRuntimeType::WindowsDesktop).unwrap();
    assert_eq!(path, "C:\\Program Files\\dotnet\\shared\\Microsoft.WindowsDesktop.App");

    let path = get_dotnet_base_path(RuntimeArch::X64, DotnetRuntimeType::Sdk).unwrap();
    assert_eq!(path, "C:\\Program Files\\dotnet\\sdk");
}

impl RuntimeInfo for DotnetInfo {
    fn get_exe_name(&self) -> String {
        let my_arch: &'static str = self.clone().architecture.into();
        let my_type: &'static str = self.clone().runtime_type.into();
        format!("inst-dotnet-{}-{}-{}.exe", self.version, my_arch, my_type)
    }

    fn display_name(&self) -> &str {
        &self.display_name
    }

    fn get_download_url(&self) -> Result<String> {
        let (major, minor, _, _) = util::parse_version(&self.version)?;
        let latest_runtime_str = match self.runtime_type {
            DotnetRuntimeType::Runtime => "Runtime",
            DotnetRuntimeType::AspNetCore => "aspnetcore/Runtime",
            DotnetRuntimeType::WindowsDesktop => "WindowsDesktop",
            DotnetRuntimeType::Sdk => "Sdk",
        };

        let get_latest_url = format!("{DOTNET_UNCACHED_FEED}/{latest_runtime_str}/{major}.{minor}/latest.version");
        let version = download::download_url_as_string(&get_latest_url)?;
        let version = version.trim();
        let cpu_arch_str = match self.architecture {
            RuntimeArch::X86 => "x86",
            RuntimeArch::X64 => "x64",
            RuntimeArch::Arm64 => "arm64",
        };

        let download_url = match self.runtime_type {
            DotnetRuntimeType::Runtime => {
                format!("{}/Runtime/{}/dotnet-runtime-{}-win-{}.exe", DOTNET_CDN_FEED, version, version, cpu_arch_str)
            }
            DotnetRuntimeType::AspNetCore => {
                format!("{}/aspnetcore/Runtime/{}/aspnetcore-runtime-{}-win-{}.exe", DOTNET_CDN_FEED, version, version, cpu_arch_str)
            }
            DotnetRuntimeType::WindowsDesktop => {
                format!("{}/WindowsDesktop/{}/windowsdesktop-runtime-{}-win-{}.exe", DOTNET_CDN_FEED, version, version, cpu_arch_str)
            }
            DotnetRuntimeType::Sdk => {
                format!("{}/Sdk/{}/dotnet-sdk-{}-win-{}.exe", DOTNET_CDN_FEED, version, version, cpu_arch_str)
            }
        };
        Ok(download_url)
    }

    fn is_installed(&self) -> bool {
        let base_path = get_dotnet_base_path(self.architecture.clone(), self.runtime_type.clone());
        if base_path.is_err() {
            return false;
        }

        let base_path = base_path.unwrap();
        let installed_versions = list_subfolders(&base_path);
        let (my_major, my_minor, my_build, _) = util::parse_version(&self.version).unwrap();

        for i in 0..installed_versions.len() {
            let v = installed_versions.get(i).unwrap();
            let pv = util::parse_version(v);
            let (major, minor, build, _) = match pv {
                Ok(v) => v,
                Err(_) => continue,
            };
            if my_major == major && my_minor == minor && build >= my_build {
                return true;
            }
        }
        false
    }

    fn install(&self, installer_path: &str, quiet: bool) -> Result<RuntimeInstallResult> {
        def_installer_routine(installer_path, quiet)
    }
}

#[test]
fn test_dotnet_resolves_latest_version() {
    // dotnet 5.0 is EOL so 5.0.17 should always be the latest.
    assert_eq!(
        parse_dotnet_version("net5.0").unwrap().get_download_url().unwrap(),
        "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/5.0.17/windowsdesktop-runtime-5.0.17-win-x64.exe"
    );
    assert_eq!(
        parse_dotnet_version("net5.0-x64-aspnetcore").unwrap().get_download_url().unwrap(),
        "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/5.0.17/aspnetcore-runtime-5.0.17-win-x64.exe"
    );
    assert_eq!(
        parse_dotnet_version("net5.0-x64-runtime").unwrap().get_download_url().unwrap(),
        "https://builds.dotnet.microsoft.com/dotnet/Runtime/5.0.17/dotnet-runtime-5.0.17-win-x64.exe"
    );
    assert_eq!(
        parse_dotnet_version("net6.0-arm64-sdk").unwrap().get_download_url().unwrap(),
        "https://builds.dotnet.microsoft.com/dotnet/Sdk/6.0.428/dotnet-sdk-6.0.428-win-arm64.exe"
    );
}

#[test]
fn test_dotnet_detects_installed_versions() {
    assert!(parse_dotnet_version("net8-runtime").unwrap().is_installed());
    assert!(parse_dotnet_version("net8-desktop").unwrap().is_installed());
    assert!(parse_dotnet_version("net8-asp").unwrap().is_installed());
    assert!(parse_dotnet_version("net9-sdk").unwrap().is_installed());
    assert!(!parse_dotnet_version("net11").unwrap().is_installed());
}

lazy_static! {
    static ref REGEX_DOTNET: Regex =
        Regex::new(r"^net(?:coreapp)?(?<version>(?P<major>\d+)(\.(?P<minor>\d+))?(\.(?P<build>\d+))?)(?:-(?<arch>[a-zA-Z]+\d\d))?(?:-(?<type>[a-zA-Z]+))?$")
            .unwrap();
}

fn parse_dotnet_version(version: &str) -> Result<DotnetInfo> {
    let caps = REGEX_DOTNET.captures(version).ok_or_else(|| anyhow!("Invalid dotnet version string: '{}'", version))?;
    let version_str = caps.name("version").ok_or_else(|| anyhow!("Invalid dotnet version string: '{}'", version))?.as_str();
    let architecture_str = caps.name("arch").map(|m| m.as_str()).unwrap_or("x64");
    let runtime_type_str = caps.name("type").map(|m| m.as_str()).unwrap_or("desktop");

    let (major, minor, build, revision) = util::parse_version(version_str)?; // validate it's valid version string
    if major < 5 {
        bail!("Only dotnet 5 and greater is supported.");
    }
    if revision != 0 {
        bail!("Invalid dotnet version string: '{}'", version);
    }

    let architecture = RuntimeArch::from_str(architecture_str).ok_or_else(|| anyhow!("Invalid dotnet version string: '{}'", version))?;
    let runtime_type =
        DotnetRuntimeType::from_str(runtime_type_str).ok_or_else(|| anyhow!("Invalid dotnet version string: '{}'", version))?;
    let version_str = format!("{}.{}.{}", major, minor, build);
    let display_name = format!(".NET {} {:?} {:?}", version_str, architecture, runtime_type);
    Ok(DotnetInfo { display_name, version: version_str, architecture, runtime_type })
}

#[derive(Clone, Debug)]
pub struct WebView2Info {}

impl RuntimeInfo for WebView2Info {
    fn get_exe_name(&self) -> String {
        "inst-webview2-evergreen.exe".to_string()
    }

    fn display_name(&self) -> &str {
        "Microsoft Edge WebView2 Runtime"
    }

    fn get_download_url(&self) -> Result<String> {
        Ok(WEBVIEW2_EVERGREEN.to_owned())
    }

    fn is_installed(&self) -> bool {
        let result = super::webview2::get_webview_version();
        if let Some(version) = result {
            info!("WebView2 version: {}", version);
            true
        } else {
            false
        }
    }

    fn install(&self, installer_path: &str, quiet: bool) -> Result<RuntimeInstallResult> {
        let args = if quiet { vec!["/silent", "/install"] } else { vec!["/install"] };

        info!("Running installer: '{}', args={:?}", installer_path, args);
        let mut cmd = Process::new(installer_path).args(&args).spawn()?;
        let result: i32 = cmd.wait()?.code().ok_or_else(|| anyhow!("Unable to get installer exit code."))?;

        match result {
            0 => Ok(RuntimeInstallResult::InstallSuccess), // success
            _ => Err(anyhow!("Installer failed with exit code: {}", result)),
        }
    }
}

#[test]
fn test_webview2_is_installed() {
    assert!(WebView2Info {}.is_installed());
}

pub fn parse_dependency_list(list: &str) -> Vec<Box<dyn RuntimeInfo>> {
    let mut vec: Vec<Box<dyn RuntimeInfo>> = Vec::new();
    for dep in list.split(',') {
        let dep = dep.trim();
        if dep.is_empty() {
            continue;
        }
        if dep == "webview2" {
            vec.push(Box::new(WebView2Info {}));
        } else if let Some(info) = HM_NET_FX.get(dep) {
            vec.push(Box::new(info.clone()));
        } else if let Some(info) = HM_VCREDIST.get(dep) {
            vec.push(Box::new(info.clone()));
        } else if let Ok(info) = parse_dotnet_version(dep) {
            vec.push(Box::new(info));
        }
    }
    vec
}

#[test]
fn test_parse_dotnet_display_name() {
    let info = parse_dotnet_version("net5.0").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X64 WindowsDesktop");

    let info = parse_dotnet_version("net5.0-x64").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X64 WindowsDesktop");

    let info = parse_dotnet_version("net5.0-x86").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X86 WindowsDesktop");

    let info = parse_dotnet_version("net5.0-arm64").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 Arm64 WindowsDesktop");

    let info = parse_dotnet_version("net5.0-desktop").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X64 WindowsDesktop");

    let info = parse_dotnet_version("net5.0-runtime").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X64 Runtime");

    let info = parse_dotnet_version("net5.0-x86-runtime").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X86 Runtime");

    let info = parse_dotnet_version("net5.0-arm64-runtime").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 Arm64 Runtime");

    let info = parse_dotnet_version("net5.0-asp").unwrap();
    assert_eq!(info.display_name, ".NET 5.0.0 X64 AspNetCore");

    let info = parse_dotnet_version("net6.0.2").unwrap();
    assert_eq!(info.display_name, ".NET 6.0.2 X64 WindowsDesktop");
}

#[test]
fn test_parse_dotnet_version() {
    let info = parse_dotnet_version("net5.0").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net5.0-x64").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net5.0-x86").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X86);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net5.0-arm64").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::Arm64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net5.0-desktop").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net5.0-runtime").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::Runtime);

    let info = parse_dotnet_version("net5.0-x86-runtime").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X86);
    assert_eq!(info.runtime_type, DotnetRuntimeType::Runtime);

    let info = parse_dotnet_version("net5.0-arm64-runtime").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::Arm64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::Runtime);

    let info = parse_dotnet_version("net5.0-asp").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::AspNetCore);

    let info = parse_dotnet_version("net5.0-x86-asp").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::X86);
    assert_eq!(info.runtime_type, DotnetRuntimeType::AspNetCore);

    let info = parse_dotnet_version("net5.0-arm64-asp").unwrap();
    assert_eq!(info.version, "5.0.0");
    assert_eq!(info.architecture, RuntimeArch::Arm64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::AspNetCore);

    let info = parse_dotnet_version("net7.0").unwrap();
    assert_eq!(info.version, "7.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net7").unwrap();
    assert_eq!(info.version, "7.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net8").unwrap();
    assert_eq!(info.version, "8.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);

    let info = parse_dotnet_version("net8-sdk").unwrap();
    assert_eq!(info.version, "8.0.0");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::Sdk);

    let info = parse_dotnet_version("net321.321.321").unwrap();
    assert_eq!(info.version, "321.321.321");
    assert_eq!(info.architecture, RuntimeArch::X64);
    assert_eq!(info.runtime_type, DotnetRuntimeType::WindowsDesktop);
}

#[test]
fn test_parse_dotnet_version_returns_error_on_invalid_strings() {
    assert!(parse_dotnet_version("net4.0").is_err());
    assert!(parse_dotnet_version("net4.0-x86").is_err());
    assert!(parse_dotnet_version("net4.0-windowsdesktop").is_err());
    assert!(parse_dotnet_version("net4.0-windowsdesktop-x86").is_err());
    assert!(parse_dotnet_version("net4.0-aspnetcore").is_err());
    assert!(parse_dotnet_version("net4.0-aspnetcore-x86").is_err());
    assert!(parse_dotnet_version("net4.0-asp").is_err());
    assert!(parse_dotnet_version("net4.0-asp-x86").is_err());
    assert!(parse_dotnet_version("net5.0-aspnetcore-x86").is_err());
    assert!(parse_dotnet_version("net5.0-asp-x86").is_err());
    assert!(parse_dotnet_version("netcoreapp4.8").is_err());
    assert!(parse_dotnet_version("net4.8").is_err());
    assert!(parse_dotnet_version("net2.5").is_err());
    assert!(parse_dotnet_version("asd").is_err());
    assert!(parse_dotnet_version("net7.0-x64-base").is_err());
    assert!(parse_dotnet_version("net6.0.0.4").is_err());
    assert!(parse_dotnet_version("net4.9").is_err());
    assert!(parse_dotnet_version("net6-basd").is_err());
    assert!(parse_dotnet_version("net6-x64-aakak").is_err());
    assert!(parse_dotnet_version("").is_err());
}
