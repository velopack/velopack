#![allow(non_snake_case)]

use anyhow::{bail, Result};
use std::path::PathBuf;
use std::sync::atomic::{AtomicUsize, Ordering};
use log::{Level, Log, Metadata, Record};
use velopack::{
    locator::VelopackLocatorConfig,
    UpdateManager,
    UpdateCheck,
    UpdateOptions as VelopackUpdateOptions,
    UpdateInfo as VelopackUpdateInfo,
    VelopackAsset,
    VelopackApp,
    Error as VelopackError,
    sources,
};

#[cxx::bridge]
mod ffi {
    // Shared structs with fields visible to both languages.
    #[derive(Default)]
    pub struct AssetDto {
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

    pub struct AssetOption {
        pub data: AssetDto,
        pub has_data: bool,
    }

    #[derive(Default)]
    pub struct UpdateInfoDto {
        pub TargetFullRelease: AssetDto,
        pub IsDowngrade: bool,
    }

    pub struct UpdateInfoOption {
        pub data: UpdateInfoDto,
        pub has_data: bool,
    }

    pub struct LocatorConfigDto<'a> {
        pub RootAppDir: &'a CxxString,
        pub UpdateExePath: &'a CxxString,
        pub PackagesDir: &'a CxxString,
        pub ManifestPath: &'a CxxString,
        pub CurrentBinaryDir: &'a CxxString,
        pub IsPortable: bool,
    }

    pub struct LocatorConfigOption<'a> {
        pub data: LocatorConfigDto<'a>,
        pub has_data: bool,
    }

    pub struct UpdateOptionsDto<'a> {
        pub AllowVersionDowngrade: bool,
        pub ExplicitChannel: &'a CxxString,
    }

    pub struct StringArrayOption {
        pub data: Vec<String>,
        pub has_data: bool,
    }
    
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
        fn bridge_new_update_manager(url_or_path: &CxxString, options: UpdateOptionsDto, locator: LocatorConfigOption) -> Result<Box<UpdateManagerOpaque>>;
        fn bridge_get_current_version(manager: &UpdateManagerOpaque) -> String;
        fn bridge_get_app_id(manager: &UpdateManagerOpaque) -> String;
        fn bridge_is_portable(manager: &UpdateManagerOpaque) -> bool;
        fn bridge_update_pending_restart(manager: &UpdateManagerOpaque) -> AssetOption;
        fn bridge_check_for_updates(manager: &UpdateManagerOpaque) -> Result<UpdateInfoOption>;
        fn bridge_download_update(manager: &UpdateManagerOpaque, to_download: UpdateInfoDto, progress: UniquePtr<DownloadCallbackManager>) -> Result<()>;
        fn bridge_wait_exit_then_apply_update(manager: &UpdateManagerOpaque, to_download: AssetDto, silent: bool, restart: bool, restart_args: Vec<String>) -> Result<()>;
        fn bridge_appbuilder_run(cb: UniquePtr<HookCallbackManager>, custom_args: StringArrayOption, locator: LocatorConfigOption, auto_apply: bool);
        fn bridge_set_logger_callback(cb: UniquePtr<LoggerCallbackManager>);
    }
}

#[derive(Clone)]
struct UpdateManagerOpaque {
    obj: UpdateManager,
}

fn to_locator_config(locator: &ffi::LocatorConfigDto) -> VelopackLocatorConfig {
    VelopackLocatorConfig {
        RootAppDir: PathBuf::from(locator.RootAppDir.to_string_lossy().to_string()),
        UpdateExePath: PathBuf::from(locator.UpdateExePath.to_string_lossy().to_string()),
        PackagesDir: PathBuf::from(locator.PackagesDir.to_string_lossy().to_string()),
        ManifestPath: PathBuf::from(locator.ManifestPath.to_string_lossy().to_string()),
        CurrentBinaryDir: PathBuf::from(locator.CurrentBinaryDir.to_string_lossy().to_string()),
        IsPortable: locator.IsPortable,
    }
}

fn to_update_options(options: &ffi::UpdateOptionsDto) -> VelopackUpdateOptions {
    let channel = options.ExplicitChannel.to_string_lossy().to_string();
    VelopackUpdateOptions {
        AllowVersionDowngrade: options.AllowVersionDowngrade,
        ExplicitChannel: if channel.is_empty() { None } else { Some(channel) },
    }
}

fn from_asset(asset: &VelopackAsset) -> ffi::AssetDto {
    ffi::AssetDto {
        PackageId: asset.PackageId.clone(),
        Version: asset.Version.clone(),
        Type: asset.Type.clone(),
        FileName: asset.FileName.clone(),
        SHA1: asset.SHA1.clone(),
        SHA256: asset.SHA256.clone(),
        Size: asset.Size,
        NotesMarkdown: asset.NotesMarkdown.clone(),
        NotesHtml: asset.NotesHtml.clone(),
    }
}

fn to_asset(asset: &ffi::AssetDto) -> VelopackAsset {
    VelopackAsset {
        PackageId: asset.PackageId.clone(),
        Version: asset.Version.clone(),
        Type: asset.Type.clone(),
        FileName: asset.FileName.clone(),
        SHA1: asset.SHA1.clone(),
        SHA256: asset.SHA256.clone(),
        Size: asset.Size,
        NotesMarkdown: asset.NotesMarkdown.clone(),
        NotesHtml: asset.NotesHtml.clone(),
    }
}

fn from_update_info(info: &VelopackUpdateInfo) -> ffi::UpdateInfoDto {
    ffi::UpdateInfoDto {
        TargetFullRelease: from_asset(&info.TargetFullRelease),
        IsDowngrade: info.IsDowngrade,
    }
}

fn to_update_info(info: &ffi::UpdateInfoDto) -> VelopackUpdateInfo {
    VelopackUpdateInfo {
        TargetFullRelease: to_asset(&info.TargetFullRelease),
        IsDowngrade: info.IsDowngrade,
    }
}

fn bridge_new_update_manager(url_or_path: &cxx::CxxString, options: ffi::UpdateOptionsDto, locator: ffi::LocatorConfigOption) -> Result<Box<UpdateManagerOpaque>> {
    let url = url_or_path.to_string_lossy();
    let source = sources::AutoSource::new(&url);
    let update_options = to_update_options(&options);

    if locator.has_data {
        let locator_config = to_locator_config(&locator.data);
        let update_manager = UpdateManager::new(source, Some(update_options), Some(locator_config))?;
        Ok(Box::new(UpdateManagerOpaque { obj: update_manager }))
    } else {
        let update_manager = UpdateManager::new(source, Some(update_options), None)?;
        Ok(Box::new(UpdateManagerOpaque { obj: update_manager }))
    }
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

fn bridge_update_pending_restart(manager: &UpdateManagerOpaque) -> ffi::AssetOption {
    if let Some(info) = manager.obj.get_update_pending_restart() {
        let update = from_asset(&info);
        ffi::AssetOption { data: update, has_data: true }
    } else {
        let default = ffi::AssetDto::default();
        ffi::AssetOption { data: default, has_data: false }
    }
}

fn bridge_check_for_updates(manager: &UpdateManagerOpaque) -> Result<ffi::UpdateInfoOption> {
    if let UpdateCheck::UpdateAvailable(info) = manager.obj.check_for_updates()? {
        let update = from_update_info(&info);
        Ok(ffi::UpdateInfoOption { data: update, has_data: true })
    } else {
        let default = ffi::UpdateInfoDto::default();
        Ok(ffi::UpdateInfoOption { data: default, has_data: false })
    }
}

fn bridge_download_update(manager: &UpdateManagerOpaque, to_download: ffi::UpdateInfoDto, cb: cxx::UniquePtr<ffi::DownloadCallbackManager>) -> Result<()> {
    let info = to_update_info(&to_download);

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

fn bridge_wait_exit_then_apply_update(manager: &UpdateManagerOpaque, to_download: ffi::AssetDto, silent: bool, restart: bool, restart_args: Vec<String>) -> Result<()> {
    let asset = to_asset(&to_download);
    manager.obj.wait_exit_then_apply_updates(&asset, silent, restart, restart_args)?;
    Ok(())
}

fn bridge_appbuilder_run(cb: cxx::UniquePtr<ffi::HookCallbackManager>, custom_args: ffi::StringArrayOption, locator: ffi::LocatorConfigOption, auto_apply: bool) {
    let mut app = VelopackApp::build()
        .on_first_run(|v| cb.firstrun_hook(v.to_string()))
        .on_restarted(|v| cb.restarted_hook(v.to_string()))
        .set_auto_apply_on_startup(auto_apply);

    #[cfg(windows)]
    {
        app = app.on_after_install_fast_callback(|v| cb.install_hook(v.to_string()))
            .on_after_update_fast_callback(|v| cb.update_hook(v.to_string()))
            .on_before_update_fast_callback(|v| cb.obsolete_hook(v.to_string()))
            .on_before_uninstall_fast_callback(|v| cb.uninstall_hook(v.to_string()));
    }

    if locator.has_data {
        app = app.set_locator(to_locator_config(&locator.data));
    }

    if custom_args.has_data {
        app = app.set_args(custom_args.data);
    }

    let _ = log::set_logger(&LOGGER);
    log::set_max_level(log::LevelFilter::Trace);

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
        }.to_string();
        
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

fn bridge_set_logger_callback(cb: cxx::UniquePtr<ffi::LoggerCallbackManager>) {
    let cb = cb.into_raw();
    store_logger(cb);
}