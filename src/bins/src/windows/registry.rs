use anyhow::Result;
use chrono::{Datelike, Local as DateTime};
use velopack::locator::VelopackLocator;
use winsafe::{self as w, co, prelude::*};

const UNINSTALL_REGISTRY_KEY: &'static str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";

pub fn write_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    info!("Writing uninstall registry key...");

    let app_id = locator.get_manifest_id();
    let app_title = locator.get_manifest_title();
    let app_authors = locator.get_manifest_authors();

    let root_path_str = locator.get_root_dir_as_string();
    let main_exe_path = locator.get_main_exe_path_as_string();
    let updater_path = locator.get_update_path_as_string();

    let folder_size = fs_extra::dir::get_size(locator.get_current_bin_dir()).unwrap_or(0);
    let short_version = locator.get_manifest_version_short_string();

    let now = DateTime::now();
    let formatted_date = format!("{}{:02}{:02}", now.year(), now.month(), now.day());

    let uninstall_cmd = format!("\"{}\" --uninstall", updater_path);
    let uninstall_quiet: String = format!("\"{}\" --uninstall --silent", updater_path);

    let reg_uninstall =
        w::HKEY::CURRENT_USER.RegCreateKeyEx(UNINSTALL_REGISTRY_KEY, None, co::REG_OPTION::NoValue, co::KEY::CREATE_SUB_KEY, None)?.0;
    let reg_app = reg_uninstall.RegCreateKeyEx(&app_id, None, co::REG_OPTION::NoValue, co::KEY::ALL_ACCESS, None)?.0;
    reg_app.RegSetKeyValue(None, Some("DisplayIcon"), w::RegistryValue::Sz(main_exe_path))?;
    reg_app.RegSetKeyValue(None, Some("DisplayName"), w::RegistryValue::Sz(app_title))?;
    reg_app.RegSetKeyValue(None, Some("DisplayVersion"), w::RegistryValue::Sz(short_version))?;
    reg_app.RegSetKeyValue(None, Some("InstallDate"), w::RegistryValue::Sz(formatted_date))?;
    reg_app.RegSetKeyValue(None, Some("InstallLocation"), w::RegistryValue::Sz(root_path_str))?;
    reg_app.RegSetKeyValue(None, Some("Publisher"), w::RegistryValue::Sz(app_authors))?;
    reg_app.RegSetKeyValue(None, Some("QuietUninstallString"), w::RegistryValue::Sz(uninstall_quiet))?;
    reg_app.RegSetKeyValue(None, Some("UninstallString"), w::RegistryValue::Sz(uninstall_cmd))?;
    reg_app.RegSetKeyValue(None, Some("EstimatedSize"), w::RegistryValue::Dword((folder_size / 1024).try_into()?))?;
    reg_app.RegSetKeyValue(None, Some("NoModify"), w::RegistryValue::Dword(1))?;
    reg_app.RegSetKeyValue(None, Some("NoRepair"), w::RegistryValue::Dword(1))?;
    reg_app.RegSetKeyValue(None, Some("Language"), w::RegistryValue::Dword(0x0409))?;
    Ok(())
}

pub fn remove_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    info!("Removing uninstall registry keys...");
    let app_id = locator.get_manifest_id();
    let reg_uninstall =
        w::HKEY::CURRENT_USER.RegCreateKeyEx(UNINSTALL_REGISTRY_KEY, None, co::REG_OPTION::NoValue, co::KEY::CREATE_SUB_KEY, None)?.0;
    reg_uninstall.RegDeleteKey(&app_id)?;
    Ok(())
}