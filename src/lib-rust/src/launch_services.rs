//! LaunchServices registration of the macOS association helper bundle(s).
//!
//! LaunchServices never scans the nested `Contents/Helpers/*.app` of an app on its own (registering or launching
//! the outer bundle leaves the helper unknown, verified on macOS 26), so the helper's URL schemes / document types
//! only become bindable through an explicit `LSRegisterURL`: at every installed app start (`VelopackApp::run`),
//! from UpdateMac after an apply, and from the `.pkg` postinstall.

use std::path::{Path, PathBuf};

/// Every `Contents/Helpers/*.app` under `root` with a `Contents/MacOS/AssocHelper` executable, sorted.
pub fn find_association_helpers(root: &Path) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(root.join("Contents").join("Helpers")) else {
        return Vec::new();
    };
    let mut helpers: Vec<PathBuf> = entries
        .flatten()
        .map(|e| e.path())
        .filter(|p| p.extension().map(|e| e.eq_ignore_ascii_case("app")).unwrap_or(false))
        .filter(|p| p.join("Contents").join("MacOS").join("AssocHelper").is_file())
        .collect();
    helpers.sort();
    helpers
}

/// Registers the app bundle at `root` and every nested association helper bundle with LaunchServices, so the
/// helper's URL schemes / document types are known. Does nothing when the bundle has no helper. Failures are
/// logged, never returned.
#[cfg(target_os = "macos")]
pub fn register_association_helpers(root: &Path) {
    let helpers = find_association_helpers(root);
    if helpers.is_empty() {
        return;
    }
    info!("Registering {:?} and {} association helper(s) with LaunchServices.", root, helpers.len());
    ls_register(root);
    for helper in helpers {
        ls_register(&helper);
    }
}

#[cfg(target_os = "macos")]
fn ls_register(path: &Path) {
    use std::ffi::{c_char, c_void, CString};
    use std::os::unix::ffi::OsStrExt;

    #[link(name = "CoreFoundation", kind = "framework")]
    extern "C" {
        fn CFStringCreateWithCString(alloc: *const c_void, c_str: *const c_char, encoding: u32) -> *const c_void;
        fn CFURLCreateWithFileSystemPath(alloc: *const c_void, path: *const c_void, style: isize, is_directory: u8) -> *const c_void;
        fn CFRelease(cf: *const c_void);
    }
    #[link(name = "CoreServices", kind = "framework")]
    extern "C" {
        fn LSRegisterURL(url: *const c_void, update: u8) -> i32;
    }
    const K_CF_STRING_ENCODING_UTF8: u32 = 0x0800_0100;
    const K_CF_URL_POSIX_PATH_STYLE: isize = 0;

    let Ok(c_path) = CString::new(path.as_os_str().as_bytes()) else {
        warn!("LSRegisterURL({:?}) skipped: the path contains a NUL byte.", path);
        return;
    };
    // SAFETY: plain CoreFoundation calls; every object created here is released here, and nothing is retained
    // past the call.
    let status = unsafe {
        let cf_path = CFStringCreateWithCString(std::ptr::null(), c_path.as_ptr(), K_CF_STRING_ENCODING_UTF8);
        if cf_path.is_null() {
            warn!("LSRegisterURL({:?}) skipped: the path is not valid UTF-8.", path);
            return;
        }
        let url = CFURLCreateWithFileSystemPath(std::ptr::null(), cf_path, K_CF_URL_POSIX_PATH_STYLE, 1);
        CFRelease(cf_path);
        if url.is_null() {
            warn!("LSRegisterURL({:?}) skipped: the path is not a valid file URL.", path);
            return;
        }
        let status = LSRegisterURL(url, 1);
        CFRelease(url);
        status
    };
    if status != 0 {
        warn!("LSRegisterURL({:?}) failed with OSStatus {}.", path, status);
    }
}

#[cfg(test)]
mod tests {
    use super::find_association_helpers;

    #[test]
    fn find_association_helpers_requires_helper_executable() {
        let base = std::env::temp_dir().join(format!("velopack_assoc_helpers_{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&base);
        let root = base.join("My App.app");
        assert!(find_association_helpers(&root).is_empty());

        let helpers = root.join("Contents").join("Helpers");
        let good = helpers.join("My App.app");
        std::fs::create_dir_all(good.join("Contents").join("MacOS")).unwrap();
        std::fs::write(good.join("Contents").join("MacOS").join("AssocHelper"), b"").unwrap();
        std::fs::create_dir_all(helpers.join("Other.app").join("Contents").join("MacOS")).unwrap();
        std::fs::create_dir_all(helpers.join("NotABundle")).unwrap();

        assert_eq!(find_association_helpers(&root), vec![good]);
        let _ = std::fs::remove_dir_all(&base);
    }
}
