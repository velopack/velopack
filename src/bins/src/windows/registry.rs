use anyhow::{bail, Result};
use chrono::{Datelike, Local as DateTime};
use std::ffi::OsString;
use std::path::Path;
use velopack::locator::VelopackLocator;
use winreg::{enums::*, RegKey};

const UNINSTALL_REGISTRY_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";

/// The scope an MSI was installed in, which decides the registry hive (HKCU vs HKLM) the install's
/// ARP entry and file/URL associations live in.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MsiScope {
    PerUser,
    PerMachine,
}

/// Directory paths compared as install roots: case-insensitive, ignoring a trailing separator (MSI's
/// `[INSTALLFOLDER]` always ends with one, Update.exe's root dir never does).
pub(crate) fn normalize_dir_path(dir: &Path) -> String {
    dir.to_string_lossy().trim_end_matches(['\\', '/']).to_lowercase()
}

fn msi_arp_subkey(app_id: &str) -> String {
    format!("MSI:{}", app_id)
}

/// Finds the scope of an MSI install from where its `MSI:{AppId}` ARP entry lives. The MSI writes that
/// key via HKMU, so it is in HKCU for a per-user and in HKLM for a per-machine install. A per-user and a
/// per-machine install of a dual-scope package can coexist; when both hives have an entry, the one whose
/// `InstallLocation` is this install's root dir wins. `None` when no entry exists in either hive.
pub fn get_msi_install_scope(locator: &VelopackLocator) -> Option<MsiScope> {
    let user = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey_with_flags(UNINSTALL_REGISTRY_KEY, KEY_READ)
        .ok();
    let machine = RegKey::predef(HKEY_LOCAL_MACHINE)
        .open_subkey_with_flags(UNINSTALL_REGISTRY_KEY, KEY_READ)
        .ok();
    find_msi_scope_in(user.as_ref(), machine.as_ref(), &locator.get_manifest_id(), &locator.get_root_dir())
}

/// The testable core of [`get_msi_install_scope`]: `user_uninstall` / `machine_uninstall` are the
/// `...\CurrentVersion\Uninstall` keys of HKCU / HKLM (`None` when a hive could not be opened).
pub fn find_msi_scope_in(user_uninstall: Option<&RegKey>, machine_uninstall: Option<&RegKey>, app_id: &str, root_dir: &Path) -> Option<MsiScope> {
    let subkey = msi_arp_subkey(app_id);
    let root = normalize_dir_path(root_dir);
    let mut first_found = None;
    for (scope, parent) in [(MsiScope::PerUser, user_uninstall), (MsiScope::PerMachine, machine_uninstall)] {
        let Some(key) = parent.and_then(|p| p.open_subkey(&subkey).ok()) else {
            continue;
        };
        let location: String = key.get_value("InstallLocation").unwrap_or_default();
        if normalize_dir_path(Path::new(&location)) == root {
            return Some(scope);
        }
        first_found.get_or_insert(scope);
    }
    first_found
}

pub fn write_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    info!("Writing uninstall registry key...");

    let app_id = locator.get_manifest_id();
    let app_title = locator.get_manifest_title();
    let app_authors = locator.get_manifest_authors();

    let root_path = locator.get_root_dir();
    let main_exe_path = locator.get_main_exe_path();
    let updater_path = locator.get_update_path();

    let folder_size = fs_extra::dir::get_size(locator.get_current_bin_dir()).unwrap_or(0);
    // Windows reads EstimatedSize as a REG_DWORD, so a u64 (REG_QWORD) is ignored and
    // no size is shown. Saturate rather than truncate: the value is in KB, so a u32
    // covers ~4TB.
    let folder_size_kb = u32::try_from(folder_size / 1024).unwrap_or(u32::MAX);
    // DisplayVersion is free-form text, so show the full semver (including prerelease
    // and build metadata) to match what the MSI writes and what the app reports.
    let display_version = locator.get_manifest_version_full_string();

    let now = DateTime::now();
    let formatted_date = format!("{}{:02}{:02}", now.year(), now.month(), now.day());

    let mut uninstall_cmd = OsString::from("\"");
    uninstall_cmd.push(&updater_path);
    uninstall_cmd.push("\" --uninstall");

    let mut uninstall_quiet = OsString::from("\"");
    uninstall_quiet.push(&updater_path);
    uninstall_quiet.push("\" --uninstall --silent");

    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let (reg_uninstall, _reg_uninstall_disp) = hkcu.create_subkey(UNINSTALL_REGISTRY_KEY)?;
    let (reg_app, _reg_app_disp) = reg_uninstall.create_subkey(&app_id)?;

    let u32true = 1u32;
    let language = 0x0409u32;

    reg_app.set_value("DisplayIcon", &main_exe_path.as_os_str())?;
    reg_app.set_value("DisplayName", &app_title)?;
    reg_app.set_value("DisplayVersion", &display_version)?;
    reg_app.set_value("InstallDate", &formatted_date)?;
    reg_app.set_value("InstallLocation", &root_path.as_os_str())?;
    reg_app.set_value("Publisher", &app_authors)?;
    reg_app.set_value("QuietUninstallString", &uninstall_quiet)?;
    reg_app.set_value("UninstallString", &uninstall_cmd)?;
    reg_app.set_value("EstimatedSize", &folder_size_kb)?;
    reg_app.set_value("NoModify", &u32true)?;
    reg_app.set_value("NoRepair", &u32true)?;
    reg_app.set_value("Language", &language)?;
    Ok(())
}

pub fn remove_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    info!("Removing uninstall registry keys...");
    let app_id = locator.get_manifest_id();
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let (reg_uninstall, _reg_uninstall_disp) = hkcu.create_subkey(UNINSTALL_REGISTRY_KEY)?;
    reg_uninstall.delete_subkey_all(&app_id)?;
    Ok(())
}

pub fn update_uninstall_entry(old_locator: &VelopackLocator, new_locator: &VelopackLocator) -> Result<()> {
    if old_locator.get_is_msi_install() {
        info!("MSI installation detected. Updating MSI registry entry.");
        if old_locator.get_manifest_id() != new_locator.get_manifest_id() {
            warn!("App ID changed for MSI install. Cannot update MSI registry reliably.");
        }
        update_msi_uninstall_entry(new_locator)
    } else {
        if old_locator.get_manifest_id() != new_locator.get_manifest_id() {
            info!("The app ID has changed, removing old uninstall registry entry.");
            if let Err(e) = remove_uninstall_entry(old_locator) {
                warn!("Failed to remove old uninstall entry ({}).", e);
            }
        }
        write_uninstall_entry(new_locator)
    }
}

pub fn update_msi_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    let app_id = locator.get_manifest_id();
    let display_version = locator.get_manifest_version_full_string();
    let reg_path = format!("{}\\{}", UNINSTALL_REGISTRY_KEY, msi_arp_subkey(&app_id));

    info!("Updating MSI uninstall entry for {} to version {}", app_id, display_version);

    let (hive, hive_name) = match get_msi_install_scope(locator) {
        Some(MsiScope::PerUser) => (HKEY_CURRENT_USER, "HKCU"),
        Some(MsiScope::PerMachine) => (HKEY_LOCAL_MACHINE, "HKLM"),
        None => bail!("Could not find MSI uninstall registry key for {} in HKCU or HKLM.", app_id),
    };
    info!("Updating DisplayVersion in {} to {}", hive_name, display_version);
    let reg_app = RegKey::predef(hive).open_subkey_with_flags(&reg_path, KEY_SET_VALUE)?;
    reg_app.set_value("DisplayVersion", &display_version)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    /// Scratch stand-ins for the HKCU and HKLM `...\Uninstall` keys, deleted on drop.
    struct ScratchUninstall {
        hkcu: RegKey,
        path: String,
        user: RegKey,
        machine: RegKey,
    }

    impl ScratchUninstall {
        fn new() -> Self {
            let hkcu = RegKey::predef(HKEY_CURRENT_USER);
            let path = format!("Software\\VelopackTests\\{}", crate::shared::random_string(16));
            let (user, _) = hkcu.create_subkey(format!("{}\\User", path)).unwrap();
            let (machine, _) = hkcu.create_subkey(format!("{}\\Machine", path)).unwrap();
            Self { hkcu, path, user, machine }
        }

        fn add_entry(&self, scope: MsiScope, app_id: &str, install_location: Option<&str>) {
            let parent = match scope {
                MsiScope::PerUser => &self.user,
                MsiScope::PerMachine => &self.machine,
            };
            let (key, _) = parent.create_subkey(msi_arp_subkey(app_id)).unwrap();
            key.set_value("DisplayName", &app_id).unwrap();
            if let Some(location) = install_location {
                key.set_value("InstallLocation", &location).unwrap();
            }
        }

        fn find(&self, app_id: &str, root: &str) -> Option<MsiScope> {
            find_msi_scope_in(Some(&self.user), Some(&self.machine), app_id, &PathBuf::from(root))
        }
    }

    impl Drop for ScratchUninstall {
        fn drop(&mut self) {
            let _ = self.hkcu.delete_subkey_all(&self.path);
        }
    }

    const APP: &str = "MyCompany.MyApp";
    const USER_ROOT: &str = "C:\\Users\\Test\\AppData\\Local\\MyApp";
    const MACHINE_ROOT: &str = "C:\\Program Files\\MyApp";

    #[test]
    fn scope_is_the_hive_holding_the_arp_entry() {
        let s = ScratchUninstall::new();
        assert_eq!(s.find(APP, USER_ROOT), None);

        s.add_entry(MsiScope::PerUser, APP, Some(&format!("{}\\", USER_ROOT)));
        assert_eq!(s.find(APP, USER_ROOT), Some(MsiScope::PerUser));
        assert_eq!(s.find("Other.App", USER_ROOT), None);

        let s = ScratchUninstall::new();
        s.add_entry(MsiScope::PerMachine, APP, Some(&format!("{}\\", MACHINE_ROOT)));
        assert_eq!(s.find(APP, MACHINE_ROOT), Some(MsiScope::PerMachine));
        // the hive is what matters when only one entry exists, even if InstallLocation does not match
        assert_eq!(s.find(APP, USER_ROOT), Some(MsiScope::PerMachine));
    }

    #[test]
    fn coexisting_installs_are_told_apart_by_install_location() {
        let s = ScratchUninstall::new();
        s.add_entry(MsiScope::PerUser, APP, Some(&format!("{}\\", USER_ROOT)));
        s.add_entry(MsiScope::PerMachine, APP, Some(&format!("{}\\", MACHINE_ROOT)));

        assert_eq!(s.find(APP, USER_ROOT), Some(MsiScope::PerUser));
        assert_eq!(s.find(APP, MACHINE_ROOT), Some(MsiScope::PerMachine));
        // comparison ignores case and trailing separators
        assert_eq!(s.find(APP, &format!("{}\\", MACHINE_ROOT.to_uppercase())), Some(MsiScope::PerMachine));
        // neither matches (eg. entries without InstallLocation): the first hive found, HKCU, wins
        assert_eq!(s.find(APP, "D:\\Elsewhere\\MyApp"), Some(MsiScope::PerUser));
    }

    #[test]
    fn unreadable_hives_are_skipped() {
        let s = ScratchUninstall::new();
        s.add_entry(MsiScope::PerMachine, APP, Some(MACHINE_ROOT));
        assert_eq!(
            find_msi_scope_in(None, Some(&s.machine), APP, Path::new(MACHINE_ROOT)),
            Some(MsiScope::PerMachine)
        );
        assert_eq!(find_msi_scope_in(Some(&s.user), None, APP, Path::new(MACHINE_ROOT)), None);
        assert_eq!(find_msi_scope_in(None, None, APP, Path::new(MACHINE_ROOT)), None);
    }
}
