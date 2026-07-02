use crate::{dialogs, shared};
use anyhow::{bail, Result};
use std::{
    ffi::OsString,
    fs, io,
    path::{Path, PathBuf},
    process::Command,
};
use velopack::{bundle, locator::VelopackLocator};

/// Printed to stdout by the elevated `swap` process on success. AuthorizationExecuteWithPrivileges
/// does not report the child's exit code, so the parent scans the output pipe for this instead.
pub const ELEVATED_SWAP_SUCCESS_MARKER: &str = "VELOPACK_ELEVATED_SWAP_SUCCESS";

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

/// Entry point for the elevated `swap` command. Replaces the bundle, then cleans up the
/// temporary directories (the parent process cannot, because after an update of a bundle
/// which required elevation they may contain root-owned files).
pub fn swap_bundles(root_path: &Path, tmp_path_old: &Path, tmp_path_new: &Path) -> Result<()> {
    replace_bundle(root_path, tmp_path_old, tmp_path_new)?;
    if let Err(e) = touch(root_path) {
        warn!("Failed to update bundle modification time ({})", e);
    }
    let _ = fs::remove_dir_all(tmp_path_new);
    let _ = fs::remove_dir_all(tmp_path_old);
    println!("{}", ELEVATED_SWAP_SUCCESS_MARKER);
    Ok(())
}

/// Minimal FFI for the Authorization Services APIs used to elevate the bundle swap.
/// Only the fully supported (non-deprecated) functions are linked at build time.
mod authorization {
    use std::os::raw::{c_char, c_void};

    pub type AuthorizationRef = *const c_void;
    pub type OSStatus = i32;

    #[repr(C)]
    pub struct AuthorizationItem {
        pub name: *const c_char,
        pub value_length: usize,
        pub value: *const c_void,
        pub flags: u32,
    }

    #[repr(C)]
    pub struct AuthorizationItemSet {
        pub count: u32,
        pub items: *const AuthorizationItem,
    }

    pub const FLAG_DEFAULTS: u32 = 0;
    pub const FLAG_INTERACTION_ALLOWED: u32 = 1 << 0;
    pub const FLAG_EXTEND_RIGHTS: u32 = 1 << 1;
    pub const ERR_SUCCESS: OSStatus = 0;
    pub const ERR_CANCELED: OSStatus = -60006;

    #[link(name = "Security", kind = "framework")]
    extern "C" {
        pub fn AuthorizationCreate(
            rights: *const AuthorizationItemSet,
            environment: *const AuthorizationItemSet,
            flags: u32,
            authorization: *mut AuthorizationRef,
        ) -> OSStatus;
        pub fn AuthorizationFree(authorization: AuthorizationRef, flags: u32) -> OSStatus;
    }

    /// AuthorizationExecuteWithPrivileges(auth, path_to_tool, options, arguments, communications_pipe)
    pub type ExecuteWithPrivilegesFn =
        unsafe extern "C" fn(AuthorizationRef, *const c_char, u32, *const *const c_char, *mut *mut libc::FILE) -> OSStatus;

    /// AuthorizationExecuteWithPrivileges has been deprecated since macOS 10.7 and could be removed
    /// from Security.framework in a future macOS release. It is resolved at runtime rather than
    /// linked, so that binaries already shipped inside apps can never fail to load because of it,
    /// and the caller can fall back to another elevation strategy if it ever disappears.
    pub fn find_execute_with_privileges() -> Option<ExecuteWithPrivilegesFn> {
        let name = b"AuthorizationExecuteWithPrivileges\0";
        let sym = unsafe { libc::dlsym(libc::RTLD_DEFAULT, name.as_ptr() as *const c_char) };
        if sym.is_null() {
            None
        } else {
            Some(unsafe { std::mem::transmute::<*mut c_void, ExecuteWithPrivilegesFn>(sym) })
        }
    }

    pub struct Guard(pub AuthorizationRef);
    impl Drop for Guard {
        fn drop(&mut self) {
            unsafe { AuthorizationFree(self.0, FLAG_DEFAULTS) };
        }
    }
}

enum SwapAttempt {
    Success,
    Cancelled,
    Failed(String),
}

/// Re-runs this executable as root to swap the bundle, and waits for it to complete. Elevation is
/// performed with Authorization Services where available (the user sees the system authentication
/// dialog with a prompt naming the app), falling back to `osascript` otherwise. Both mechanisms
/// run the same hidden `swap` command, so elevated updates get the same atomic swap behavior.
fn run_elevated_swap(app_title: &str, root_path: &Path, tmp_path_old: &Path, tmp_path_new: &Path) -> Result<()> {
    let exe = std::env::current_exe()?;
    // the child logs to the parent's pipe / stdout; a root process must not write
    // to the shared log file or it would no longer be writable by the user
    let args: Vec<OsString> = vec![
        "swap".into(),
        "--rootDir".into(),
        root_path.into(),
        "--old".into(),
        tmp_path_old.into(),
        "--new".into(),
        tmp_path_new.into(),
        "--log".into(),
        "/dev/null".into(),
        "--silent".into(),
    ];

    if let Some(execute_with_privileges) = authorization::find_execute_with_privileges() {
        match run_swap_via_authorization(execute_with_privileges, app_title, &exe, &args) {
            SwapAttempt::Success => {
                info!("Bundle applied successfully via elevated process.");
                return Ok(());
            }
            SwapAttempt::Cancelled => bail!("The user declined the elevation request."),
            SwapAttempt::Failed(reason) => {
                warn!("Elevation via Authorization Services failed ({}), falling back to osascript.", reason);
            }
        }
    } else {
        warn!("AuthorizationExecuteWithPrivileges is not available on this system, falling back to osascript.");
    }

    run_swap_via_osascript(&exe, root_path, tmp_path_old, tmp_path_new)
}

fn run_swap_via_authorization(
    execute_with_privileges: authorization::ExecuteWithPrivilegesFn,
    app_title: &str,
    exe: &Path,
    args: &[OsString],
) -> SwapAttempt {
    use authorization::*;
    use std::ffi::CString;
    use std::io::Read;
    use std::os::raw::{c_char, c_void};
    use std::os::unix::ffi::OsStrExt;

    let to_cstring = |bytes: &[u8]| CString::new(bytes).map_err(|_| "path contains a NUL byte".to_string());
    let exe_c = match to_cstring(exe.as_os_str().as_bytes()) {
        Ok(c) => c,
        Err(e) => return SwapAttempt::Failed(e),
    };
    let args_c = match args.iter().map(|a| to_cstring(a.as_bytes())).collect::<Result<Vec<_>, _>>() {
        Ok(c) => c,
        Err(e) => return SwapAttempt::Failed(e),
    };

    // "prompt" is kAuthorizationEnvironmentPrompt; the text is shown in the system dialog above the
    // password field. Like Sparkle, we forgo localization (the rest of the dialog is localized by the OS).
    let prompt = match to_cstring(format!("{} wants to install an update.\n\n", app_title).as_bytes()) {
        Ok(c) => c,
        Err(e) => return SwapAttempt::Failed(e),
    };
    let right = AuthorizationItem {
        name: b"system.privilege.admin\0".as_ptr() as *const c_char,
        value_length: 0,
        value: std::ptr::null(),
        flags: 0,
    };
    let rights = AuthorizationItemSet { count: 1, items: &right };
    let env_prompt = AuthorizationItem {
        name: b"prompt\0".as_ptr() as *const c_char,
        value_length: prompt.as_bytes().len(),
        value: prompt.as_ptr() as *const c_void,
        flags: 0,
    };
    let environment = AuthorizationItemSet {
        count: 1,
        items: &env_prompt,
    };

    // this shows the authentication dialog if necessary
    let mut auth: AuthorizationRef = std::ptr::null();
    let status = unsafe { AuthorizationCreate(&rights, &environment, FLAG_INTERACTION_ALLOWED | FLAG_EXTEND_RIGHTS, &mut auth) };
    if status == ERR_CANCELED {
        return SwapAttempt::Cancelled;
    }
    if status != ERR_SUCCESS || auth.is_null() {
        return SwapAttempt::Failed(format!("AuthorizationCreate returned {}", status));
    }
    let _guard = Guard(auth);

    // NULL-terminated argv; the tool path is passed separately and becomes argv[0]
    let mut argv: Vec<*const c_char> = args_c.iter().map(|c| c.as_ptr()).collect();
    argv.push(std::ptr::null());

    info!("Running elevated process: {:?} {:?}", exe, args);
    let mut pipe: *mut libc::FILE = std::ptr::null_mut();
    let status = unsafe { execute_with_privileges(auth, exe_c.as_ptr(), FLAG_DEFAULTS, argv.as_ptr(), &mut pipe) };
    if status != ERR_SUCCESS || pipe.is_null() {
        return SwapAttempt::Failed(format!("AuthorizationExecuteWithPrivileges returned {}", status));
    }

    // reading until EOF also waits for the elevated process to exit. taking ownership of the
    // file descriptor intentionally leaks the (small) FILE struct itself.
    let mut output = String::new();
    use std::os::fd::FromRawFd;
    let mut file = unsafe { std::fs::File::from_raw_fd(libc::fileno(pipe)) };
    let _ = file.read_to_string(&mut output);
    for line in output.lines().filter(|l| !l.trim().is_empty()) {
        info!("[elevated] {}", line);
    }

    if output.contains(ELEVATED_SWAP_SUCCESS_MARKER) {
        SwapAttempt::Success
    } else {
        SwapAttempt::Failed("the elevated process did not report success".to_string())
    }
}

fn run_swap_via_osascript(exe: &Path, root_path: &Path, tmp_path_old: &Path, tmp_path_new: &Path) -> Result<()> {
    let script = format!(
        "do shell script \"'{}' swap --rootDir '{}' --old '{}' --new '{}' --log /dev/null --silent\" with administrator privileges",
        exe.to_string_lossy(),
        root_path.to_string_lossy(),
        tmp_path_old.to_string_lossy(),
        tmp_path_new.to_string_lossy()
    );
    info!("Running elevated process via osascript: {}", script);
    let output = Command::new("osascript").arg("-e").arg(&script).status()?;
    if output.success() {
        info!("Bundle applied successfully via osascript.");
        Ok(())
    } else {
        bail!("elevated process failed: exited with code: {}", output);
    }
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
        info!("Extracting bundle to {:?}", &tmp_path_new);
        bundle.extract_lib_contents_to_path(&tmp_path_new, |p| reporter.set_progress(p))?;

        // 2. attempt to replace the current bundle with the new one
        reporter.set_indeterminate();
        let result: Result<()> = (|| {
            info!("Replacing bundle at {:?}", &root_path);
            replace_bundle(&root_path, &tmp_path_old, &tmp_path_new)?;
            Ok(())
        })();

        match result {
            Ok(()) => {
                info!("Bundle extracted successfully to {:?}", &root_path);
                // let LaunchServices know the bundle has changed (Sparkle does the same)
                if let Err(e) = touch(&root_path) {
                    warn!("Failed to update bundle modification time ({})", e);
                }
                Ok(())
            }
            Err(e) => {
                // 3. if fails for permission error, re-run this executable as root to swap the bundle
                if shared::is_error_permission_denied(&e) {
                    error!(
                        "A permissions error occurred ({}), will attempt to elevate permissions and try again...",
                        e
                    );
                    dialogs::ask_user_to_elevate(&manifest.title, &manifest.version.to_string())?;
                    run_elevated_swap(&manifest.title, &root_path, &tmp_path_old, &tmp_path_new)
                } else {
                    bail!("Failed to extract bundle ({})", e);
                }
            }
        }
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
