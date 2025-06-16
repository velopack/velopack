#![cfg(windows)]

mod msi;
use msi::*;

use std::{ffi::c_uint, path::PathBuf};
use velopack::process;
use velopack_bins::{dialogs, windows::prerequisite};
use windows::Win32::{
    Foundation::{ERROR_INSTALL_USEREXIT, ERROR_SUCCESS},
    System::ApplicationInstallationAndServicing::MSIHANDLE,
};

#[no_mangle]
pub extern "system" fn EarlyBootstrap(h_install: MSIHANDLE) -> c_uint {
    let dependencies = msi_get_property(h_install, "RustRuntimeDependencies");
    let app_name = msi_get_property(h_install, "RustAppTitle");
    let app_version = msi_get_property(h_install, "RustAppVersion");

    show_debug_message(
        "EarlyBootstrap",
        format!("RustRuntimeDependencies={:?} RustAppTitle={:?} RustAppVersion={:?}", dependencies, app_name, app_version),
    );

    if let Some(dependencies) = dependencies {
        let app_name = app_name.unwrap_or("Application".into());
        let app_version = app_version.unwrap_or("0.0.0".into());
        match prerequisite::prompt_and_install_all_missing(&app_name, &app_version, &dependencies, None) {
            Ok(true) => ERROR_SUCCESS.0,
            Ok(false) => ERROR_INSTALL_USEREXIT.0,
            Err(e) => {
                let title = format!("{} Setup", app_name);
                let err = format!("An error occurred: {}", e);
                dialogs::show_error(&title, Some("Setup can not continue"), &err);
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

        show_debug_message("CleanupDeferred", format!("install_dir={:?}, app_id={:?}, temp_dir={:?}", install_dir, app_id, temp_dir));

        if let Some(install_dir) = install_dir {
            if let Err(e) = remove_dir_all::remove_dir_all(install_dir) {
                show_debug_message("CleanupDeferred", format!("Failed to remove install directory: {:?} {}", install_dir, e));
            }
        }

        if let Some(app_id) = app_id {
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

    show_debug_message("LaunchApplication", format!("INSTALLFOLDER={:?}, RustStubFileName={:?}", install_dir, stub_file));

    if let Some(install_dir) = install_dir {
        if let Some(stub_file) = stub_file {
            let stub_path = PathBuf::from(&install_dir).join(stub_file);
            if let Err(e) = process::run_process(stub_path, vec![], Some(&install_dir), false, None) {
                show_debug_message("LaunchApplication", format!("Failed to launch application: {}", e));
            }
        }
    }

    ERROR_SUCCESS.0
}

#[cfg(debug_assertions)]
fn show_debug_message(fn_name: &str, message: String) {
    let message = format!("{}: {}", fn_name, message);
    dialogs::show_warn(fn_name, None, &message);
}

#[cfg(not(debug_assertions))]
fn show_debug_message(fn_name: &str, message: String) {
    // no-op
}
