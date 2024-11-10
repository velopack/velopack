use std::ffi::{c_char, c_void, CStr, CString};
use std::path::PathBuf;
use velopack::{locator::VelopackLocatorConfig, UpdateInfo, UpdateOptions, VelopackAsset};

pub fn c_to_string_opt(psz: *const c_char) -> Option<String> {
    if psz.is_null() {
        return None;
    }
    let cstr = unsafe { CStr::from_ptr(psz) };
    Some(String::from_utf8_lossy(cstr.to_bytes()).to_string())
}

pub fn c_to_string(psz: *const c_char) -> String {
    c_to_string_opt(psz).unwrap_or_default()
}

pub fn c_to_pathbuf(psz: *const c_char) -> PathBuf {
    PathBuf::from(c_to_string(psz))
}

pub fn string_to_cstr(s: &str) -> *mut c_char {
    let cstr = CString::new(s).unwrap();
    cstr.into_raw()
}

pub fn free_cstr(psz: *mut c_char) {
    if !psz.is_null() {
        let _ = unsafe { CString::from_raw(psz) };
    }
}

pub fn allocate_string(s: String, psz: *mut *mut c_char) {
    if psz.is_null() {
        return;
    }
    unsafe { *psz = string_to_cstr(&s) };
}

pub fn allocate_string_opt(s: Option<String>, psz: *mut *mut c_char) {
    if let Some(s) = s {
        allocate_string(s, psz);
    }
}

pub unsafe fn free_string(psz: *mut *mut c_char) {
    if !psz.is_null() {
        free_cstr(*psz);
    }
}

pub fn allocate_pathbuf(p: PathBuf, psz: *mut *mut c_char) {
    allocate_string(p.to_string_lossy().to_string(), psz);
}

pub unsafe fn free_pathbuf(psz: *mut *mut c_char) {
    free_string(psz);
}

pub fn c_to_string_array_opt(p_args: *mut *mut c_char, c_args: usize) -> Option<Vec<String>> {
    if p_args.is_null() || c_args == 0 {
        return None;
    }

    let mut args = Vec::with_capacity(c_args);
    for i in 0..c_args {
        if let Some(arg) = c_to_string_opt(unsafe { *p_args.add(i) }) {
            args.push(arg);
        }
    }

    Some(args)
}

pub fn return_cstr(psz: *mut c_char, c: usize, s: &str) -> usize {
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

pub type vpkc_progress_callback_t = extern "C" fn(p_user_data: *mut c_void, progress: usize);

pub type vpkc_log_callback_t = extern "C" fn(p_user_data: *mut c_void, psz_level: *const c_char, psz_message: *const c_char);

pub type vpkc_hook_callback_t = extern "C" fn(p_user_data: *mut c_void, psz_app_version: *const c_char);

// !! AUTO-GENERATED-START RUST_TYPES
#[rustfmt::skip]
#[repr(C)]
pub struct vpkc_locator_config_t {
    pub RootAppDir: *mut c_char,
    pub UpdateExePath: *mut c_char,
    pub PackagesDir: *mut c_char,
    pub ManifestPath: *mut c_char,
    pub CurrentBinaryDir: *mut c_char,
    pub IsPortable: bool,
}

#[rustfmt::skip]
pub fn c_to_velopacklocatorconfig(obj: &vpkc_locator_config_t) -> VelopackLocatorConfig {
    VelopackLocatorConfig {
        RootAppDir: c_to_pathbuf(obj.RootAppDir),
        UpdateExePath: c_to_pathbuf(obj.UpdateExePath),
        PackagesDir: c_to_pathbuf(obj.PackagesDir),
        ManifestPath: c_to_pathbuf(obj.ManifestPath),
        CurrentBinaryDir: c_to_pathbuf(obj.CurrentBinaryDir),
        IsPortable: obj.IsPortable,
    }
}

#[rustfmt::skip]
pub fn c_to_velopacklocatorconfig_opt(obj: *mut vpkc_locator_config_t) -> Option<VelopackLocatorConfig> {
    if obj.is_null() { return None; }
    Some(c_to_velopacklocatorconfig(unsafe { &*obj }))
}

#[rustfmt::skip]
pub unsafe fn allocate_velopacklocatorconfig(dto: VelopackLocatorConfig, obj: *mut vpkc_locator_config_t) {
    if obj.is_null() { return; }
    allocate_pathbuf(dto.RootAppDir, &mut (*obj).RootAppDir);
    allocate_pathbuf(dto.UpdateExePath, &mut (*obj).UpdateExePath);
    allocate_pathbuf(dto.PackagesDir, &mut (*obj).PackagesDir);
    allocate_pathbuf(dto.ManifestPath, &mut (*obj).ManifestPath);
    allocate_pathbuf(dto.CurrentBinaryDir, &mut (*obj).CurrentBinaryDir);
    (*obj).IsPortable = dto.IsPortable;
}

#[rustfmt::skip]
pub unsafe fn free_velopacklocatorconfig(obj: *mut vpkc_locator_config_t) {
    if obj.is_null() { return; }
    free_pathbuf(&mut (*obj).RootAppDir);
    free_pathbuf(&mut (*obj).UpdateExePath);
    free_pathbuf(&mut (*obj).PackagesDir);
    free_pathbuf(&mut (*obj).ManifestPath);
    free_pathbuf(&mut (*obj).CurrentBinaryDir);
}

#[rustfmt::skip]
#[repr(C)]
pub struct vpkc_asset_t {
    pub PackageId: *mut c_char,
    pub Version: *mut c_char,
    pub Type: *mut c_char,
    pub FileName: *mut c_char,
    pub SHA1: *mut c_char,
    pub SHA256: *mut c_char,
    pub Size: u64,
    pub NotesMarkdown: *mut c_char,
    pub NotesHtml: *mut c_char,
}

#[rustfmt::skip]
pub fn c_to_velopackasset(obj: &vpkc_asset_t) -> VelopackAsset {
    VelopackAsset {
        PackageId: c_to_string(obj.PackageId),
        Version: c_to_string(obj.Version),
        Type: c_to_string(obj.Type),
        FileName: c_to_string(obj.FileName),
        SHA1: c_to_string(obj.SHA1),
        SHA256: c_to_string(obj.SHA256),
        Size: obj.Size,
        NotesMarkdown: c_to_string(obj.NotesMarkdown),
        NotesHtml: c_to_string(obj.NotesHtml),
    }
}

#[rustfmt::skip]
pub fn c_to_velopackasset_opt(obj: *mut vpkc_asset_t) -> Option<VelopackAsset> {
    if obj.is_null() { return None; }
    Some(c_to_velopackasset(unsafe { &*obj }))
}

#[rustfmt::skip]
pub unsafe fn allocate_velopackasset(dto: VelopackAsset, obj: *mut vpkc_asset_t) {
    if obj.is_null() { return; }
    allocate_string(dto.PackageId, &mut (*obj).PackageId);
    allocate_string(dto.Version, &mut (*obj).Version);
    allocate_string(dto.Type, &mut (*obj).Type);
    allocate_string(dto.FileName, &mut (*obj).FileName);
    allocate_string(dto.SHA1, &mut (*obj).SHA1);
    allocate_string(dto.SHA256, &mut (*obj).SHA256);
    (*obj).Size = dto.Size;
    allocate_string(dto.NotesMarkdown, &mut (*obj).NotesMarkdown);
    allocate_string(dto.NotesHtml, &mut (*obj).NotesHtml);
}

#[rustfmt::skip]
pub unsafe fn free_velopackasset(obj: *mut vpkc_asset_t) {
    if obj.is_null() { return; }
    free_string(&mut (*obj).PackageId);
    free_string(&mut (*obj).Version);
    free_string(&mut (*obj).Type);
    free_string(&mut (*obj).FileName);
    free_string(&mut (*obj).SHA1);
    free_string(&mut (*obj).SHA256);
    free_string(&mut (*obj).NotesMarkdown);
    free_string(&mut (*obj).NotesHtml);
}

#[rustfmt::skip]
#[repr(C)]
pub struct vpkc_update_info_t {
    pub TargetFullRelease: vpkc_asset_t,
    pub IsDowngrade: bool,
}

#[rustfmt::skip]
pub fn c_to_updateinfo(obj: &vpkc_update_info_t) -> UpdateInfo {
    UpdateInfo {
        TargetFullRelease: c_to_velopackasset(&obj.TargetFullRelease),
        IsDowngrade: obj.IsDowngrade,
    }
}

#[rustfmt::skip]
pub fn c_to_updateinfo_opt(obj: *mut vpkc_update_info_t) -> Option<UpdateInfo> {
    if obj.is_null() { return None; }
    Some(c_to_updateinfo(unsafe { &*obj }))
}

#[rustfmt::skip]
pub unsafe fn allocate_updateinfo(dto: UpdateInfo, obj: *mut vpkc_update_info_t) {
    if obj.is_null() { return; }
    allocate_velopackasset(dto.TargetFullRelease, &mut (*obj).TargetFullRelease);
    (*obj).IsDowngrade = dto.IsDowngrade;
}

#[rustfmt::skip]
pub unsafe fn free_updateinfo(obj: *mut vpkc_update_info_t) {
    if obj.is_null() { return; }
    free_velopackasset(&mut (*obj).TargetFullRelease);
}

#[rustfmt::skip]
#[repr(C)]
pub struct vpkc_update_options_t {
    pub AllowVersionDowngrade: bool,
    pub ExplicitChannel: *mut c_char,
}

#[rustfmt::skip]
pub fn c_to_updateoptions(obj: &vpkc_update_options_t) -> UpdateOptions {
    UpdateOptions {
        AllowVersionDowngrade: obj.AllowVersionDowngrade,
        ExplicitChannel: c_to_string_opt(obj.ExplicitChannel),
    }
}

#[rustfmt::skip]
pub fn c_to_updateoptions_opt(obj: *mut vpkc_update_options_t) -> Option<UpdateOptions> {
    if obj.is_null() { return None; }
    Some(c_to_updateoptions(unsafe { &*obj }))
}

#[rustfmt::skip]
pub unsafe fn allocate_updateoptions(dto: UpdateOptions, obj: *mut vpkc_update_options_t) {
    if obj.is_null() { return; }
    (*obj).AllowVersionDowngrade = dto.AllowVersionDowngrade;
    allocate_string_opt(dto.ExplicitChannel, &mut (*obj).ExplicitChannel);
}

#[rustfmt::skip]
pub unsafe fn free_updateoptions(obj: *mut vpkc_update_options_t) {
    if obj.is_null() { return; }
    free_string(&mut (*obj).ExplicitChannel);
}
// !! AUTO-GENERATED-END RUST_TYPES
