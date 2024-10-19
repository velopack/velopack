#![allow(non_snake_case)]

mod map;
use map::*;

use anyhow::{bail, Result};
use log::{Level, Log, Metadata, Record};
use std::sync::atomic::{AtomicUsize, Ordering};
use velopack::{sources, Error as VelopackError, UpdateCheck, UpdateManager, VelopackApp};

#[cxx::bridge]
mod ffi {
    // Shared structs with fields visible to both languages.
    #[derive(Default)]
    pub struct StringOption {
        pub data: String,
        pub has_data: bool,
    }

    #[derive(Default)]
    pub struct StringArrayOption {
        pub data: Vec<String>,
        pub has_data: bool,
    }

    // !! AUTO-GENERATED-START BRIDGE_DTOS
    #[derive(Default)]
    pub struct VelopackLocatorConfigDto {
        pub RootAppDir: String,
        pub UpdateExePath: String,
        pub PackagesDir: String,
        pub ManifestPath: String,
        pub CurrentBinaryDir: String,
        pub IsPortable: bool,
    }

    #[derive(Default)]
    pub struct VelopackLocatorConfigDtoOption {
        pub data: VelopackLocatorConfigDto,
        pub has_data: bool,
    }

    #[derive(Default)]
    pub struct VelopackAssetDto {
        pub PackageId: String,
        pub Version: String,
        pub Type: String,
        pub FileName: String,
        pub SHA1: String,
        pub SHA256: String,
        pub Size: u64,
        pub NotesMarkdown: String,
        pub NotesHtml: String,
    }

    #[derive(Default)]
    pub struct VelopackAssetDtoOption {
        pub data: VelopackAssetDto,
        pub has_data: bool,
    }

    #[derive(Default)]
    pub struct UpdateInfoDto {
        pub TargetFullRelease: VelopackAssetDto,
        pub IsDowngrade: bool,
    }

    #[derive(Default)]
    pub struct UpdateInfoDtoOption {
        pub data: UpdateInfoDto,
        pub has_data: bool,
    }

    #[derive(Default)]
    pub struct UpdateOptionsDto {
        pub AllowVersionDowngrade: bool,
        pub ExplicitChannel: StringOption,
    }

    #[derive(Default)]
    pub struct UpdateOptionsDtoOption {
        pub data: UpdateOptionsDto,
        pub has_data: bool,
    }
    // !! AUTO-GENERATED-END BRIDGE_DTOS

    // C++ types and signatures exposed to Rust.
    unsafe extern "C++" {
        include!("velopack_libc/include/Velopack.h");
        include!("velopack_libc/src/bridge.hpp");
        
        type HookCallbackManager;
        fn install_hook(self: &HookCallbackManager, app_version: String);
        fn update_hook(self: &HookCallbackManager, app_version: String);
        fn obsolete_hook(self: &HookCallbackManager, app_version: String);
        fn uninstall_hook(self: &HookCallbackManager, app_version: String);
        fn firstrun_hook(self: &HookCallbackManager, app_version: String);
        fn restarted_hook(self: &HookCallbackManager, app_version: String);

        type DownloadCallbackManager;
        fn download_progress(self: &DownloadCallbackManager, progress: i16);

        type LoggerCallbackManager;
        fn log(self: &LoggerCallbackManager, level: String, message: String);
    }

    // Rust types and signatures exposed to C++.
    extern "Rust" {
        type UpdateManagerOpaque;
        fn bridge_new_update_manager(
            url_or_path: &String,
            options: &UpdateOptionsDtoOption,
            locator: &VelopackLocatorConfigDtoOption,
        ) -> Result<Box<UpdateManagerOpaque>>;
        fn bridge_get_current_version(manager: &UpdateManagerOpaque) -> String;
        fn bridge_get_app_id(manager: &UpdateManagerOpaque) -> String;
        fn bridge_is_portable(manager: &UpdateManagerOpaque) -> bool;
        fn bridge_update_pending_restart(manager: &UpdateManagerOpaque) -> VelopackAssetDtoOption;
        fn bridge_check_for_updates(manager: &UpdateManagerOpaque) -> Result<UpdateInfoDtoOption>;
        fn bridge_download_updates(
            manager: &UpdateManagerOpaque,
            to_download: &UpdateInfoDto,
            progress: &DownloadCallbackManager,
        ) -> Result<()>;
        fn bridge_wait_exit_then_apply_update(
            manager: &UpdateManagerOpaque,
            to_apply: &VelopackAssetDto,
            silent: bool,
            restart: bool,
            restart_args: &Vec<String>,
        ) -> Result<()>;
        fn bridge_appbuilder_run(
            cb: &HookCallbackManager,
            custom_args: &StringArrayOption,
            locator: &VelopackLocatorConfigDtoOption,
            auto_apply: bool,
        );
        unsafe fn bridge_set_logger_callback(cb: *mut LoggerCallbackManager);
    }
}

#[derive(Clone)]
struct UpdateManagerOpaque {
    obj: UpdateManager,
}

fn bridge_new_update_manager(
    url_or_path: &String,
    options: &ffi::UpdateOptionsDtoOption,
    locator: &ffi::VelopackLocatorConfigDtoOption,
) -> Result<Box<UpdateManagerOpaque>> {
    let source = sources::AutoSource::new(url_or_path);
    let options = updateoptions_to_core_option(options);
    let locator = velopacklocatorconfig_to_core_option(locator);
    let update_manager = UpdateManager::new(source, options, locator)?;
    Ok(Box::new(UpdateManagerOpaque { obj: update_manager }))
}

fn bridge_get_current_version(manager: &UpdateManagerOpaque) -> String {
    manager.obj.get_current_version_as_string()
}

fn bridge_get_app_id(manager: &UpdateManagerOpaque) -> String {
    manager.obj.get_app_id()
}

fn bridge_is_portable(manager: &UpdateManagerOpaque) -> bool {
    manager.obj.get_is_portable()
}

fn bridge_update_pending_restart(manager: &UpdateManagerOpaque) -> ffi::VelopackAssetDtoOption {
    let asset_opt = manager.obj.get_update_pending_restart();
    velopackasset_to_bridge_option(&asset_opt)
}

fn bridge_check_for_updates(manager: &UpdateManagerOpaque) -> Result<ffi::UpdateInfoDtoOption> {
    let info_opt = if let UpdateCheck::UpdateAvailable(info) = manager.obj.check_for_updates()? { Some(info) } else { None };
    Ok(updateinfo_to_bridge_option(&info_opt))
}

fn bridge_download_updates(
    manager: &UpdateManagerOpaque,
    to_download: &ffi::UpdateInfoDto,
    cb: &ffi::DownloadCallbackManager,
) -> Result<()> {
    let info = updateinfo_to_core(&to_download);
    let (progress_sender, progress_receiver) = std::sync::mpsc::channel::<i16>();
    let (completion_sender, completion_receiver) = std::sync::mpsc::channel::<std::result::Result<(), VelopackError>>();

    // Move the download_updates call into a new thread
    let manager = manager.clone();
    std::thread::spawn(move || {
        let result = manager.obj.download_updates(&info, Some(progress_sender));
        let _ = completion_sender.send(result);
    });

    // Process progress updates on the caller's thread
    loop {
        // Try to receive progress updates without blocking
        match progress_receiver.try_recv() {
            Ok(progress) => {
                cb.download_progress(progress);
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
}

fn bridge_wait_exit_then_apply_update(
    manager: &UpdateManagerOpaque,
    to_apply: &ffi::VelopackAssetDto,
    silent: bool,
    restart: bool,
    restart_args: &Vec<String>,
) -> Result<()> {
    let asset = velopackasset_to_core(&to_apply);
    manager.obj.wait_exit_then_apply_updates(&asset, silent, restart, restart_args)?;
    Ok(())
}

fn bridge_appbuilder_run(
    cb: &ffi::HookCallbackManager,
    custom_args: &ffi::StringArrayOption,
    locator: &ffi::VelopackLocatorConfigDtoOption,
    auto_apply: bool,
) {
    let mut app = VelopackApp::build()
        .set_auto_apply_on_startup(auto_apply)
        .on_first_run(|v| cb.firstrun_hook(v.to_string()))
        .on_restarted(|v| cb.restarted_hook(v.to_string()));

    #[cfg(windows)]
    {
        app = app
            .on_after_install_fast_callback(|v| cb.install_hook(v.to_string()))
            .on_after_update_fast_callback(|v| cb.update_hook(v.to_string()))
            .on_before_update_fast_callback(|v| cb.obsolete_hook(v.to_string()))
            .on_before_uninstall_fast_callback(|v| cb.uninstall_hook(v.to_string()));
    }

    if locator.has_data {
        let locator = velopacklocatorconfig_to_core(&locator.data);
        app = app.set_locator(locator);
    }

    if custom_args.has_data {
        app = app.set_args(custom_args.data.clone());
    }

    app.run();
}

struct LoggerImpl {}

static LOGGER: LoggerImpl = LoggerImpl {};

impl Log for LoggerImpl {
    fn enabled(&self, metadata: &Metadata) -> bool {
        metadata.level() <= log::max_level()
    }

    fn log(&self, record: &Record) {
        if !self.enabled(record.metadata()) {
            return;
        }

        let text = format!("{}", record.args());

        let level = match record.level() {
            Level::Error => "error",
            Level::Warn => "warn",
            Level::Info => "info",
            Level::Debug => "debug",
            Level::Trace => "trace",
        }
        .to_string();

        if let Some(cb) = get_logger() {
            if let Some(cb) = unsafe { cb.as_mut() } {
                cb.log(level, text);
            }
        }
    }

    fn flush(&self) {}
}

lazy_static::lazy_static! {
    static ref LOGGER_CB: AtomicUsize = AtomicUsize::new(0);
}

fn store_logger(ptr: *mut ffi::LoggerCallbackManager) {
    LOGGER_CB.store(ptr as usize, Ordering::SeqCst);
}

fn get_logger() -> Option<*mut ffi::LoggerCallbackManager> {
    let ptr = LOGGER_CB.load(Ordering::SeqCst);
    if ptr == 0 {
        None
    } else {
        Some(ptr as *mut ffi::LoggerCallbackManager)
    }
}

unsafe fn bridge_set_logger_callback(cb: *mut ffi::LoggerCallbackManager) {
    let _ = log::set_logger(&LOGGER);
    log::set_max_level(log::LevelFilter::Trace);
    store_logger(cb);
}
