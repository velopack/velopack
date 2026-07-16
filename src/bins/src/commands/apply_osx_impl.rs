use crate::{dialogs, shared};
use anyhow::{bail, Result};
use std::{
    fs, io,
    path::{Path, PathBuf},
    process::Command,
};
use velopack::{bundle, locator::VelopackLocator};

/// Atomically exchanges two paths in a single syscall (renamex_np with RENAME_SWAP).
fn atomic_swap(a: &Path, b: &Path) -> io::Result<()> {
    use std::os::unix::ffi::OsStrExt;
    let a_c = std::ffi::CString::new(a.as_os_str().as_bytes())?;
    let b_c = std::ffi::CString::new(b.as_os_str().as_bytes())?;
    if unsafe { libc::renamex_np(a_c.as_ptr(), b_c.as_ptr(), libc::RENAME_SWAP) } != 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(())
}

/// Sets the modification and access time of the item to now. LaunchServices uses this to
/// notice that a new version of the app is present (updating registered URL schemes, etc).
fn touch(path: &Path) -> io::Result<()> {
    use std::os::unix::ffi::OsStrExt;
    let path_c = std::ffi::CString::new(path.as_os_str().as_bytes())?;
    if unsafe { libc::utimes(path_c.as_ptr(), std::ptr::null()) } != 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(())
}

/// Replaces the installed .app bundle with the newly extracted one, using the same strategy
/// as the Sparkle updater: a single atomic swap syscall, so there is never a moment where the
/// bundle is missing or incomplete. Replacing the bundle non-atomically lets the Dock observe
/// the app vanish, which orphans its Dock tiles and causes it to treat every updated version
/// as a new app (e.g. duplicate "Recent Applications" icons, #966).
fn replace_bundle(root_path: &Path, tmp_path_old: &Path, tmp_path_new: &Path) -> io::Result<()> {
    match atomic_swap(root_path, tmp_path_new) {
        Ok(()) => return Ok(()), // the old bundle is now in tmp_path_new, deleted by the caller
        Err(e) if e.kind() == io::ErrorKind::PermissionDenied => return Err(e),
        Err(e) => warn!("Atomic bundle swap failed ({}), falling back to a non-atomic rename.", e),
    }
    fs::rename(root_path, tmp_path_old)?;
    if let Err(e) = fs::rename(tmp_path_new, root_path) {
        // restore the old bundle, otherwise the caller's cleanup would delete the only copy of the app
        let _ = fs::rename(tmp_path_old, root_path);
        return Err(e);
    }
    Ok(())
}

pub fn apply_package_impl(locator: &VelopackLocator, pkg: &PathBuf, _hook_mode: super::HookRunMode) -> Result<VelopackLocator> {
    let _mutex = locator.try_get_exclusive_lock()?;
    let root_path = locator.get_root_dir();
    let tmp_path_new = locator.get_temp_dir_rand16();
    let tmp_path_old = locator.get_temp_dir_rand16();
    let mut bundle = bundle::load_bundle_from_file(pkg).map_err(|e| {
        warn!("Deleting package {:?} to prevent update loop: {}", pkg, e);
        let _ = fs::remove_file(pkg);
        e
    })?;
    let manifest = bundle.read_manifest().map_err(|e| {
        warn!("Deleting package {:?} to prevent update loop: {}", pkg, e);
        let _ = fs::remove_file(pkg);
        e
    })?;
    let new_locator = locator.clone_self_with_new_manifest(&manifest);

    // show progress dialog
    let reporter = dialogs::progress::show_apply_progress(&manifest.title, &manifest.version.to_string());

    let action: Result<()> = (|| {
        // 1. extract the bundle to a temp dir
        fs::create_dir_all(&tmp_path_new)?;
        info!("Extracting bundle to {:?}", tmp_path_new);
        bundle.extract_lib_contents_to_path(&tmp_path_new, |p| reporter.set_progress(p))?;

        // 2. attempt to replace the current bundle with the new one
        reporter.set_indeterminate();
        let result: Result<()> = (|| {
            info!("Replacing bundle at {:?}", root_path);
            replace_bundle(&root_path, &tmp_path_old, &tmp_path_new)?;
            Ok(())
        })();

        let result = match result {
            Ok(()) => {
                info!("Bundle extracted successfully to {:?}", root_path);
                Ok(())
            }
            Err(e) => {
                // 3. if fails for permission error, try again escalated via osascript
                if shared::is_error_permission_denied(&e) {
                    error!(
                        "A permissions error occurred ({}), will attempt to elevate permissions and try again...",
                        e
                    );
                    dialogs::ask_user_to_elevate(&manifest.title, &manifest.version.to_string())?;
                    let script = format!(
                        "do shell script \"mv -f '{}' '{}' && mv -f '{}' '{}' && rm -rf '{}'\" with administrator privileges",
                        &root_path.to_string_lossy(),
                        &tmp_path_old.to_string_lossy(),
                        &tmp_path_new.to_string_lossy(),
                        &root_path.to_string_lossy(),
                        &tmp_path_old.to_string_lossy()
                    );
                    info!("Running elevated process via osascript: {}", script);
                    let output = Command::new("osascript").arg("-e").arg(&script).status()?;
                    if output.success() {
                        info!("Bundle applied successfully via osascript.");
                        Ok(())
                    } else {
                        bail!("elevated process failed: exited with code: {}", output);
                    }
                } else {
                    bail!("Failed to extract bundle ({})", e);
                }
            }
        };

        if result.is_ok() {
            // let LaunchServices know the bundle has changed (Sparkle does the same)
            if let Err(e) = touch(&root_path) {
                warn!("Failed to update bundle modification time ({})", e);
            }
        }
        result
    })();
    reporter.close();
    let _ = fs::remove_dir_all(&tmp_path_new);
    let _ = fs::remove_dir_all(&tmp_path_old);
    action?;
    Ok(new_locator)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_base(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("velopack_swap_test_{}_{}", name, std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn make_bundle(root: &Path, plist: &str) {
        fs::create_dir_all(root.join("Contents/MacOS")).unwrap();
        fs::write(root.join("Contents/Info.plist"), plist).unwrap();
    }

    #[test]
    fn test_replace_bundle_swaps_atomically() {
        let base = temp_base("swap");
        let root = base.join("MyApp.app");
        let tmp_old = base.join("tmp_old");
        let tmp_new = base.join("tmp_new");
        make_bundle(&root, "old-plist");
        make_bundle(&tmp_new, "new-plist");

        replace_bundle(&root, &tmp_old, &tmp_new).unwrap();

        assert_eq!(fs::read_to_string(root.join("Contents/Info.plist")).unwrap(), "new-plist");
        // the old bundle was exchanged into tmp_new (deleted later by the caller), tmp_old unused
        assert_eq!(fs::read_to_string(tmp_new.join("Contents/Info.plist")).unwrap(), "old-plist");
        assert!(!tmp_old.exists());

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn test_replace_bundle_restores_old_bundle_on_failure() {
        let base = temp_base("undo");
        let root = base.join("MyApp.app");
        let tmp_old = base.join("tmp_old");
        let tmp_new = base.join("tmp_new");
        make_bundle(&root, "old-plist");
        // tmp_new does not exist, so the swap and the fallback rename both fail

        assert!(replace_bundle(&root, &tmp_old, &tmp_new).is_err());
        assert_eq!(fs::read_to_string(root.join("Contents/Info.plist")).unwrap(), "old-plist");

        let _ = fs::remove_dir_all(&base);
    }
}
