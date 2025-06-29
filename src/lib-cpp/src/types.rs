use anyhow::{bail, Result};
use libc::{c_char, c_void, size_t};
use std::ffi::{CStr, CString};
use std::mem::size_of;
use std::path::PathBuf;
use velopack::{locator::VelopackLocatorConfig, UpdateInfo, UpdateOptions, VelopackAsset};

/// The result of a call to check for updates. This can indicate that an update is available, or that an error occurred.
#[repr(i8)]
#[derive(Debug, Copy, Clone, PartialEq, Eq)]
pub enum vpkc_update_check_t {
    UPDATE_ERROR = -1,
    UPDATE_AVAILABLE = 0,
    NO_UPDATE_AVAILABLE = 1,
    REMOTE_IS_EMPTY = 2,
}

/// Opaque type for the Velopack UpdateManager. Must be freed with `vpkc_free_update_manager`.
pub type vpkc_update_manager_t = c_void;

/// Opaque type for a Velopack UpdateSource. Must be freed with `vpkc_free_update_source`.
pub type vpkc_update_source_t = c_void;

/// Progress callback function.
pub type vpkc_progress_callback_t = Option<extern "C" fn(p_user_data: *mut c_void, progress: size_t)>;

/// Log callback function.
pub type vpkc_log_callback_t = Option<extern "C" fn(p_user_data: *mut c_void, psz_level: *const c_char, psz_message: *const c_char)>;

/// VelopackApp startup hook callback function.
pub type vpkc_hook_callback_t = Option<extern "C" fn(p_user_data: *mut c_void, psz_app_version: *const c_char)>;

/// User delegate for to fetch a release feed. This function should return the raw JSON string of the release.json feed.
pub type vpkc_release_feed_delegate_t = Option<extern "C" fn(p_user_data: *mut c_void, psz_releases_name: *const c_char) -> *mut c_char>;

/// User delegate for freeing a release feed. This function should free the feed string returned by `vpkc_release_feed_delegate_t`.
pub type vpkc_free_release_feed_t = Option<extern "C" fn(p_user_data: *mut c_void, psz_feed: *mut c_char)>;

/// User delegate for downloading an asset file. This function is expected to download the provided asset
/// to the provided local file path. Througout, you can use the progress callback to write progress reports.
/// The function should return true if the download was successful, false otherwise.
/// Progress
pub type vpkc_download_asset_delegate_t = Option<
    extern "C" fn(
        p_user_data: *mut c_void,
        p_asset: *const vpkc_asset_t,
        psz_local_path: *const c_char,
        progress_callback_id: size_t,
    ) -> bool,
>;

pub fn c_to_String(psz: *const c_char) -> Result<String> {
    if psz.is_null() {
        bail!("Null pointer: String must be set.");
    }
    let cstr = unsafe { CStr::from_ptr(psz) };
    Ok(String::from_utf8_lossy(cstr.to_bytes()).to_string())
}

pub fn c_to_String_vec(p_args: *mut *mut c_char, c_args: size_t) -> Result<Vec<String>> {
    if p_args.is_null() || c_args == 0 {
        return Ok(Vec::new());
    }
    let mut args = Vec::with_capacity(c_args);
    for i in 0..c_args {
        let arg = c_to_String(unsafe { *p_args.add(i) })?;
        args.push(arg);
    }
    Ok(args)
}

pub fn c_to_PathBuf(psz: *const c_char) -> Result<PathBuf> {
    c_to_String(psz).map(PathBuf::from)
}

pub fn allocate_String<'a, T: Into<Option<&'a String>>>(s: T) -> *mut c_char {
    let s = s.into();
    if s.is_none() {
        return std::ptr::null_mut();
    }
    let s = s.unwrap();
    let cstr = CString::new(s.clone()).unwrap();
    cstr.into_raw()
}

pub fn allocate_PathBuf(p: &PathBuf) -> *mut c_char {
    let st = p.to_string_lossy().to_string();
    allocate_String(&st)
}

pub unsafe fn free_String(psz: *mut c_char) {
    if !psz.is_null() {
        drop(CString::from_raw(psz));
    }
}

pub unsafe fn free_PathBuf(psz: *mut c_char) {
    free_String(psz);
}

pub fn return_cstr(psz: *mut c_char, c: size_t, s: &str) -> size_t {
    if !psz.is_null() && c > 0 {
        let cstr = CString::new(s).unwrap();
        let bytes = cstr.as_bytes_with_nul();
        let len = bytes.len().min(c);
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), psz as *mut u8, len);
            *psz.add(len) = 0;
        }
    }

    return s.len();
}

// !! AUTO-GENERATED-START RUST_TYPES
#[rustfmt::skip]
#[repr(C)]
/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
pub struct vpkc_locator_config_t {
    /// The root directory of the current app.
    pub RootAppDir: *mut c_char,
    /// The path to the Update.exe binary.
    pub UpdateExePath: *mut c_char,
    /// The path to the packages' directory.
    pub PackagesDir: *mut c_char,
    /// The current app manifest.
    pub ManifestPath: *mut c_char,
    /// The directory containing the application's user binaries.
    pub CurrentBinaryDir: *mut c_char,
    /// Whether the current application is portable or installed.
    pub IsPortable: bool,
}

#[rustfmt::skip]
pub fn c_to_VelopackLocatorConfig(obj: *mut vpkc_locator_config_t) -> Result<VelopackLocatorConfig> {
    if obj.is_null() { bail!("Null pointer: VelopackLocatorConfig must be set."); }
    let obj = unsafe { &*obj };
    let result = VelopackLocatorConfig {
        RootAppDir: c_to_PathBuf(obj.RootAppDir)?,
        UpdateExePath: c_to_PathBuf(obj.UpdateExePath)?,
        PackagesDir: c_to_PathBuf(obj.PackagesDir)?,
        ManifestPath: c_to_PathBuf(obj.ManifestPath)?,
        CurrentBinaryDir: c_to_PathBuf(obj.CurrentBinaryDir)?,
        IsPortable: obj.IsPortable,
    };
    Ok(result)
}

#[rustfmt::skip]
pub fn c_to_VelopackLocatorConfig_vec(obj: *mut *mut vpkc_locator_config_t, count: size_t) -> Result<Vec<VelopackLocatorConfig>> {
    if obj.is_null() || count == 0 { return Ok(Vec::new()); }
    let mut assets = Vec::with_capacity(count as usize);
    for i in 0..count {
        let ptr = unsafe { *obj.add(i as usize) };
        assets.push(c_to_VelopackLocatorConfig(ptr)?);
    }
    Ok(assets)
}

#[rustfmt::skip]
pub unsafe fn allocate_VelopackLocatorConfig<'a, T: Into<Option<&'a VelopackLocatorConfig>>>(dto: T) -> *mut vpkc_locator_config_t {
    let dto = dto.into();
    if dto.is_none() {
        return std::ptr::null_mut();
    }
    log::debug!("vpkc_locator_config_t allocated");
    let dto = dto.unwrap();
    let obj = libc::malloc(size_of::<vpkc_locator_config_t>()) as *mut vpkc_locator_config_t;
    (*obj).RootAppDir = allocate_PathBuf(&dto.RootAppDir);
    (*obj).UpdateExePath = allocate_PathBuf(&dto.UpdateExePath);
    (*obj).PackagesDir = allocate_PathBuf(&dto.PackagesDir);
    (*obj).ManifestPath = allocate_PathBuf(&dto.ManifestPath);
    (*obj).CurrentBinaryDir = allocate_PathBuf(&dto.CurrentBinaryDir);
    (*obj).IsPortable = dto.IsPortable;
    obj
}

#[rustfmt::skip]
pub unsafe fn allocate_VelopackLocatorConfig_vec(dto: &Vec<VelopackLocatorConfig>, count: *mut size_t) -> *mut *mut vpkc_locator_config_t {
    if dto.is_empty() {
        *count = 0;
        return std::ptr::null_mut(); 
    }
    log::debug!("vpkc_locator_config_t vector allocated");
    let count_value = dto.len() as size_t;
    *count = count_value;
    let mut assets = Vec::with_capacity(count_value as usize);
    for i in 0..count_value {
        let ptr = allocate_VelopackLocatorConfig(&dto[i as usize]);
        assets.push(ptr);
    }
    let ptr = assets.as_mut_ptr();
    std::mem::forget(assets);
    ptr
}

#[rustfmt::skip]
pub unsafe fn free_VelopackLocatorConfig(obj: *mut vpkc_locator_config_t) {
    if obj.is_null() { return; }
    free_PathBuf((*obj).RootAppDir);
    free_PathBuf((*obj).UpdateExePath);
    free_PathBuf((*obj).PackagesDir);
    free_PathBuf((*obj).ManifestPath);
    free_PathBuf((*obj).CurrentBinaryDir);
    
    libc::free(obj as *mut c_void);
    log::debug!("vpkc_locator_config_t freed");
}

#[rustfmt::skip]
pub unsafe fn free_VelopackLocatorConfig_vec(obj: *mut *mut vpkc_locator_config_t, count: size_t) {
    if obj.is_null() || count == 0 { return; }
    let vec = Vec::from_raw_parts(obj, count as usize, count as usize);
    for i in 0..count {
        let ptr = *vec.get_unchecked(i as usize);
        free_VelopackLocatorConfig(ptr);
    }
    log::debug!("vpkc_locator_config_t vector freed");
}

#[rustfmt::skip]
#[repr(C)]
/// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
pub struct vpkc_asset_t {
    /// The name or Id of the package containing this release.
    pub PackageId: *mut c_char,
    /// The version of this release.
    pub Version: *mut c_char,
    /// The type of asset (eg. "Full" or "Delta").
    pub Type: *mut c_char,
    /// The filename of the update package containing this release.
    pub FileName: *mut c_char,
    /// The SHA1 checksum of the update package containing this release.
    pub SHA1: *mut c_char,
    /// The SHA256 checksum of the update package containing this release.
    pub SHA256: *mut c_char,
    /// The size in bytes of the update package containing this release.
    pub Size: u64,
    /// The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
    pub NotesMarkdown: *mut c_char,
    /// The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
    pub NotesHtml: *mut c_char,
}

#[rustfmt::skip]
pub fn c_to_VelopackAsset(obj: *mut vpkc_asset_t) -> Result<VelopackAsset> {
    if obj.is_null() { bail!("Null pointer: VelopackAsset must be set."); }
    let obj = unsafe { &*obj };
    let result = VelopackAsset {
        PackageId: c_to_String(obj.PackageId)?,
        Version: c_to_String(obj.Version)?,
        Type: c_to_String(obj.Type)?,
        FileName: c_to_String(obj.FileName)?,
        SHA1: c_to_String(obj.SHA1)?,
        SHA256: c_to_String(obj.SHA256)?,
        Size: obj.Size,
        NotesMarkdown: c_to_String(obj.NotesMarkdown)?,
        NotesHtml: c_to_String(obj.NotesHtml)?,
    };
    Ok(result)
}

#[rustfmt::skip]
pub fn c_to_VelopackAsset_vec(obj: *mut *mut vpkc_asset_t, count: size_t) -> Result<Vec<VelopackAsset>> {
    if obj.is_null() || count == 0 { return Ok(Vec::new()); }
    let mut assets = Vec::with_capacity(count as usize);
    for i in 0..count {
        let ptr = unsafe { *obj.add(i as usize) };
        assets.push(c_to_VelopackAsset(ptr)?);
    }
    Ok(assets)
}

#[rustfmt::skip]
pub unsafe fn allocate_VelopackAsset<'a, T: Into<Option<&'a VelopackAsset>>>(dto: T) -> *mut vpkc_asset_t {
    let dto = dto.into();
    if dto.is_none() {
        return std::ptr::null_mut();
    }
    log::debug!("vpkc_asset_t allocated");
    let dto = dto.unwrap();
    let obj = libc::malloc(size_of::<vpkc_asset_t>()) as *mut vpkc_asset_t;
    (*obj).PackageId = allocate_String(&dto.PackageId);
    (*obj).Version = allocate_String(&dto.Version);
    (*obj).Type = allocate_String(&dto.Type);
    (*obj).FileName = allocate_String(&dto.FileName);
    (*obj).SHA1 = allocate_String(&dto.SHA1);
    (*obj).SHA256 = allocate_String(&dto.SHA256);
    (*obj).Size = dto.Size;
    (*obj).NotesMarkdown = allocate_String(&dto.NotesMarkdown);
    (*obj).NotesHtml = allocate_String(&dto.NotesHtml);
    obj
}

#[rustfmt::skip]
pub unsafe fn allocate_VelopackAsset_vec(dto: &Vec<VelopackAsset>, count: *mut size_t) -> *mut *mut vpkc_asset_t {
    if dto.is_empty() {
        *count = 0;
        return std::ptr::null_mut(); 
    }
    log::debug!("vpkc_asset_t vector allocated");
    let count_value = dto.len() as size_t;
    *count = count_value;
    let mut assets = Vec::with_capacity(count_value as usize);
    for i in 0..count_value {
        let ptr = allocate_VelopackAsset(&dto[i as usize]);
        assets.push(ptr);
    }
    let ptr = assets.as_mut_ptr();
    std::mem::forget(assets);
    ptr
}

#[rustfmt::skip]
pub unsafe fn free_VelopackAsset(obj: *mut vpkc_asset_t) {
    if obj.is_null() { return; }
    free_String((*obj).PackageId);
    free_String((*obj).Version);
    free_String((*obj).Type);
    free_String((*obj).FileName);
    free_String((*obj).SHA1);
    free_String((*obj).SHA256);
    
    free_String((*obj).NotesMarkdown);
    free_String((*obj).NotesHtml);
    libc::free(obj as *mut c_void);
    log::debug!("vpkc_asset_t freed");
}

#[rustfmt::skip]
pub unsafe fn free_VelopackAsset_vec(obj: *mut *mut vpkc_asset_t, count: size_t) {
    if obj.is_null() || count == 0 { return; }
    let vec = Vec::from_raw_parts(obj, count as usize, count as usize);
    for i in 0..count {
        let ptr = *vec.get_unchecked(i as usize);
        free_VelopackAsset(ptr);
    }
    log::debug!("vpkc_asset_t vector freed");
}

#[rustfmt::skip]
#[repr(C)]
/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
pub struct vpkc_update_info_t {
    /// The available version that we are updating to.
    pub TargetFullRelease: *mut vpkc_asset_t,
    /// The base release that this update is based on. This is only available if the update is a delta update.
    pub BaseRelease: *mut vpkc_asset_t,
    /// The list of delta updates that can be applied to the base version to get to the target version.
    pub DeltasToTarget: *mut *mut vpkc_asset_t,
    /// The number of elements in the DeltasToTarget array.
    pub DeltasToTargetCount: size_t,
    /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
    /// In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
    /// deleted.
    pub IsDowngrade: bool,
}

#[rustfmt::skip]
pub fn c_to_UpdateInfo(obj: *mut vpkc_update_info_t) -> Result<UpdateInfo> {
    if obj.is_null() { bail!("Null pointer: UpdateInfo must be set."); }
    let obj = unsafe { &*obj };
    let result = UpdateInfo {
        TargetFullRelease: c_to_VelopackAsset(obj.TargetFullRelease)?,
        BaseRelease: c_to_VelopackAsset(obj.BaseRelease).ok(),
        DeltasToTarget: c_to_VelopackAsset_vec(obj.DeltasToTarget, obj.DeltasToTargetCount)?,
        IsDowngrade: obj.IsDowngrade,
    };
    Ok(result)
}

#[rustfmt::skip]
pub fn c_to_UpdateInfo_vec(obj: *mut *mut vpkc_update_info_t, count: size_t) -> Result<Vec<UpdateInfo>> {
    if obj.is_null() || count == 0 { return Ok(Vec::new()); }
    let mut assets = Vec::with_capacity(count as usize);
    for i in 0..count {
        let ptr = unsafe { *obj.add(i as usize) };
        assets.push(c_to_UpdateInfo(ptr)?);
    }
    Ok(assets)
}

#[rustfmt::skip]
pub unsafe fn allocate_UpdateInfo<'a, T: Into<Option<&'a UpdateInfo>>>(dto: T) -> *mut vpkc_update_info_t {
    let dto = dto.into();
    if dto.is_none() {
        return std::ptr::null_mut();
    }
    log::debug!("vpkc_update_info_t allocated");
    let dto = dto.unwrap();
    let obj = libc::malloc(size_of::<vpkc_update_info_t>()) as *mut vpkc_update_info_t;
    (*obj).TargetFullRelease = allocate_VelopackAsset(&dto.TargetFullRelease);
    (*obj).BaseRelease = allocate_VelopackAsset(&dto.BaseRelease);
    (*obj).DeltasToTarget = allocate_VelopackAsset_vec(&dto.DeltasToTarget, &mut (*obj).DeltasToTargetCount);
    (*obj).IsDowngrade = dto.IsDowngrade;
    obj
}

#[rustfmt::skip]
pub unsafe fn allocate_UpdateInfo_vec(dto: &Vec<UpdateInfo>, count: *mut size_t) -> *mut *mut vpkc_update_info_t {
    if dto.is_empty() {
        *count = 0;
        return std::ptr::null_mut(); 
    }
    log::debug!("vpkc_update_info_t vector allocated");
    let count_value = dto.len() as size_t;
    *count = count_value;
    let mut assets = Vec::with_capacity(count_value as usize);
    for i in 0..count_value {
        let ptr = allocate_UpdateInfo(&dto[i as usize]);
        assets.push(ptr);
    }
    let ptr = assets.as_mut_ptr();
    std::mem::forget(assets);
    ptr
}

#[rustfmt::skip]
pub unsafe fn free_UpdateInfo(obj: *mut vpkc_update_info_t) {
    if obj.is_null() { return; }
    free_VelopackAsset((*obj).TargetFullRelease);
    free_VelopackAsset((*obj).BaseRelease);
    free_VelopackAsset_vec((*obj).DeltasToTarget, (*obj).DeltasToTargetCount);
    
    libc::free(obj as *mut c_void);
    log::debug!("vpkc_update_info_t freed");
}

#[rustfmt::skip]
pub unsafe fn free_UpdateInfo_vec(obj: *mut *mut vpkc_update_info_t, count: size_t) {
    if obj.is_null() || count == 0 { return; }
    let vec = Vec::from_raw_parts(obj, count as usize, count as usize);
    for i in 0..count {
        let ptr = *vec.get_unchecked(i as usize);
        free_UpdateInfo(ptr);
    }
    log::debug!("vpkc_update_info_t vector freed");
}

#[rustfmt::skip]
#[repr(C)]
/// Options to customise the behaviour of UpdateManager.
pub struct vpkc_update_options_t {
    /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
    /// This could happen if a release has bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest version on that
    /// channel is lower than the current version.
    pub AllowVersionDowngrade: bool,
    /// **This option should usually be left None/NULL**.
    /// Overrides the default channel used to fetch updates.
    /// The default channel will be whatever channel was specified on the command line when building this release.
    /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
    /// This allows users to automatically receive updates from the same channel they installed from. This options
    /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
    /// without having to reinstall the application.
    pub ExplicitChannel: *mut c_char,
    /// Sets the maximum number of deltas to consider before falling back to a full update.
    /// The default is 10. Set to a negative number (eg. -1) to disable deltas.
    pub MaximumDeltasBeforeFallback: i32,
}

#[rustfmt::skip]
pub fn c_to_UpdateOptions(obj: *mut vpkc_update_options_t) -> Result<UpdateOptions> {
    if obj.is_null() { bail!("Null pointer: UpdateOptions must be set."); }
    let obj = unsafe { &*obj };
    let result = UpdateOptions {
        AllowVersionDowngrade: obj.AllowVersionDowngrade,
        ExplicitChannel: c_to_String(obj.ExplicitChannel).ok(),
        MaximumDeltasBeforeFallback: obj.MaximumDeltasBeforeFallback,
    };
    Ok(result)
}

#[rustfmt::skip]
pub fn c_to_UpdateOptions_vec(obj: *mut *mut vpkc_update_options_t, count: size_t) -> Result<Vec<UpdateOptions>> {
    if obj.is_null() || count == 0 { return Ok(Vec::new()); }
    let mut assets = Vec::with_capacity(count as usize);
    for i in 0..count {
        let ptr = unsafe { *obj.add(i as usize) };
        assets.push(c_to_UpdateOptions(ptr)?);
    }
    Ok(assets)
}

#[rustfmt::skip]
pub unsafe fn allocate_UpdateOptions<'a, T: Into<Option<&'a UpdateOptions>>>(dto: T) -> *mut vpkc_update_options_t {
    let dto = dto.into();
    if dto.is_none() {
        return std::ptr::null_mut();
    }
    log::debug!("vpkc_update_options_t allocated");
    let dto = dto.unwrap();
    let obj = libc::malloc(size_of::<vpkc_update_options_t>()) as *mut vpkc_update_options_t;
    (*obj).AllowVersionDowngrade = dto.AllowVersionDowngrade;
    (*obj).ExplicitChannel = allocate_String(&dto.ExplicitChannel);
    (*obj).MaximumDeltasBeforeFallback = dto.MaximumDeltasBeforeFallback;
    obj
}

#[rustfmt::skip]
pub unsafe fn allocate_UpdateOptions_vec(dto: &Vec<UpdateOptions>, count: *mut size_t) -> *mut *mut vpkc_update_options_t {
    if dto.is_empty() {
        *count = 0;
        return std::ptr::null_mut(); 
    }
    log::debug!("vpkc_update_options_t vector allocated");
    let count_value = dto.len() as size_t;
    *count = count_value;
    let mut assets = Vec::with_capacity(count_value as usize);
    for i in 0..count_value {
        let ptr = allocate_UpdateOptions(&dto[i as usize]);
        assets.push(ptr);
    }
    let ptr = assets.as_mut_ptr();
    std::mem::forget(assets);
    ptr
}

#[rustfmt::skip]
pub unsafe fn free_UpdateOptions(obj: *mut vpkc_update_options_t) {
    if obj.is_null() { return; }
    
    free_String((*obj).ExplicitChannel);
    
    libc::free(obj as *mut c_void);
    log::debug!("vpkc_update_options_t freed");
}

#[rustfmt::skip]
pub unsafe fn free_UpdateOptions_vec(obj: *mut *mut vpkc_update_options_t, count: size_t) {
    if obj.is_null() || count == 0 { return; }
    let vec = Vec::from_raw_parts(obj, count as usize, count as usize);
    for i in 0..count {
        let ptr = *vec.get_unchecked(i as usize);
        free_UpdateOptions(ptr);
    }
    log::debug!("vpkc_update_options_t vector freed");
}
// !! AUTO-GENERATED-END RUST_TYPES
