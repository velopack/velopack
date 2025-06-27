#![allow(dead_code)]
#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

mod statics;
use statics::*;
mod types;
use types::*;
mod csource;
use csource::*;
mod raw;
use raw::*;

use anyhow::{anyhow, bail};
use libc::{c_char, c_void, size_t};
use log_derive::{logfn, logfn_inputs};
use std::{ffi::CString, ptr};
use velopack::locator::LocationContext;
use velopack::logging::{default_logfile_path, init_logging};
use velopack::{sources, ApplyWaitMode, Error as VelopackError, UpdateCheck, UpdateManager, VelopackApp};

/// Create a new FileSource update source for a given file path.
/// @param psz_file_path The path to a local directory containing updates.
/// @returns A new vpkc_update_source_t instance, or null on error.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_new_source_file(psz_file_path: *const c_char) -> *mut vpkc_update_source_t {
    if let Some(update_path) = c_to_String(psz_file_path).ok() {
        UpdateSourceRawPtr::new(Box::new(sources::FileSource::new(update_path)))
    } else {
        log::error!("psz_file_path is null");
        ptr::null_mut()
    }
}

/// Create a new HttpSource update source for a given HTTP URL.
/// @param psz_http_url The URL to a remote update server.
/// @returns A new vpkc_update_source_t instance, or null on error.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_new_source_http_url(psz_http_url: *const c_char) -> *mut vpkc_update_source_t {
    if let Some(update_url) = c_to_String(psz_http_url).ok() {
        UpdateSourceRawPtr::new(Box::new(sources::HttpSource::new(update_url)))
    } else {
        log::error!("psz_http_url is null");
        ptr::null_mut()
    }
}

/// Create a new _CUSTOM_ update source with user-provided callbacks to fetch release feeds and download assets.
/// You can report download progress using `vpkc_source_report_progress`. Note that the callbacks must be valid
/// for the lifetime of any UpdateManager's that use this source. You should call `vpkc_free_source` to free the source,
/// but note that if the source is still in use by an UpdateManager, it will not be freed until the UpdateManager is freed.
/// Therefore to avoid possible issues, it is recommended to create this type of source once for the lifetime of your application.
/// @param cb_release_feed A callback to fetch the release feed.
/// @param cb_free_release_feed A callback to free the memory allocated by `cb_release_feed`.
/// @param cb_download_entry A callback to download an asset.
/// @param p_user_data Optional user data to be passed to the callbacks.
/// @returns A new vpkc_update_source_t instance, or null on error.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_new_source_custom_callback(
    cb_release_feed: vpkc_release_feed_delegate_t,
    cb_free_release_feed: vpkc_free_release_feed_t,
    cb_download_entry: vpkc_download_asset_delegate_t,
    p_user_data: *mut c_void,
) -> *mut vpkc_update_source_t {
    if cb_release_feed.is_none() || cb_download_entry.is_none() || cb_free_release_feed.is_none() {
        log::error!("cb_release_feed, cb_download_entry, or cb_free_release_feed is null");
        return ptr::null_mut();
    }

    let source = CCallbackUpdateSource {
        p_user_data,
        cb_get_release_feed: cb_release_feed,
        cb_download_release_entry: cb_download_entry,
        cb_free_release_feed: cb_free_release_feed,
    };

    UpdateSourceRawPtr::new(Box::new(source))
}

/// Sends a progress update to the callback with the specified ID. This is used by custom
/// update sources created with `vpkc_new_source_custom_callback` to report download progress.
/// @param progress_callback_id The ID of the progress callback to send the update to.
/// @param progress The progress value to send (0-100).
#[no_mangle]
pub extern "C" fn vpkc_source_report_progress(progress_callback_id: size_t, progress: i16) {
    report_csource_progress(progress_callback_id, progress);
}

/// Frees a vpkc_update_source_t instance.
/// @param p_source The source to free.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_free_source(p_source: *mut vpkc_update_source_t) {
    UpdateSourceRawPtr::free(p_source);
}

/// Create a new UpdateManager instance.
/// @param psz_url_or_path Location of the http update server url or path to the local update directory.
/// @param p_options Optional extra configuration for update manager.
/// @param p_locator Optional explicit path configuration for Velopack. If null, the default locator will be used.
/// @param p_manager A pointer to where the new vpkc_update_manager_t* instance will be stored.
/// @returns True if the update manager was created successfully, false otherwise. If false, the error will be available via `vpkc_get_last_error`.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_new_update_manager(
    psz_url_or_path: *const c_char,
    p_options: *mut vpkc_update_options_t,
    p_locator: *mut vpkc_locator_config_t,
    p_manager: *mut *mut vpkc_update_manager_t,
) -> bool {
    wrap_error(|| {
        let update_url = c_to_String(psz_url_or_path)?;
        let source = sources::AutoSource::new(&update_url);
        let options = c_to_UpdateOptions(p_options).ok();
        let locator = c_to_VelopackLocatorConfig(p_locator).ok();
        let manager = UpdateManager::new(source, options, locator)?;
        unsafe { *p_manager = UpdateManagerRawPtr::new(manager) };
        Ok(())
    })
}

/// Create a new UpdateManager instance with a custom UpdateSource.
/// @param p_source A pointer to a custom UpdateSource.
/// @param p_options Optional extra configuration for update manager.
/// @param p_locator Optional explicit path configuration for Velopack. If null, the default locator will be used.
/// @param p_manager A pointer to where the new vpkc_update_manager_t* instance will be stored.
/// @returns True if the update manager was created successfully, false otherwise. If false, the error will be available via `vpkc_get_last_error`.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_new_update_manager_with_source(
    p_source: *mut vpkc_update_source_t,
    p_options: *mut vpkc_update_options_t,
    p_locator: *mut vpkc_locator_config_t,
    p_manager: *mut *mut vpkc_update_manager_t,
) -> bool {
    wrap_error(|| {
        let source = UpdateSourceRawPtr::get_source_clone(p_source).ok_or(anyhow!("pSource must not be null"))?;
        let options = c_to_UpdateOptions(p_options).ok();
        let locator = c_to_VelopackLocatorConfig(p_locator).ok();
        let manager = UpdateManager::new_boxed(source, options, locator)?;
        unsafe { *p_manager = UpdateManagerRawPtr::new(manager) };
        Ok(())
    })
}

/// Returns the currently installed version of the app.
/// @param p_manager The update manager instance.
/// @param psz_version A buffer to store the version string.
/// @param c_version The size of the `psz_version` buffer.
/// @returns The number of characters written to `psz_version` (including null terminator), or the required buffer size if the buffer is too small.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
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
/// @param p_manager The update manager instance.
/// @param psz_id A buffer to store the app id string.
/// @param c_id The size of the `psz_id` buffer.
/// @returns The number of characters written to `psz_id` (including null terminator), or the required buffer size if the buffer is too small.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
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
/// @param p_manager The update manager instance.
/// @returns True if the app is in portable mode, false otherwise.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_is_portable(p_manager: *mut vpkc_update_manager_t) -> bool {
    match p_manager.to_opaque_ref() {
        Some(manager) => manager.get_is_portable(),
        None => false,
    }
}

/// Returns an asset if there is an update downloaded which still needs to be applied.
/// You can pass this asset to `vpkc_wait_exit_then_apply_updates` to apply the update.
/// @param p_manager The update manager instance.
/// @param p_asset A pointer to where the new vpkc_asset_t* instance will be stored.
/// @returns True if there is an update pending restart, false otherwise.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_update_pending_restart(p_manager: *mut vpkc_update_manager_t, p_asset: *mut *mut vpkc_asset_t) -> bool {
    match p_manager.to_opaque_ref() {
        Some(manager) => match manager.get_update_pending_restart() {
            Some(asset) => {
                unsafe { *p_asset = allocate_VelopackAsset(&asset) };
                true
            }
            None => false,
        },
        None => false,
    }
}

/// Checks for updates. If there are updates available, this method will return an
/// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
/// @param p_manager The update manager instance.
/// @param p_update A pointer to where the new vpkc_update_info_t* instance will be stored if an update is available.
/// @returns A `vpkc_update_check_t` value indicating the result of the check. If an update is available, the value will be `HasUpdate` and `p_update` will be populated.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_check_for_updates(
    p_manager: *mut vpkc_update_manager_t,
    p_update: *mut *mut vpkc_update_info_t,
) -> vpkc_update_check_t {
    match p_manager.to_opaque_ref() {
        Some(manager) => match manager.check_for_updates() {
            Ok(UpdateCheck::UpdateAvailable(info)) => {
                unsafe { *p_update = allocate_UpdateInfo(&info) };
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
/// @param p_manager The update manager instance.
/// @param p_update The update info object from `vpkc_check_for_updates`.
/// @param cb_progress An optional callback to report download progress (0-100).
/// @param p_user_data Optional user data to be passed to the progress callback.
/// @returns true on success, false on failure. If false, the error will be available via `vpkc_get_last_error`.

#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
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

        let update = c_to_UpdateInfo(p_update)?;

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
/// (if specified) restart your app. The updater will only wait for 60 seconds before giving up.
/// @param p_manager The update manager instance.
/// @param p_asset The asset to apply. This can be from `vpkc_update_pending_restart` or `vpkc_update_info_get_target_asset`.
/// @param b_silent True to attempt to apply the update without showing any UI.
/// @param b_restart True to restart the app after the update is applied.
/// @param p_restart_args An array of command line arguments to pass to the new process when it's restarted.
/// @param c_restart_args The number of arguments in `p_restart_args`.
/// @returns true on success, false on failure. If false, the error will be available via `vpkc_get_last_error`.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_wait_exit_then_apply_updates(
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

        let asset = c_to_VelopackAsset(p_asset)?;
        let restart_args = c_to_String_vec(p_restart_args, c_restart_args)?;
        manager.wait_exit_then_apply_updates(&asset, b_silent, b_restart, &restart_args)?;
        Ok(())
    })
}

/// This will launch the Velopack updater and optionally wait for a program to exit gracefully.
/// This method is unsafe because it does not necessarily wait for any / the correct process to exit
/// before applying updates. The `vpkc_wait_exit_then_apply_updates` method is recommended for most use cases.
/// If dw_wait_pid is 0, the updater will not wait for any process to exit before applying updates (Not Recommended).
/// @param p_manager The update manager instance.
/// @param p_asset The asset to apply. This can be from `vpkc_update_pending_restart` or `vpkc_update_info_get_target_asset`.
/// @param b_silent True to attempt to apply the update without showing any UI.
/// @param dw_wait_pid The process ID to wait for before applying updates. If 0, the updater will not wait.
/// @param b_restart True to restart the app after the update is applied.
/// @param p_restart_args An array of command line arguments to pass to the new process when it's restarted.
/// @param c_restart_args The number of arguments in `p_restart_args`.
/// @returns true on success, false on failure. If false, the error will be available via `vpkc_get_last_error`.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_unsafe_apply_updates(
    p_manager: *mut vpkc_update_manager_t,
    p_asset: *mut vpkc_asset_t,
    b_silent: bool,
    dw_wait_pid: u32,
    b_restart: bool,
    p_restart_args: *mut *mut c_char,
    c_restart_args: size_t,
) -> bool {
    wrap_error(|| {
        let manager = p_manager.to_opaque_ref().ok_or(anyhow!("pManager must not be null"))?;
        let asset = c_to_VelopackAsset(p_asset)?;
        let restart_args = c_to_String_vec(p_restart_args, c_restart_args)?;
        let wait_mode = if dw_wait_pid > 0 { ApplyWaitMode::WaitPid(dw_wait_pid) } else { ApplyWaitMode::NoWait };
        manager.unsafe_apply_updates(&asset, b_silent, wait_mode, b_restart, &restart_args)?;
        Ok(())
    })
}

/// Frees a vpkc_update_manager_t instance.
/// @param p_manager The update manager instance to free.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_free_update_manager(p_manager: *mut vpkc_update_manager_t) {
    UpdateManagerRawPtr::free(p_manager);
}

/// Frees a vpkc_update_info_t instance.
/// @param p_update_info The update info instance to free.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_free_update_info(p_update_info: *mut vpkc_update_info_t) {
    unsafe { free_UpdateInfo(p_update_info) };
}

/// Frees a vpkc_asset_t instance.
/// @param p_asset The asset instance to free.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
pub extern "C" fn vpkc_free_asset(p_asset: *mut vpkc_asset_t) {
    unsafe { free_VelopackAsset(p_asset) };
}

/// VelopackApp helps you to handle app activation events correctly.
/// This should be used as early as possible in your application startup code.
/// (eg. the beginning of main() or wherever your entry point is).
/// This function will not return in some cases.
/// @param p_user_data Optional user data to be passed to the callbacks.
#[no_mangle]
#[logfn(Trace)]
#[logfn_inputs(Trace)]
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

    // init logging
    let log_file = if let Some(locator) = &app_options.locator {
        default_logfile_path(locator)
    } else {
        default_logfile_path(LocationContext::FromCurrentExe)
    };

    init_logging("lib-cpp", Some(&log_file), false, false, Some(create_shared_logger()));
    app.run();
}

/// Set whether to automatically apply downloaded updates on startup. This is ON by default.
/// @param b_auto_apply True to automatically apply updates, false otherwise.
#[no_mangle]
pub extern "C" fn vpkc_app_set_auto_apply_on_startup(b_auto_apply: bool) {
    update_app_options(|opt| {
        opt.auto_apply = Some(b_auto_apply);
    });
}

/// Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
/// @param p_args An array of command line arguments.
/// @param c_args The number of arguments in `p_args`.
#[no_mangle]
pub extern "C" fn vpkc_app_set_args(p_args: *mut *mut c_char, c_args: size_t) {
    update_app_options(|opt| {
        opt.args = c_to_String_vec(p_args, c_args).ok();
    });
}

/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
/// @param p_locator The locator configuration to use.
#[no_mangle]
pub extern "C" fn vpkc_app_set_locator(p_locator: *mut vpkc_locator_config_t) {
    update_app_options(|opt| {
        opt.locator = c_to_VelopackLocatorConfig(p_locator).ok();
    });
}

/// Sets a callback to be run after the app is installed.
/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
/// @param cb_after_install The callback to run after the app is installed. The callback takes a user data pointer and the version of the app as a string.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_after_install(cb_after_install: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.install_hook = cb_after_install;
    });
}

/// Sets a callback to be run before the app is uninstalled.
/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
/// @param cb_before_uninstall The callback to run before the app is uninstalled. The callback takes a user data pointer and the version of the app as a string.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_before_uninstall(cb_before_uninstall: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.uninstall_hook = cb_before_uninstall;
    });
}

/// Sets a callback to be run before the app is updated.
/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
/// @param cb_before_update The callback to run before the app is updated. The callback takes a user data pointer and the version of the app as a string.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_before_update(cb_before_update: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.obsolete_hook = cb_before_update;
    });
}

/// Sets a callback to be run after the app is updated.
/// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
/// Your code will be run and then the process will exit.
/// If your code has not completed within 30 seconds, it will be terminated.
/// Only supported on windows; On other operating systems, this will never be called.
/// @param cb_after_update The callback to run after the app is updated. The callback takes a user data pointer and the version of the app as a string.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_after_update(cb_after_update: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.update_hook = cb_after_update;
    });
}

/// This hook is triggered when the application is started for the first time after installation.
/// @param cb_first_run The callback to run on first run. The callback takes a user data pointer and the version of the app as a string.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_first_run(cb_first_run: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.firstrun_hook = cb_first_run;
    });
}

/// This hook is triggered when the application is restarted by Velopack after installing updates.
/// @param cb_restarted The callback to run after the app is restarted. The callback takes a user data pointer and the version of the app as a string.
#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_restarted(cb_restarted: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.restarted_hook = cb_restarted;
    });
}

/// Get the last error message that occurred in the Velopack library.
/// @param psz_error A buffer to store the error message.
/// @param c_error The size of the `psz_error` buffer.
/// @returns The number of characters written to `psz_error` (including null terminator). If the return value is greater than `c_error`, the buffer was too small and the message was truncated.
#[no_mangle]
pub extern "C" fn vpkc_get_last_error(psz_error: *mut c_char, c_error: size_t) -> size_t {
    let error = get_last_error();
    return_cstr(psz_error, c_error, &error)
}

/// Set a custom log callback. This will be called for all log messages generated by the Velopack library.
/// @param cb_log The callback to call with log messages. The callback takes a user data pointer, a log level, and the log message as a string.
/// @param p_user_data Optional user data to be passed to the callback.
#[no_mangle]
pub extern "C" fn vpkc_set_logger(cb_log: vpkc_log_callback_t, p_user_data: *mut c_void) {
    set_log_callback(cb_log, p_user_data);
}
