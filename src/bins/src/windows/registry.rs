use anyhow::{bail, Result};
use chrono::{Datelike, Local as DateTime};
use std::ffi::OsString;
use velopack::locator::VelopackLocator;
use winreg::{enums::*, RegKey};

const UNINSTALL_REGISTRY_KEY: &'static str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";

pub fn write_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    info!("Writing uninstall registry key...");

    let app_id = locator.get_manifest_id();
    let app_title = locator.get_manifest_title();
    let app_authors = locator.get_manifest_authors();

    let root_path = locator.get_root_dir();
    let main_exe_path = locator.get_main_exe_path();
    let updater_path = locator.get_update_path();

    let folder_size = fs_extra::dir::get_size(locator.get_current_bin_dir()).unwrap_or(0);
    let folder_size_kb = folder_size / 1024;
    let short_version = locator.get_manifest_version_short_string();

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
    reg_app.set_value("DisplayVersion", &short_version)?;
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
    let short_version = locator.get_manifest_version_short_string();
    let reg_path = format!("{}\\MSI:{}", UNINSTALL_REGISTRY_KEY, app_id);

    info!("Updating MSI uninstall entry for {} to version {}", app_id, short_version);

    // Try HKCU first (per-user MSI install), then HKLM (per-machine MSI install)
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);

    if let Ok(reg_app) = hkcu.open_subkey_with_flags(&reg_path, KEY_SET_VALUE) {
        info!("Updating DisplayVersion in HKCU to {}", short_version);
        reg_app.set_value("DisplayVersion", &short_version)?;
    } else if let Ok(reg_app) = hklm.open_subkey_with_flags(&reg_path, KEY_SET_VALUE) {
        info!("Updating DisplayVersion in HKLM to {}", short_version);
        reg_app.set_value("DisplayVersion", &short_version)?;
    } else {
        bail!("Could not find MSI uninstall registry key for {} in HKCU or HKLM.", app_id);
    }

    Ok(())
}
