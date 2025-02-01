use anyhow::{anyhow, Result};
use chrono::{Datelike, Local as DateTime};
use velopack::locator::VelopackLocator;
use winsafe::{self as w, co, prelude::*};
use std::collections::HashSet;

const UNINSTALL_REGISTRY_KEY: &'static str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
const SOFTWARE_CLASSES_REGISTRY_KEY: &'static str = "Software\\Classes";
// Sub key for setting the command line that the shell invokes
// https://learn.microsoft.com/en-us/windows/win32/com/shell
const SHELL_OPEN_COMMAND_KEY: &'static str = "shell\\open\\command";

pub fn create_or_update_custom_protocols(next_app: &VelopackLocator, previous_app: Option<&VelopackLocator>) -> Result<()> {
    info!("Writing custom protocol registry keys...");
    let prev_custom_url_protocols = previous_app.map(|a| a.get_custom_url_protocols()).unwrap_or(Vec::<String>::new());
    let next_custom_url_protocols = next_app.get_custom_url_protocols();

    if prev_custom_url_protocols.is_empty() && next_custom_url_protocols.is_empty() {
        return Ok(());
    }
    if prev_custom_url_protocols == next_custom_url_protocols {
        return Ok(());
    }

    let mut removable_protocols = prev_custom_url_protocols;
    let mut new_protocols = next_custom_url_protocols;

    // Do the following only if both are not empty, if one is empty then we're either removing all of one or adding all of one:
    if !removable_protocols.is_empty() && !new_protocols.is_empty() {
        let prev_protocols: HashSet<_> = removable_protocols.into_iter().collect();
        let next_protocols: HashSet<_> = new_protocols.into_iter().collect();

        // Get only the old protocols that needs to be removed from the previous app:
        removable_protocols = prev_protocols.difference(&next_protocols).cloned().collect();
        // Get only the new protocols from the next app that were not part of the previosu app:
        new_protocols = next_protocols.difference(&prev_protocols).cloned().collect();
    }

    // Open registry key to Software/Classes, where the app's custom URL protocols will be stored:
    let reg_software_classes_key =
        w::HKEY::CURRENT_USER.RegCreateKeyEx(SOFTWARE_CLASSES_REGISTRY_KEY, None, co::REG_OPTION::NoValue, co::KEY::CREATE_SUB_KEY, None).map_err(|e| anyhow!("Failed to create/open registry key: {}", e))?.0;
    
    // Remove unused protocols
    let _ = remove_custom_protocols(&reg_software_classes_key, &removable_protocols);

    // Create new protocols
    let _ = register_custom_protocols(next_app, &reg_software_classes_key, &new_protocols);

    Ok(())
}

fn remove_custom_protocols(reg_software_classes_key: &w::HKEY, protocols: &Vec<String>) -> Result<()> {
    if protocols.is_empty() {
        return Ok(());
    }

    for protocol_name in protocols {
        // let reg_protocol_key = reg_software_classes_key.RegCreateKeyEx(&protocol_name, None, co::REG_OPTION::NoValue, co::KEY::ALL_ACCESS, None).map_err(|e| anyhow!("Failed to open key for protocol {}: {}", &protocol_name, e))?;
        // reg_protocol_key.delete_key_recursive().map_err(|e| anyhow!("Failed to recursively delete protocol {}: {}", &protocol_name, e))?;
        reg_software_classes_key.RegDeleteTree(Some(&protocol_name)).map_err(|e| anyhow!("Failed to recursively delete protocol {}: {}", &protocol_name, e))?;
    }
    Ok(())
}

fn register_custom_protocols(locator: &VelopackLocator, reg_software_classes_key: &w::HKEY, new_protocols: &Vec<String>) -> Result<()> {
    if new_protocols.is_empty() {
        return Ok(());
    }

    let main_exe_path = locator.get_main_exe_path_as_string();
    let app_shell_open_cmd = format!("\"{}\" \"%1\"", main_exe_path);

    for protocol_name in new_protocols {
        let _ = register_single_protocol(&reg_software_classes_key, &protocol_name, &app_shell_open_cmd);
    }
    Ok(())
}

fn register_single_protocol(reg_software_classes_key: &w::HKEY, protocol_name: &String, app_shell_open_cmd: &String) -> Result<()> {
    // Create registry key for the protocol
    let reg_protocol_sub_key = reg_software_classes_key.RegCreateKeyEx(&protocol_name, None, co::REG_OPTION::NoValue, co::KEY::ALL_ACCESS, None).map_err(|e| anyhow!("Failed to create subkey for protocol {}: {}", &protocol_name, e))?.0;

    // This seems like standard practice, to add these values for URL protocols.
    reg_protocol_sub_key.RegSetKeyValue(None, Some(""), w::RegistryValue::Sz(format!("URL:{} protocol", &protocol_name)))?;
    reg_protocol_sub_key.RegSetKeyValue(None, Some("URL Protocol"), w::RegistryValue::Sz(String::new()))?;

    // Create registry key for the shell open command
    let reg_protocol_open_sub_key
        = reg_protocol_sub_key.RegCreateKeyEx(SHELL_OPEN_COMMAND_KEY, None, co::REG_OPTION::NoValue, co::KEY::ALL_ACCESS, None).map_err(|e| anyhow!("Failed to create shell open command subkey for protocol {}: {}", &protocol_name, e))?.0;

    // Set value
    reg_protocol_open_sub_key.RegSetKeyValue(None, Some(""), w::RegistryValue::Sz(app_shell_open_cmd.to_string())).map_err(|e| anyhow!("Failed to set shell open command for protocol {}: {}", &protocol_name, e))?;
    Ok(())
}

pub fn remove_all_custom_protocols(locator: &VelopackLocator) -> Result<()> {
    info!("Removing custom protocols registry keys...");
    // Open registry key to Software/Classes, where the app's custom URL protocols will be stored:
    let reg_software_classes_key =
        w::HKEY::CURRENT_USER.RegCreateKeyEx(SOFTWARE_CLASSES_REGISTRY_KEY, None, co::REG_OPTION::NoValue, co::KEY::CREATE_SUB_KEY, None).map_err(|e| anyhow!("Failed to create/open registry key: {}", e))?.0;

    let protocols = locator.get_custom_url_protocols();
    let _ = remove_custom_protocols(&reg_software_classes_key, &protocols);

    Ok(())
}

pub fn write_uninstall_entry(locator: &VelopackLocator) -> Result<()> {
    info!("Writing uninstall registry key...");

    let app_id = locator.get_manifest_id();
    let app_title = locator.get_manifest_title();
    let app_authors = locator.get_manifest_authors();

    let root_path_str = locator.get_root_dir_as_string();
    let main_exe_path = locator.get_main_exe_path_as_string();
    let updater_path = locator.get_update_path_as_string();

    let folder_size = fs_extra::dir::get_size(locator.get_root_dir()).unwrap_or(0);
    let short_version = locator.get_manifest_version_short_string();

    let now = DateTime::now();
    let formatted_date = format!("{}{:02}{:02}", now.year(), now.month(), now.day());

    let uninstall_cmd = format!("\"{}\" --uninstall", updater_path);
    let uninstall_quiet = format!("\"{}\" --uninstall --silent", updater_path);

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