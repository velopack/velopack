#![cfg(windows)]

mod channel_tag;
mod msi;
mod validate_path;
use msi::*;

use std::{
    ffi::c_uint,
    ffi::OsString,
    path::{Path, PathBuf},
    time::Duration,
};
use velopack::process::{self, WaitResult};
use velopack_bins::windows::prerequisite;
#[cfg(debug_assertions)]
use windows::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONWARNING, MB_OK, MESSAGEBOX_STYLE};
use windows::Win32::{
    Foundation::{ERROR_INSTALL_USEREXIT, ERROR_SUCCESS},
    System::ApplicationInstallationAndServicing::MSIHANDLE,
};

#[no_mangle]
pub extern "system" fn RustSetLocaleStrings(h_install: MSIHANDLE) -> c_uint {
    velopack_l18n::init();

    let app_title = msi_get_property(h_install, "RustAppTitle").unwrap_or_default();

    show_debug_message("RustSetLocaleStrings", format!("RustAppTitle={:?}", app_title));

    for (property_name, value) in velopack_l18n::msi_strings::locale_strings(&app_title) {
        // Don't overwrite properties that the WiX template already set explicitly
        // (e.g. MsiWelcomeDescription when --instWelcome was provided at pack time).
        if msi_get_property(h_install, property_name).is_some() {
            continue;
        }
        msi_set_property_string(h_install, property_name, &value);
    }

    ERROR_SUCCESS.0
}

#[no_mangle]
pub extern "system" fn ValidatePath(h_install: MSIHANDLE) -> c_uint {
    validate_path::validate_path(h_install)
}

#[no_mangle]
pub extern "system" fn EarlyBootstrap(h_install: MSIHANDLE) -> c_uint {
    velopack_l18n::init();
    velopack_l18n::init_win32_direct(); // bypass xdialog message loop and use taskdialog directly

    let dependencies = msi_get_property(h_install, "RustRuntimeDependencies");
    let app_name = msi_get_property(h_install, "RustAppTitle");
    let app_version = msi_get_property(h_install, "RustAppVersion");

    show_debug_message(
        "EarlyBootstrap",
        format!(
            "RustRuntimeDependencies={:?} RustAppTitle={:?} RustAppVersion={:?}",
            dependencies, app_name, app_version
        ),
    );

    if let Some(dependencies) = dependencies {
        let app_name = app_name.unwrap_or("Application".into());
        let app_version = app_version.unwrap_or("0.0.0".into());
        match prerequisite::prompt_and_install_all_missing(&app_name, &app_version, &dependencies, None) {
            Ok(true) => ERROR_SUCCESS.0,
            Ok(false) => ERROR_INSTALL_USEREXIT.0,
            Err(e) => {
                velopack_l18n::show_setup_error(&app_name, &e.to_string());
                ERROR_INSTALL_USEREXIT.0
            }
        }
    } else {
        ERROR_SUCCESS.0
    }
}

/// Parses the CustomActionData marshaled by SetRustCleanupData / SetUserRustCleanupData:
/// `[INSTALLFOLDER]"[RustAppId]"[TempFolder]"[LocalAppDataFolder]"[UPGRADINGPRODUCTCODE]`
/// (the last field is only present for RustCleanup, and is empty unless the product is being
/// removed as part of a major upgrade).
struct CleanupData {
    install_dir: String,
    app_id: String,
    temp_dir: String,
    local_app_data: String,
    is_upgrading: bool,
}

fn parse_cleanup_data(custom_data: &str) -> CleanupData {
    let mut parts = custom_data.split('"');
    let install_dir = parts.next().unwrap_or("").to_string();
    let mut app_id = parts.next().unwrap_or("").to_string();
    let temp_dir = parts.next().unwrap_or("").to_string();
    let local_app_data = parts.next().unwrap_or("").to_string();
    let is_upgrading = parts.next().map(|s| !s.is_empty()).unwrap_or(false);
    // app_id is joined onto profile paths and deleted recursively, so it must be a plain
    // single path component
    if app_id == "." || app_id == ".." || app_id.contains(['/', '\\', ':']) {
        app_id = String::new();
    }
    CleanupData {
        install_dir,
        app_id,
        temp_dir,
        local_app_data,
        is_upgrading,
    }
}

fn remove_dir_logged(fn_name: &str, dir: &Path) {
    if let Err(e) = remove_dir_all::remove_dir_all(dir) {
        show_debug_message(fn_name, format!("Failed to remove directory: {:?} {}", dir, e));
    }
}

fn remove_msi_arp_registry_keys(fn_name: &str, app_id: &str) {
    use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE};
    use winreg::RegKey;
    const UNINSTALL_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
    let subkey = format!("MSI:{}", app_id);
    for root in [HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE] {
        // KEY_READ on the parent is enough: RegDeleteTreeW opens the named subkey itself with
        // the rights it needs (covered by the unit test below, incl. foreign values/subkeys)
        if let Ok(uninstall) = RegKey::predef(root).open_subkey(UNINSTALL_KEY) {
            if let Err(e) = uninstall.delete_subkey_all(&subkey) {
                show_debug_message(fn_name, format!("Did not remove uninstall registry key {:?}: {}", subkey, e));
            }
        }
    }
}

/// Deferred, non-impersonated (elevated on per-machine installs). On a full uninstall this removes
/// everything left in the install dir (files from in-app updates, logs, user data — mirroring the
/// Setup.exe/Update.exe uninstall behavior) plus any leftover ARP registry key. During a major
/// upgrade it only purges the `current` payload dir so the incoming MSI lays down a clean payload
/// while files outside `current` (user data, packages) survive.
#[no_mangle]
pub extern "system" fn CleanupDeferred(h_install: MSIHANDLE) -> c_uint {
    let custom_data = msi_get_property(h_install, "CustomActionData");
    show_debug_message("CleanupDeferred", format!("CustomActionData={:?}", custom_data));

    if let Some(custom_data) = custom_data {
        let data = parse_cleanup_data(&custom_data);
        if data.install_dir.is_empty() {
            show_debug_message("CleanupDeferred", "Missing install_dir, skipping".to_string());
            return ERROR_SUCCESS.0;
        }

        let install_dir = Path::new(&data.install_dir);
        if data.is_upgrading {
            show_debug_message("CleanupDeferred", "Major upgrade in progress, only purging 'current' dir".to_string());
            remove_dir_logged("CleanupDeferred", &install_dir.join("current"));
            return ERROR_SUCCESS.0;
        }

        // the app could still be running from the install dir, which would prevent deletion
        if let Err(e) = velopack_bins::shared::force_stop_package(install_dir) {
            show_debug_message("CleanupDeferred", format!("Failed to stop running processes: {}", e));
        }

        remove_dir_logged("CleanupDeferred", install_dir);

        if !data.app_id.is_empty() {
            // remove any ARP entry left behind (e.g. values written by Update.exe, or an orphaned
            // entry from a previous side-by-side install of the same app)
            remove_msi_arp_registry_keys("CleanupDeferred", &data.app_id);
        }

        show_debug_message("CleanupDeferred", "Done!".to_string());
    }

    ERROR_SUCCESS.0
}

/// Deferred, impersonated as the installing user. Runs on full uninstall only, and cleans up the
/// per-user state which CleanupDeferred cannot reliably reach when it runs as SYSTEM on
/// per-machine installs: shortcuts pointing into the install dir, the `%LocalAppData%\{AppId}`
/// fallback dir (packages + Update.exe copied there when the install dir is not writable), the
/// velopack temp dir, and the HKCU ARP key.
#[no_mangle]
pub extern "system" fn UserCleanupDeferred(h_install: MSIHANDLE) -> c_uint {
    let custom_data = msi_get_property(h_install, "CustomActionData");
    show_debug_message("UserCleanupDeferred", format!("CustomActionData={:?}", custom_data));

    if let Some(custom_data) = custom_data {
        let data = parse_cleanup_data(&custom_data);

        if !data.install_dir.is_empty() {
            velopack_bins::windows::remove_all_shortcuts_for_root_dir(&data.install_dir);
        }

        if !data.app_id.is_empty() {
            if !data.local_app_data.is_empty() {
                remove_dir_logged("UserCleanupDeferred", &PathBuf::from(&data.local_app_data).join(&data.app_id));
            }
            if !data.temp_dir.is_empty() {
                remove_dir_logged(
                    "UserCleanupDeferred",
                    &PathBuf::from(&data.temp_dir).join(format!("velopack_{}", data.app_id)),
                );
            }
            remove_msi_arp_registry_keys("UserCleanupDeferred", &data.app_id);
        }

        show_debug_message("UserCleanupDeferred", "Done!".to_string());
    }

    ERROR_SUCCESS.0
}

#[no_mangle]
pub extern "system" fn LaunchApplication(h_install: MSIHANDLE) -> c_uint {
    let install_dir = msi_get_property(h_install, "INSTALLFOLDER");
    let stub_file = msi_get_property(h_install, "RustStubFileName");

    if let Some(install_dir) = install_dir {
        if let Some(stub_file) = stub_file {
            let stub_path = PathBuf::from(&install_dir).join(stub_file);
            show_debug_message(
                "LaunchApplication",
                format!("INSTALLFOLDER={:?}, RustStubFileName={:?}", install_dir, stub_path),
            );

            //NB: Need to start the process because the MSI starting a child process won't have any environment variables set.
            if let Err(e) = process::start_process(stub_path, vec![], Some(&install_dir), false) {
                show_debug_message("LaunchApplication", format!("Failed to launch application: {}", e));
            }
        }
    }

    ERROR_SUCCESS.0
}

fn run_hook_deferred(h_install: MSIHANDLE, hook_name: &str, timeout_secs: u64) -> c_uint {
    let custom_data = msi_get_property(h_install, "CustomActionData");
    show_debug_message(hook_name, format!("CustomActionData={:?}", custom_data));

    if let Some(custom_data) = custom_data {
        let mut parts = custom_data.split('"');
        let install_dir = parts.next().unwrap_or("");
        let main_exe = parts.next().unwrap_or("");
        let version = parts.next().unwrap_or("");

        show_debug_message(
            hook_name,
            format!("install_dir={:?}, main_exe={:?}, version={:?}", install_dir, main_exe, version),
        );

        if install_dir.is_empty() || main_exe.is_empty() {
            show_debug_message(hook_name, "Missing install_dir or main_exe, skipping hook".to_string());
            return ERROR_SUCCESS.0;
        }

        let current_dir = PathBuf::from(install_dir).join("current");
        let exe_path = current_dir.join(main_exe);

        if !exe_path.exists() {
            show_debug_message(hook_name, format!("Exe not found at {:?}, skipping hook", exe_path));
            return ERROR_SUCCESS.0;
        }

        let args: Vec<OsString> = vec![hook_name.into(), version.into()];

        match process::run_process(&exe_path, args, Some(&current_dir), false, None) {
            Ok(handle) => match process::wait_for_process_to_exit(&handle, Some(Duration::from_secs(timeout_secs))) {
                Ok(WaitResult::ExitCode(0)) => {
                    show_debug_message(hook_name, "Hook executed successfully".to_string());
                }
                Ok(WaitResult::ExitCode(code)) => {
                    show_debug_message(hook_name, format!("Hook exited with code: {}", code));
                }
                Ok(WaitResult::WaitTimeout) => {
                    let _ = process::kill_process(&handle);
                    show_debug_message(hook_name, format!("Hook timed out after {}s and was killed", timeout_secs));
                }
                Ok(WaitResult::NoWaitRequired) => {
                    show_debug_message(hook_name, "Hook exited immediately".to_string());
                }
                Err(e) => {
                    show_debug_message(hook_name, format!("Error waiting for hook: {}", e));
                }
            },
            Err(e) => {
                show_debug_message(hook_name, format!("Failed to start hook process: {}", e));
            }
        }
    }

    ERROR_SUCCESS.0
}

/// Deferred custom action: reads the channel tag from the MSI's own Authenticode signature
/// (`\x05DigitalSignature` stream of `[OriginalDatabase]`) and, if present, patches `<channel>`
/// in `{INSTALLFOLDER}\current\sq.version`. Runs on fresh install and repair — the cached MSI
/// under `C:\Windows\Installer` carries the tag, so a repair re-applies the installer's channel
/// even if the app has since switched channels via updates. Returns `ERROR_SUCCESS` always; an
/// absent/malformed tag or any error is logged and swallowed, never faulting the install.
#[no_mangle]
pub extern "system" fn PatchChannelDeferred(h_install: MSIHANDLE) -> c_uint {
    let custom_data = msi_get_property(h_install, "CustomActionData");
    show_debug_message("PatchChannelDeferred", format!("CustomActionData={:?}", custom_data));

    if let Some(custom_data) = custom_data {
        // custom data is marshaled by SetPatchChannelData as [OriginalDatabase]"[INSTALLFOLDER]
        let mut parts = custom_data.split('"');
        let original_db = parts.next().unwrap_or("");
        let install_folder = parts.next().unwrap_or("");

        show_debug_message(
            "PatchChannelDeferred",
            format!("original_db={:?}, install_folder={:?}", original_db, install_folder),
        );

        if original_db.is_empty() || install_folder.is_empty() {
            show_debug_message("PatchChannelDeferred", "Missing original_db or install_folder, skipping".to_string());
            return ERROR_SUCCESS.0;
        }

        match channel_tag::apply_msi_channel_override(Path::new(original_db), Path::new(install_folder)) {
            Ok(Some(channel)) => {
                show_debug_message("PatchChannelDeferred", format!("Patched installed channel to {:?}", channel));
            }
            Ok(None) => {
                show_debug_message("PatchChannelDeferred", "No channel tag present, nothing to do".to_string());
            }
            Err(e) => {
                show_debug_message("PatchChannelDeferred", format!("Failed to apply channel override (ignored): {:?}", e));
            }
        }
    }

    ERROR_SUCCESS.0
}

#[no_mangle]
pub extern "system" fn InstallHookDeferred(h_install: MSIHANDLE) -> c_uint {
    run_hook_deferred(h_install, "--veloapp-install", 30)
}

#[no_mangle]
pub extern "system" fn UninstallHookDeferred(h_install: MSIHANDLE) -> c_uint {
    run_hook_deferred(h_install, "--veloapp-uninstall", 60)
}

#[cfg(debug_assertions)]
fn show_messagebox(title: &str, message: &str, icon: MESSAGEBOX_STYLE) {
    use velopack::wide_strings::string_to_wide;
    let title_w = string_to_wide(title);
    let message_w = string_to_wide(message);
    unsafe {
        let _ = MessageBoxW(None, message_w.as_pcwstr(), title_w.as_pcwstr(), MB_OK | icon);
    }
}

#[cfg(debug_assertions)]
fn show_debug_message(fn_name: &str, message: String) {
    // Debug dialogs are opt-in so that automated/local test runs are not interrupted.
    // Deferred custom actions run in processes spawned by the Windows Installer service and do
    // not inherit the msiexec client's environment, so to see their dialogs this variable must
    // be set user-wide (`setx VELOPACK_WIX_DEBUG_DIALOGS 1`), or machine-wide
    // (`setx /M VELOPACK_WIX_DEBUG_DIALOGS 1`) for non-impersonated actions like CleanupDeferred.
    if std::env::var("VELOPACK_WIX_DEBUG_DIALOGS").is_err() {
        return;
    }
    let message = format!("{}: {}", fn_name, message);
    show_messagebox(fn_name, &message, MB_ICONWARNING);
}

#[cfg(not(debug_assertions))]
fn show_debug_message(_fn_name: &str, _message: String) {
    // no-op
}

#[cfg(test)]
mod tests {
    use super::*;
    use winreg::enums::HKEY_CURRENT_USER;
    use winreg::RegKey;

    #[test]
    fn removes_arp_key_with_values_and_subkeys() {
        // MSI only removes the registry values it authored; the helper must be able to delete a
        // key that still holds foreign values and nested subkeys
        let app_id = "VelopackWixArpTest";
        let path = format!("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MSI:{}", app_id);
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        {
            let (key, _) = hkcu.create_subkey(&path).unwrap();
            key.set_value("DisplayName", &"leftover").unwrap();
            let (nested, _) = key.create_subkey("Nested").unwrap();
            nested.set_value("Extra", &1u32).unwrap();
        }

        remove_msi_arp_registry_keys("test", app_id);

        assert!(hkcu.open_subkey(&path).is_err(), "ARP key should have been deleted");
    }

    #[test]
    fn parse_cleanup_data_rejects_unsafe_app_id() {
        let d = parse_cleanup_data("C:\\install\"..\\evil\"C:\\temp\"C:\\lad");
        assert!(d.app_id.is_empty());

        let d = parse_cleanup_data("C:\\install\"..\"C:\\temp\"C:\\lad\"{PRODUCT-CODE}");
        assert!(d.app_id.is_empty());
        assert!(d.is_upgrading);

        let d = parse_cleanup_data("C:\\install\"MyApp\"C:\\temp\"C:\\lad\"");
        assert_eq!(d.app_id, "MyApp");
        assert!(!d.is_upgrading);

        let d = parse_cleanup_data("C:\\install\"MyApp\"C:\\temp\"C:\\lad");
        assert_eq!(d.app_id, "MyApp");
        assert!(!d.is_upgrading);
    }
}
