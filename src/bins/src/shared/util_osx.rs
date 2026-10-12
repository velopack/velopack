use anyhow::{anyhow, bail, Result};
use objc2::rc::Retained;
use objc2_foundation::{NSString, NSURL};
use std::{
    ffi::OsString,
    path::{Path, PathBuf},
    process::Command as Process,
    time::Duration,
};
use velopack::locator::VelopackLocator;

#[link(name = "CoreServices", kind = "framework")]
extern "C" {
    fn LSRegisterURL(url: *const std::ffi::c_void, update: u8) -> i32;
}

/// Registers the app bundle at `root` and every nested association helper bundle
/// (`Contents/Helpers/*.app` containing `Contents/MacOS/AssocHelper`) with LaunchServices, so the
/// helper's URL schemes / document types are known after an install or update. Does nothing when the
/// bundle has no helper. Failures are logged, never returned.
pub fn register_association_helpers(root: &Path) {
    let helpers = find_association_helpers(root);
    if helpers.is_empty() {
        return;
    }
    info!("Registering {:?} and {} association helper(s) with LaunchServices.", root, helpers.len());
    // LaunchServices scans the nested bundles of a registered app, so the outer bundle goes first;
    // the explicit helper registrations are belt and braces.
    ls_register(root);
    for helper in helpers {
        ls_register(&helper);
    }
}

fn find_association_helpers(root: &Path) -> Vec<PathBuf> {
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

fn ls_register(path: &Path) {
    let url = NSURL::fileURLWithPath(&NSString::from_str(&path.to_string_lossy()));
    // SAFETY: NSURL is toll-free bridged to CFURLRef and stays alive for the call.
    let status = unsafe { LSRegisterURL(Retained::as_ptr(&url).cast(), 1) };
    if status != 0 {
        warn!("LSRegisterURL({:?}) failed with OSStatus {}.", path, status);
    }
}

pub fn wait_for_pid_to_exit(pid: u32, ms_to_wait: u32) -> Result<()> {
    info!("Waiting {}ms for process ({}) to exit.", ms_to_wait, pid);
    let mut handle = waitpid_any::WaitHandle::open(pid.try_into()?)?;
    let result = handle.wait_timeout(Duration::from_millis(ms_to_wait as u64))?;
    if result.is_some() {
        info!("Parent process exited.");
        Ok(())
    } else {
        bail!("Parent process timed out.");
    }
}

pub fn wait_for_parent_to_exit(ms_to_wait: u32) -> Result<()> {
    let id = std::os::unix::process::parent_id();
    info!("Attempting to wait for parent process ({}) to exit.", id);
    if id > 1 {
        wait_for_pid_to_exit(id, ms_to_wait)?;
    }
    Ok(())
}

pub fn force_stop_package<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref();
    let command = format!("quit app \"{}\"", root_dir.to_string_lossy());
    Process::new("/usr/bin/osascript")
        .arg("-e")
        .arg(command)
        .spawn()
        .map_err(|z| anyhow!("Failed to stop application ({}).", z))?;
    Ok(())
}

pub fn start_package(locator: &VelopackLocator, exe_args: Option<Vec<OsString>>, set_env: Option<&str>) -> Result<()> {
    let root_dir = locator.get_root_dir();
    let mut args: Vec<OsString> = vec!["-n".into(), root_dir.into()];
    if let Some(a) = exe_args {
        args.push("--args".into());
        args.extend(a);
    }
    info!("Starting application: open {:?}", args);
    let mut psi = Process::new("/usr/bin/open");
    psi.args(args);
    if let Some(env) = set_env {
        psi.env(env, "true");
    }
    psi.spawn().map_err(|z| anyhow!("Failed to start application ({}).", z))?;
    Ok(())
}

#[test]
fn test_find_association_helpers_requires_helper_executable() {
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

#[test]
#[ignore]
fn test_start_and_stop_package() {
    let mani = velopack::bundle::Manifest::default();
    let paths = velopack::locator::VelopackLocatorConfig {
        RootAppDir: std::path::PathBuf::from("/Applications/Calcbot.app"),
        ..Default::default()
    };
    let locator = VelopackLocator::new_with_manifest(paths, mani);

    let _ = force_stop_package(locator.get_root_dir());

    fn is_running() -> bool {
        let output = Process::new("pgrep").arg("-f").arg("Calcbot.app").output().unwrap();
        !output.stdout.is_empty()
    }

    std::thread::sleep(Duration::from_secs(1));
    assert!(!is_running());
    std::thread::sleep(Duration::from_secs(1));
    start_package(&locator, None, None).unwrap();
    std::thread::sleep(Duration::from_secs(1));
    assert!(is_running());
    std::thread::sleep(Duration::from_secs(1));
    force_stop_package(locator.get_root_dir()).unwrap();
    std::thread::sleep(Duration::from_secs(1));
    assert!(!is_running());
}
