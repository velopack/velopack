#![cfg(windows)]

mod msi;
mod validate_path;
use msi::*;

use std::{ffi::c_uint, ffi::OsString, path::PathBuf, time::Duration};
use velopack::process::{self, WaitResult};
use velopack_bins::windows::prerequisite;
use windows::Win32::{
    Foundation::{ERROR_INSTALL_USEREXIT, ERROR_SUCCESS},
    System::ApplicationInstallationAndServicing::MSIHANDLE,
    UI::WindowsAndMessaging::{MessageBoxW, MB_ICONWARNING, MB_OK, MESSAGEBOX_STYLE},
};

#[no_mangle]
pub extern "system" fn RustSetLocaleStrings(h_install: MSIHANDLE) -> c_uint {
    velopack_l18n::init();

    let app_title = msi_get_property(h_install, "RustAppTitle").unwrap_or_default();

    show_debug_message("RustSetLocaleStrings", format!("RustAppTitle={:?}", app_title));

    for (property_name, value) in velopack_l18n::msi_strings::locale_strings(&app_title) {
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

#[no_mangle]
pub extern "system" fn CleanupDeferred(h_install: MSIHANDLE) -> c_uint {
    let custom_data = msi_get_property(h_install, "CustomActionData");
    show_debug_message("CleanupDeferred", format!("CustomActionData={:?}", custom_data));

    if let Some(custom_data) = custom_data {
        // custom data will be a list delimited by " (0x22)
        let mut custom_data = custom_data.split('"');
        let install_dir = custom_data.next();
        let app_id = custom_data.next();
        let temp_dir = custom_data.next();

        show_debug_message(
            "CleanupDeferred",
            format!("install_dir={:?}, app_id={:?}, temp_dir={:?}", install_dir, app_id, temp_dir),
        );

        if let Some(install_dir) = install_dir {
            if let Err(e) = remove_dir_all::remove_dir_all(install_dir) {
                show_debug_message("CleanupDeferred", format!("Failed to remove install directory: {:?} {}", install_dir, e));
            }
        }

        if let Some(app_id) = app_id {
            if let Ok(appdata) = std::env::var("LOCALAPPDATA") {
                let velopack_app_dir = PathBuf::from(appdata).join(app_id);
                if let Err(e) = remove_dir_all::remove_dir_all(&velopack_app_dir) {
                    show_debug_message(
                        "CleanupDeferred",
                        format!("Failed to remove local app data directory: {:?} {}", velopack_app_dir, e),
                    );
                }
            }

            if let Some(temp_dir) = temp_dir {
                let temp_dir = PathBuf::from(temp_dir);
                let temp_dir = temp_dir.join(format!("velopack_{}", app_id));
                if let Err(e) = remove_dir_all::remove_dir_all(&temp_dir) {
                    show_debug_message("CleanupDeferred", format!("Failed to remove temp directory: {:?} {}", temp_dir, e));
                }
            }
        }

        show_debug_message("CleanupDeferred", "Done!".to_string());
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

#[no_mangle]
pub extern "system" fn InstallHookDeferred(h_install: MSIHANDLE) -> c_uint {
    run_hook_deferred(h_install, "--veloapp-install", 30)
}

#[no_mangle]
pub extern "system" fn UninstallHookDeferred(h_install: MSIHANDLE) -> c_uint {
    run_hook_deferred(h_install, "--veloapp-uninstall", 60)
}

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
    if std::env::var("CI").is_ok() {
        return;
    }
    let message = format!("{}: {}", fn_name, message);
    show_messagebox(fn_name, &message, MB_ICONWARNING);
}

#[cfg(not(debug_assertions))]
fn show_debug_message(_fn_name: &str, _message: String) {
    // no-op
}
