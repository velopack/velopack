#![allow(dead_code)]
#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

mod statics;
use statics::*;

mod types;
use types::*;

use anyhow::{anyhow, bail};
use libc::{c_char, c_void, size_t};
use std::ffi::CString;
use velopack::{sources, Error as VelopackError, UpdateCheck, UpdateManager, VelopackApp};

/// Create a new UpdateManager instance.
/// @param urlOrPath Location of the update server or path to the local update directory.
/// @param options Optional extra configuration for update manager.
/// @param locator Override the default locator configuration (usually used for testing / mocks).
#[no_mangle]
pub extern "C" fn vpkc_new_update_manager(
    psz_url_or_path: *const c_char,
    p_options: *mut vpkc_update_options_t,
    p_locator: *mut vpkc_locator_config_t,
    p_manager: *mut *mut vpkc_update_manager_t,
) -> bool {
    wrap_error(|| {
        let update_url = c_to_string_opt(psz_url_or_path).ok_or(anyhow!("URL or path is null"))?;
        let source = sources::AutoSource::new(&update_url);
        let options = c_to_updateoptions_opt(p_options);
        let locator = c_to_velopacklocatorconfig_opt(p_locator);
        let manager = UpdateManager::new(source, options, locator)?;
        unsafe { *p_manager = UpdateManagerRawPtr::new(manager) };
        Ok(())
    })
}

/// Returns the currently installed version of the app.
#[no_mangle]
pub extern "C" fn vpkc_get_current_version(p_manager: *mut vpkc_update_manager_t, psz_version: *mut c_char, c_version: size_t) -> size_t {
    match p_manager.to_opaque_ref() {
        Some(manager) => {
            let version = manager.get_current_version_as_string();
            return_cstr(psz_version, c_version, &version)
        }
        None => 0,
    }
}

/// Returns the currently installed app id.
#[no_mangle]
pub extern "C" fn vpkc_get_app_id(p_manager: *mut vpkc_update_manager_t, psz_id: *mut c_char, c_id: size_t) -> size_t {
    match p_manager.to_opaque_ref() {
        Some(manager) => {
            let app_id = manager.get_app_id();
            return_cstr(psz_id, c_id, &app_id)
        }
        None => 0,
    }
}

/// Returns whether the app is in portable mode. On Windows this can be true or false.
/// On MacOS and Linux this will always be true.
#[no_mangle]
pub extern "C" fn vpkc_is_portable(p_manager: *mut vpkc_update_manager_t) -> bool {
    match p_manager.to_opaque_ref() {
        Some(manager) => manager.get_is_portable(),
        None => false,
    }
}

/// Returns an UpdateInfo object if there is an update downloaded which still needs to be applied.
/// You can pass the UpdateInfo object to waitExitThenApplyUpdate to apply the update.
#[no_mangle]
pub extern "C" fn vpkc_update_pending_restart(p_manager: *mut vpkc_update_manager_t, p_asset: *mut vpkc_asset_t) -> bool {
    match p_manager.to_opaque_ref() {
        Some(manager) => match manager.get_update_pending_restart() {
            Some(asset) => {
                unsafe { allocate_velopackasset(asset, p_asset) };
                true
            }
            None => false,
        },
        None => false,
    }
}

/// Checks for updates, returning None if there are none available. If there are updates available, this method will return an
/// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
#[no_mangle]
pub extern "C" fn vpkc_check_for_updates(p_manager: *mut vpkc_update_manager_t, p_update: *mut vpkc_update_info_t) -> vpkc_update_check_t {
    match p_manager.to_opaque_ref() {
        Some(manager) => match manager.check_for_updates() {
            Ok(UpdateCheck::UpdateAvailable(info)) => {
                unsafe {
                    allocate_updateinfo(info, p_update);
                }
                vpkc_update_check_t::UPDATE_AVAILABLE
            }
            Ok(UpdateCheck::RemoteIsEmpty) => vpkc_update_check_t::REMOTE_IS_EMPTY,
            Ok(UpdateCheck::NoUpdateAvailable) => vpkc_update_check_t::NO_UPDATE_AVAILABLE,
            Err(e) => {
                set_last_error(&format!("{:?}", e));
                vpkc_update_check_t::UPDATE_ERROR
            }
        },
        None => {
            set_last_error("pManager must not be null");
            vpkc_update_check_t::UPDATE_ERROR
        }
    }
}

/// Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional callback.
/// This function will acquire a global update lock so may fail if there is already another update operation in progress.
/// - If the update contains delta packages and the delta feature is enabled
///   this method will attempt to unpack and prepare them.
/// - If there is no delta update available, or there is an error preparing delta
///   packages, this method will fall back to downloading the full version of the update.
#[no_mangle]
pub extern "C" fn vpkc_download_updates(
    p_manager: *mut vpkc_update_manager_t,
    p_update: *mut vpkc_update_info_t,
    cb_progress: vpkc_progress_callback_t,
    p_user_data: *mut c_void,
) -> bool {
    wrap_error(|| {
        let manager = match p_manager.to_opaque_ref() {
            Some(manager) => manager,
            None => bail!("pManager must not be null"),
        };

        let cb_progress = cb_progress.to_option();
        let update = c_to_updateinfo_opt(p_update).ok_or(anyhow!("pUpdate must not be null"))?;

        if let Some(cb_progress) = cb_progress {
            let (progress_sender, progress_receiver) = std::sync::mpsc::channel::<i16>();
            let (completion_sender, completion_receiver) = std::sync::mpsc::channel::<std::result::Result<(), VelopackError>>();

            // Move the download_updates call into a new thread
            let manager = manager.clone();
            std::thread::spawn(move || {
                let result = manager.download_updates(&update, Some(progress_sender));
                let _ = completion_sender.send(result);
            });

            // Process progress updates on the caller's thread
            loop {
                // Try to receive progress updates without blocking
                match progress_receiver.try_recv() {
                    Ok(progress) => {
                        cb_progress(p_user_data, progress as size_t);
                    }
                    _ => {
                        // No progress updates available, sleep for a short time to avoid busy-waiting
                        std::thread::sleep(std::time::Duration::from_millis(50));
                    }
                }

                // Check if download is complete
                match completion_receiver.try_recv() {
                    Ok(result) => {
                        // Download is complete, return the result (propagating any errors)
                        result?;
                        return Ok(());
                    }
                    Err(std::sync::mpsc::TryRecvError::Empty) => {
                        // Download is still in progress, continue processing progress updates
                    }
                    Err(std::sync::mpsc::TryRecvError::Disconnected) => {
                        bail!("Download thread disconnected unexpectedly without returning a result");
                    }
                }
            }
        } else {
            manager.download_updates(&update, None)?;
            Ok(())
        }
    })
}

/// This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
/// You should then clean up any state and exit your app. The updater will apply updates and then
/// optionally restart your app. The updater will only wait for 60 seconds before giving up.
#[no_mangle]
pub extern "C" fn vpkc_wait_exit_then_apply_update(
    p_manager: *mut vpkc_update_manager_t,
    p_asset: *mut vpkc_asset_t,
    b_silent: bool,
    b_restart: bool,
    p_restart_args: *mut *mut c_char,
    c_restart_args: size_t,
) -> bool {
    wrap_error(|| {
        let manager = match p_manager.to_opaque_ref() {
            Some(manager) => manager,
            None => bail!("pManager must not be null"),
        };

        let asset = c_to_velopackasset_opt(p_asset).ok_or(anyhow!("pAsset must not be null"))?;
        let restart_args = c_to_string_array_opt(p_restart_args, c_restart_args).unwrap_or_default();
        manager.wait_exit_then_apply_updates(&asset, b_silent, b_restart, &restart_args)?;
        Ok(())
    })
}

/// Frees a vpkc_update_manager_t instance.
#[no_mangle]
pub extern "C" fn vpkc_free_update_manager(p_manager: *mut vpkc_update_manager_t) {
    UpdateManagerRawPtr::free(p_manager);
}

/// Frees a vpkc_update_info_t instance.
#[no_mangle]
pub extern "C" fn vpkc_free_update_info(p_update_info: *mut vpkc_update_info_t) {
    unsafe { free_updateinfo(p_update_info) };
}

/// Frees a vpkc_asset_t instance.
#[no_mangle]
pub extern "C" fn vpkc_free_asset(p_asset: *mut vpkc_asset_t) {
    unsafe { free_velopackasset(p_asset) };
}

/// VelopackApp helps you to handle app activation events correctly.
/// This should be used as early as possible in your application startup code.
/// (eg. the beginning of main() or wherever your entry point is)
#[no_mangle]
pub extern "C" fn vpkc_app_run(p_user_data: *mut c_void) {
    let app_options = VELOPACK_APP.read().unwrap();
    let mut app = VelopackApp::build();

    if let Some(auto_apply) = app_options.auto_apply {
        app = app.set_auto_apply_on_startup(auto_apply);
    }

    if let Some(args) = &app_options.args {
        app = app.set_args(args.clone());
    }

    if let Some(locator) = &app_options.locator {
        app = app.set_locator(locator.clone());
    }

    #[cfg(windows)]
    if let Some(hook) = &app_options.install_hook {
        app = app.on_after_install_fast_callback(|version| {
            let c_string = CString::new(version.to_string()).unwrap();
            hook(p_user_data, c_string.as_ptr());
        });
    }

    #[cfg(windows)]
    if let Some(hook) = &app_options.uninstall_hook {
        app = app.on_before_uninstall_fast_callback(|version| {
            let c_string = CString::new(version.to_string()).unwrap();
            hook(p_user_data, c_string.as_ptr());
        });
    }

    #[cfg(windows)]
    if let Some(hook) = &app_options.obsolete_hook {
        app = app.on_before_update_fast_callback(|version| {
            let c_string = CString::new(version.to_string()).unwrap();
            hook(p_user_data, c_string.as_ptr());
        });
    }

    #[cfg(windows)]
    if let Some(hook) = &app_options.update_hook {
        app = app.on_after_update_fast_callback(|version| {
            let c_string = CString::new(version.to_string()).unwrap();
            hook(p_user_data, c_string.as_ptr());
        });
    }

    if let Some(hook) = &app_options.firstrun_hook {
        app = app.on_first_run(|version| {
            let c_string = CString::new(version.to_string()).unwrap();
            hook(p_user_data, c_string.as_ptr());
        });
    }

    if let Some(hook) = &app_options.restarted_hook {
        app = app.on_restarted(|version| {
            let c_string = CString::new(version.to_string()).unwrap();
            hook(p_user_data, c_string.as_ptr());
        });
    }

    app.run();
}

/// Set whether to automatically apply downloaded updates on startup. This is ON by default.
#[no_mangle]
pub extern "C" fn vpkc_app_set_auto_apply_on_startup(b_auto_apply: bool) {
    update_app_options(|opt| {
        opt.auto_apply = Some(b_auto_apply);
    });
}

/// Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
#[no_mangle]
pub extern "C" fn vpkc_app_set_args(p_args: *mut *mut c_char, c_args: size_t) {
    update_app_options(|opt| {
        opt.args = c_to_string_array_opt(p_args, c_args);
    });
}

/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
#[no_mangle]
pub extern "C" fn vpkc_app_set_locator(p_locator: *mut vpkc_locator_config_t) {
    update_app_options(|opt| {
        opt.locator = c_to_velopacklocatorconfig_opt(p_locator);
    });
}

/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_after_install(cb_after_install: vpkc_hook_callback_t) {
    let cb_after_install = cb_after_install.to_option();
    update_app_options(|opt| {
        opt.install_hook = cb_after_install;
    });
}

/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_before_uninstall(cb_before_uninstall: vpkc_hook_callback_t) {
    let cb_before_uninstall = cb_before_uninstall.to_option();
    update_app_options(|opt| {
        opt.uninstall_hook = cb_before_uninstall;
    });
}

/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_before_update(cb_before_update: vpkc_hook_callback_t) {
    let cb_before_update = cb_before_update.to_option();
    update_app_options(|opt| {
        opt.obsolete_hook = cb_before_update;
    });
}

/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_after_update(cb_after_update: vpkc_hook_callback_t) {
    let cb_after_update = cb_after_update.to_option();
    update_app_options(|opt| {
        opt.update_hook = cb_after_update;
    });
}

/// This hook is triggered when the application is started for the first time after installation.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_first_run(cb_first_run: vpkc_hook_callback_t) {
    let cb_first_run = cb_first_run.to_option();
    update_app_options(|opt| {
        opt.firstrun_hook = cb_first_run;
    });
}

/// This hook is triggered when the application is restarted by Velopack after installing updates.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_restarted(cb_restarted: vpkc_hook_callback_t) {
    let cb_restarted = cb_restarted.to_option();
    update_app_options(|opt| {
        opt.restarted_hook = cb_restarted;
    });
}

/// Get the last error message that occurred in the Velopack library.
#[no_mangle]
pub extern "C" fn vpkc_get_last_error(psz_error: *mut c_char, c_error: size_t) -> size_t {
    let error = get_last_error();
    return_cstr(psz_error, c_error, &error)
}

/// Set a custom log callback. This will be called for all log messages generated by the Velopack library.
#[no_mangle]
pub extern "C" fn vpkc_set_logger(cb_log: vpkc_log_callback_t, p_user_data: *mut c_void) {
    let cb_log = cb_log.to_option();
    if let Some(cb_log) = cb_log {
        set_log_callback(cb_log, p_user_data);
    } else {
        clear_log_callback();
    }
}
