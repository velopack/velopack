use std::path::PathBuf;

use crate::{manifest::{self, Manifest}, util, Error};

#[derive(Clone)]
/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
pub struct VelopackLocator {
    /// The root directory of the current app.
    pub root_app_dir: PathBuf,
    /// The path to the Update.exe binary.
    pub update_exe_path: PathBuf,
    /// The path to the packages directory.
    pub packages_dir: PathBuf,
    /// The current app manifest.
    pub manifest: Manifest,
}

/// Default log location for Velopack code.
pub fn default_log_location() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        let mut my_dir = std::env::current_exe().unwrap();
        my_dir.pop();
        my_dir.pop();
        return my_dir.join("Velopack.log");
    }
    #[cfg(target_os = "linux")]
    {
        return std::path::Path::new("/tmp/velopack.log").to_path_buf();
    }
    #[cfg(target_os = "macos")]
    {
        #[allow(deprecated)]
        let mut user_home = std::env::home_dir().expect("Could not locate user home directory via $HOME or /etc/passwd");
        user_home.push("Library");
        user_home.push("Logs");
        user_home.push("velopack.log");
        return user_home;
    }
}

#[cfg(target_os = "windows")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate() -> Result<VelopackLocator, Error> {
    // check if Update.exe exists in parent dir, if it does, that's the root dir.
    let mut path = std::env::current_exe()?;
    path.pop(); // current dir
    path.pop(); // root dir
    if (path.join("Update.exe")).exists() {
        info!("Found Update.exe in parent directory: {}", path.to_string_lossy());
        return Ok(VelopackLocator {
            root_app_dir: path.clone(),
            update_exe_path: path.join("Update.exe"),
            packages_dir: path.join("packages"),
            manifest: read_current_manifest(&path.join("current").join("sq.version"))?,
        });
    }

    // see if we can find the current dir in the path, maybe we're more nested than that.
    path = std::env::current_exe()?;
    let path = path.to_string_lossy();
    let idx = path.rfind("\\current\\");
    if let Some(i) = idx {
        let maybe_root = &path[..i];
        let maybe_root = PathBuf::from(maybe_root);
        if (maybe_root.join("Update.exe")).exists() {
            info!("Found Update.exe in parent directory: {}", maybe_root.to_string_lossy());
            return Ok(VelopackLocator {
                root_app_dir: maybe_root.clone(),
                update_exe_path: maybe_root.join("Update.exe"),
                packages_dir: maybe_root.join("packages"),
                manifest: read_current_manifest(&maybe_root.join("current").join("sq.version"))?,
            });
        }
    }

    Err(Error::MissingUpdateExe)
}

#[cfg(target_os = "linux")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate() -> Result<VelopackLocator> {
    let path = std::env::current_exe()?;
    let path = path.to_string_lossy();
    let idx = path.rfind("/usr/bin/");
    if idx.is_none() {
        bail!("Unable to locate '/usr/bin/' directory in path: {}", path);
    }
    let idx = idx.unwrap();
    let root_app_dir = PathBuf::from(path[..idx].to_string());
    let contents_dir = root_app_dir.join("usr").join("bin");
    let update_exe_path = contents_dir.join("UpdateNix");
    let metadata_path = contents_dir.join("sq.version");

    if !update_exe_path.exists() {
        bail!("Unable to locate UpdateMac in directory: {}", contents_dir.to_string_lossy());
    }

    let app = read_current_manifest(&metadata_path)?;
    Ok(VelopackLocator {
        root_app_dir,
        update_exe_path,
        packages_dir: PathBuf::from("/var/tmp/velopack").join(&app.id).join("packages"),
        manifest: app,
    })
}

#[cfg(target_os = "macos")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate() -> Result<VelopackLocator> {
    let path = std::env::current_exe()?;
    let path = path.to_string_lossy();
    let idx = path.rfind(".app/");
    if idx.is_none() {
        bail!("Unable to locate '.app' directory in path: {}", path);
    }
    let idx = idx.unwrap();
    let path = path[..(idx + 4)].to_string();

    let root_app_dir = PathBuf::from(&path);
    let contents_dir = root_app_dir.join("Contents").join("MacOS");
    let update_exe_path = contents_dir.join("UpdateMac");
    let metadata_path = contents_dir.join("sq.version");

    if !update_exe_path.exists() {
        bail!("Unable to locate UpdateMac in directory: {}", contents_dir.to_string_lossy());
    }

    let app = read_current_manifest(&metadata_path)?;

    #[allow(deprecated)]
    let mut packages_dir = std::env::home_dir().expect("Could not locate user home directory via $HOME or /etc/passwd");
    packages_dir.push("Library");
    packages_dir.push("Caches");
    packages_dir.push("velopack");
    packages_dir.push(&app.id);
    packages_dir.push("packages");

    Ok(VelopackLocator {
        root_app_dir,
        update_exe_path,
        packages_dir: packages_dir,
        manifest: app,
    })
}

fn read_current_manifest(nuspec_path: &PathBuf) -> Result<Manifest, Error> {
    if nuspec_path.exists() {
        if let Ok(nuspec) = util::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
            return Ok(manifest::read_manifest_from_string(&nuspec)?);
        }
    }
    Err(Error::MissingNuspec)
}
