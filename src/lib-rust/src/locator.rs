use std::path::{Path, PathBuf};

use crate::{
    manifest::{self, Manifest},
    util, Error,
};

/// Returns the default channel name for the current OS.
pub fn default_channel_name() -> String {
    #[cfg(target_os = "windows")]
    return "win".to_owned();
    #[cfg(target_os = "linux")]
    return "linux".to_owned();
    #[cfg(target_os = "macos")]
    return "osx".to_owned();
}

/// Default log location for Velopack on the current OS.
pub fn default_log_location() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        let mut my_exe = std::env::current_exe().expect("Could not locate current executable");
        if let Ok(locator) = auto_locate(&my_exe) {
            return locator.RootAppDir.join("Velopack.log");
        }

        // If we can't locate the current app, we write to the parent directory.
        my_exe.pop();
        my_exe.pop();
        return my_exe.join("Velopack.log");
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


/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
#[allow(non_snake_case)]
#[derive(serde::Serialize, serde::Deserialize, Debug, Clone)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
pub struct VelopackLocator {
    /// The root directory of the current app.
    pub RootAppDir: PathBuf,
    /// The path to the Update.exe binary.
    pub UpdateExePath: PathBuf,
    /// The path to the packages directory.
    pub PackagesDir: PathBuf,
    /// The current app manifest.
    pub ManifestPath: PathBuf,
    /// The temporary directory for the current app.
    pub TempDir: PathBuf,
}

impl VelopackLocator {
    /// Load and parse the current app manifest from the manifest_path field. This will return an error if the manifest is missing.
    pub fn load_manifest(&self) -> Result<Manifest, Error> {
        read_current_manifest(&self.ManifestPath)
    }
}

#[cfg(target_os = "windows")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate<P: AsRef<Path>>(exe_path: P) -> Result<VelopackLocator, Error> {
    // check if Update.exe exists in parent dir, if it does, that's the root dir.
    let mut path = exe_path.as_ref().to_path_buf();
    path.pop(); // current dir
    path.pop(); // root dir
    if path.join("Update.exe").exists() {
        info!("Found Update.exe in parent directory: {}", path.to_string_lossy());
        return Ok(VelopackLocator {
            RootAppDir: path.clone(),
            UpdateExePath: path.join("Update.exe"),
            PackagesDir: path.join("packages"),
            ManifestPath: path.join("current").join("sq.version"),
            TempDir: path.join("packages").join("VelopackTemp"),
        });
    }

    // see if we can find the current dir in the path, maybe we're more nested than that.
    path = exe_path.as_ref().to_path_buf();
    let path = path.to_string_lossy();
    let idx = path.rfind("\\current\\");
    if let Some(i) = idx {
        let maybe_root = &path[..i];
        let maybe_root = PathBuf::from(maybe_root);
        if (maybe_root.join("Update.exe")).exists() {
            info!("Found Update.exe in parent directory: {}", maybe_root.to_string_lossy());
            return Ok(VelopackLocator {
                RootAppDir: maybe_root.clone(),
                UpdateExePath: maybe_root.join("Update.exe"),
                PackagesDir: maybe_root.join("packages"),
                ManifestPath: maybe_root.join("current").join("sq.version"),
                TempDir: maybe_root.join("packages").join("VelopackTemp"),
            });
        }
    }

    Err(Error::MissingUpdateExe)
}

#[cfg(target_os = "linux")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate<P: AsRef<Path>>(exe_path: P) -> Result<VelopackLocator, Error> {
    let path = exe_path.as_ref().to_path_buf();
    let path = path.to_string_lossy();
    let idx = path.rfind("/usr/bin/");
    if idx.is_none() {
        return Err(Error::NotInstalled(format!("Could not locate '/usr/bin/' in executable path {}", path)));
    }
    let idx = idx.unwrap();
    let root_app_dir = PathBuf::from(path[..idx].to_string());
    let contents_dir = root_app_dir.join("usr").join("bin");
    let update_exe_path = contents_dir.join("UpdateNix");
    let metadata_path = contents_dir.join("sq.version");

    if !update_exe_path.exists() {
        return Err(Error::MissingUpdateExe);
    }

    let app = read_current_manifest(&metadata_path)?;
    Ok(VelopackLocator {
        RootAppDir: root_app_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: PathBuf::from("/var/tmp/velopack").join(&app.id).join("packages"),
        ManifestPath: metadata_path,
        TempDir: PathBuf::from("/tmp/velopack").join(&app.id),
    })
}

#[cfg(target_os = "macos")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate<P: AsRef<Path>>(exe_path: P) -> Result<VelopackLocator, Error> {
    let path = exe_path.as_ref().to_path_buf();
    let path = path.to_string_lossy();
    let idx = path.rfind(".app/");
    if idx.is_none() {
        return Err(Error::NotInstalled(format!("Could not locate '.app' in executable path {}", path)));
    }
    let idx = idx.unwrap();
    let path = path[..(idx + 4)].to_string();

    let root_app_dir = PathBuf::from(&path);
    let contents_dir = root_app_dir.join("Contents").join("MacOS");
    let update_exe_path = contents_dir.join("UpdateMac");
    let metadata_path = contents_dir.join("sq.version");

    if !update_exe_path.exists() {
        return Err(Error::MissingUpdateExe);
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
        RootAppDir: root_app_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: metadata_path,
        TempDir: PathBuf::from("/tmp/velopack").join(&app.id),
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

