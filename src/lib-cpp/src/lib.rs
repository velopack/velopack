#![allow(dead_code)]
#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

mod statics;
use statics::*;

mod types;
use types::*;

use anyhow::{anyhow, bail};
use std::ffi::{c_char, c_void, CString};
use velopack::{sources, Error as VelopackError, UpdateCheck, UpdateManager, VelopackApp};

#[repr(C)]
pub enum vpkc_update_check_t {
    UPDATE_ERROR = -1,
    UPDATE_AVAILABLE = 0,
    NO_UPDATE_AVAILABLE = 1,
    REMOTE_IS_EMPTY = 2,
}

#[no_mangle]
pub extern "C" fn vpkc_new_update_manager(
    psz_url_or_string: *const c_char,
    p_options: *mut vpkc_update_options_t,
    p_locator: *mut vpkc_locator_config_t,
    p_manager: *mut *mut c_void,
) -> bool {
    wrap_error(|| {
        let update_url = c_to_string_opt(psz_url_or_string).ok_or(anyhow!("URL or path is null"))?;
        let source = sources::AutoSource::new(&update_url);
        let options = c_to_updateoptions_opt(p_options);
        let locator = c_to_velopacklocatorconfig_opt(p_locator);
        let manager = UpdateManager::new(source, options, locator)?;
        let opaque = Box::new(UpdateManagerOpaque::new(manager));
        unsafe { *p_manager = Box::into_raw(opaque) as *mut c_void };
        Ok(())
    })
}

#[no_mangle]
pub extern "C" fn vpkc_get_current_version(p_manager: *mut c_void, psz_version: *mut c_char, c_version: usize) -> usize {
    if p_manager.is_null() {
        return 0;
    }

    let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };
    let version = manager.obj.get_current_version_as_string();
    return_cstr(psz_version, c_version, &version)
}

#[no_mangle]
pub extern "C" fn vpkc_get_app_id(p_manager: *mut c_void, psz_id: *mut c_char, c_id: usize) -> usize {
    if p_manager.is_null() {
        return 0;
    }

    let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };
    let app_id = manager.obj.get_app_id();
    return_cstr(psz_id, c_id, &app_id)
}

#[no_mangle]
pub extern "C" fn vpkc_is_portable(p_manager: *mut c_void) -> bool {
    if p_manager.is_null() {
        return false;
    }

    let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };
    manager.obj.get_is_portable()
}

#[no_mangle]
pub extern "C" fn vpkc_update_pending_restart(p_manager: *mut c_void, p_asset: *mut vpkc_asset_t) -> bool {
    if p_manager.is_null() {
        return false;
    }

    let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };
    let asset_opt = manager.obj.get_update_pending_restart();

    if let Some(asset) = asset_opt {
        unsafe { allocate_velopackasset(asset, p_asset) };
        true
    } else {
        false
    }
}

#[no_mangle]
pub extern "C" fn vpkc_check_for_updates(p_manager: *mut c_void, p_update: *mut vpkc_update_info_t) -> vpkc_update_check_t {
    if p_manager.is_null() {
        set_last_error("pManager must not be null");
        return vpkc_update_check_t::UPDATE_ERROR;
    }

    let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };

    match manager.obj.check_for_updates() {
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
    }
}

#[no_mangle]
pub extern "C" fn vpkc_download_updates(
    p_manager: *mut c_void,
    p_update: *mut vpkc_update_info_t,
    cb_progress: vpkc_progress_callback_t,
    p_user_data: *mut c_void,
) -> bool {
    wrap_error(|| {
        if p_manager.is_null() {
            bail!("pManager must not be null");
        }

        let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };
        let update = c_to_updateinfo_opt(p_update).ok_or(anyhow!("pUpdate must not be null"))?;

        let (progress_sender, progress_receiver) = std::sync::mpsc::channel::<i16>();
        let (completion_sender, completion_receiver) = std::sync::mpsc::channel::<std::result::Result<(), VelopackError>>();

        // Move the download_updates call into a new thread
        let manager = manager.clone();
        std::thread::spawn(move || {
            let result = manager.obj.download_updates(&update, Some(progress_sender));
            let _ = completion_sender.send(result);
        });

        // Process progress updates on the caller's thread
        loop {
            // Try to receive progress updates without blocking
            match progress_receiver.try_recv() {
                Ok(progress) => {
                    cb_progress(p_user_data, progress as usize);
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
    })
}

#[no_mangle]
pub extern "C" fn vpkc_wait_exit_then_apply_update(
    p_manager: *mut c_void,
    p_asset: *mut vpkc_asset_t,
    b_silent: bool,
    b_restart: bool,
    p_restart_args: *mut *mut c_char,
    c_restart_args: usize,
) -> bool {
    wrap_error(|| {
        if p_manager.is_null() {
            bail!("pManager must not be null");
        }

        let manager = unsafe { &*(p_manager as *mut UpdateManagerOpaque) };
        let asset = c_to_velopackasset_opt(p_asset).ok_or(anyhow!("pAsset must not be null"))?;
        let restart_args = c_to_string_array_opt(p_restart_args, c_restart_args).unwrap_or_default();
        manager.obj.wait_exit_then_apply_updates(&asset, b_silent, b_restart, &restart_args)?;
        Ok(())
    })
}

#[no_mangle]
pub extern "C" fn vpkc_free_update_manager(p_manager: *mut c_void) {
    if !p_manager.is_null() {
        // Convert the raw pointer back into a Box to deallocate it properly
        let _ = unsafe { Box::from_raw(p_manager as *mut UpdateManagerOpaque) };
    }
}

#[no_mangle]
pub extern "C" fn vpkc_free_update_info(p_update_info: *mut vpkc_update_info_t) {
    if !p_update_info.is_null() {
        unsafe { free_updateinfo(p_update_info) };
    }
}

#[no_mangle]
pub extern "C" fn vpkc_free_asset(p_asset: *mut vpkc_asset_t) {
    if !p_asset.is_null() {
        unsafe { free_velopackasset(p_asset) };
    }
}

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

#[no_mangle]
pub extern "C" fn vpkc_app_set_auto_apply_on_startup(b_auto_apply: bool) {
    update_app_options(|opt| {
        opt.auto_apply = Some(b_auto_apply);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_args(p_args: *mut *mut c_char, c_args: usize) {
    update_app_options(|opt| {
        opt.args = c_to_string_array_opt(p_args, c_args);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_locator(p_locator: *mut vpkc_locator_config_t) {
    update_app_options(|opt| {
        opt.locator = c_to_velopacklocatorconfig_opt(p_locator);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_after_install(cb_after_install: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.install_hook = Some(cb_after_install);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_before_uninstall(cb_before_uninstall: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.uninstall_hook = Some(cb_before_uninstall);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_before_update(cb_before_update: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.obsolete_hook = Some(cb_before_update);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_after_update(cb_after_update: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.update_hook = Some(cb_after_update);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_first_run(cb_first_run: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.firstrun_hook = Some(cb_first_run);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_app_set_hook_restarted(cb_restarted: vpkc_hook_callback_t) {
    update_app_options(|opt| {
        opt.restarted_hook = Some(cb_restarted);
    });
}

#[no_mangle]
pub extern "C" fn vpkc_get_last_error(psz_error: *mut c_char, c_error: usize) -> usize {
    let error = get_last_error();
    return_cstr(psz_error, c_error, &error)
}

#[no_mangle]
pub extern "C" fn vpkc_set_logger(cb_log: vpkc_log_callback_t, p_user_data: *mut c_void) {
    set_log_callback(cb_log, p_user_data);
}

#[derive(Clone)]
struct UpdateManagerOpaque {
    obj: UpdateManager,
}

impl UpdateManagerOpaque {
    fn new(obj: UpdateManager) -> Self {
        log::debug!("UpdateManagerOpaque allocated");
        UpdateManagerOpaque { obj }
    }
}

impl Drop for UpdateManagerOpaque {
    fn drop(&mut self) {
        log::debug!("UpdateManagerOpaque dropped");
    }
}
