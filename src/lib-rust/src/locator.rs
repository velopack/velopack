use std::path::PathBuf;
use semver::Version;
use crate::{
    bundle::{self, Manifest},
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
#[allow(unused_variables)]
pub fn default_log_location(context: LocationContext) -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Ok(locator) = auto_locate_app_manifest(context) {
            return locator.get_root_dir().join("Velopack.log");
        }
        
        warn!("Could not auto-locate app manifest, writing log to current directory.");

        // If we can't locate the current app, we write to the current directory.
        let mut my_exe = std::env::current_exe().expect("Could not locate current executable");
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

bitflags::bitflags! {
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
    /// ShortcutLocationFlags is a bitflags enumeration of system shortcut locations.
    pub struct ShortcutLocationFlags: u32 {
        /// No shortcut.
        const NONE = 0;
        /// Start Menu shortcut inside a PackAuthor folder.
        const START_MENU = 1 << 0;
        /// Desktop shortcut.
        const DESKTOP = 1 << 1;
        /// Startup shortcut.
        const STARTUP = 1 << 2;
        //const APP_ROOT = 1 << 3,
        /// Start Menu shortcut at the root level (not inside an author/publisher folder).
        const START_MENU_ROOT = 1 << 4;
        /// User pinned to taskbar shortcut.
        const USER_PINNED = 1 << 5;
    }
}

impl ShortcutLocationFlags {
    /// Parses a string containing comma or semicolon delimited shortcut flags.
    pub fn from_string(input: &str) -> ShortcutLocationFlags {
        let mut flags = ShortcutLocationFlags::NONE;
        for part in input.split(|c| c == ',' || c == ';') {
            match part.trim().to_lowercase().as_str() {
                "none" => flags |= ShortcutLocationFlags::NONE,
                "startmenu" => flags |= ShortcutLocationFlags::START_MENU,
                "desktop" => flags |= ShortcutLocationFlags::DESKTOP,
                "startup" => flags |= ShortcutLocationFlags::STARTUP,
                "startmenuroot" => flags |= ShortcutLocationFlags::START_MENU_ROOT,
                _ => warn!("Warning: Unrecognized shortcut flag `{}`", part.trim()),
            }
        }
        flags
    }
}

/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
#[allow(non_snake_case)]
#[derive(serde::Serialize, serde::Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
pub struct VelopackLocatorConfig {
    /// The root directory of the current app.
    pub RootAppDir: PathBuf,
    /// The path to the Update.exe binary.
    pub UpdateExePath: PathBuf,
    /// The path to the packages' directory.
    pub PackagesDir: PathBuf,
    /// The current app manifest.
    pub ManifestPath: PathBuf,
    /// The directory containing the application's user binaries.
    pub CurrentBinaryDir: PathBuf,
    /// Whether the current application is portable or installed.
    pub IsPortable: bool,
}

impl VelopackLocatorConfig {
    /// Load and parse the current app manifest from the manifest_path field. This will return an error if the manifest is missing.
    pub fn load_manifest(&self) -> Result<Manifest, Error> {
        read_current_manifest(&self.ManifestPath)
    }
}

/// VelopackLocator provides some utility functions for locating the current app important paths
#[derive(Clone)]
pub struct VelopackLocator {
    paths: VelopackLocatorConfig,
    manifest: Manifest,
}

impl VelopackLocator {
    /// Creates a new VelopackLocator from the given paths and manifest.
    pub fn new(paths: VelopackLocatorConfig, manifest: Manifest) -> Self {
        Self { paths, manifest }
    }

    /// Returns the path to the current app's packages directory.
    pub fn get_packages_dir(&self) -> PathBuf {
        self.paths.PackagesDir.clone()
    }

    /// Returns the path to the current app's packages directory as a string.
    pub fn get_packages_dir_as_string(&self) -> String {
        Self::path_as_string(&self.paths.PackagesDir)
    }

    /// Returns the path to the ideal local nupkg path.
    pub fn get_ideal_local_nupkg_path(&self, id: Option<&str>, version: Option<Version>) -> PathBuf {
        let id = id.unwrap_or(&self.manifest.id);
        let version = version.unwrap_or(self.manifest.version.clone());
        self.paths.RootAppDir.join("packages").join(format!("{}-{}-full.nupkg", id, version))
    }

    /// Returns the path to the ideal local nupkg path as a string.
    pub fn get_ideal_local_nupkg_path_as_string(&self, id: Option<&str>, version: Option<Version>) -> String {
        Self::path_as_string(&self.get_ideal_local_nupkg_path(id, version))
    }

    /// Returns the path to the current app temporary directory.
    pub fn get_temp_dir_root(&self) -> PathBuf {
        self.paths.PackagesDir.join("VelopackTemp")
    }
    
    /// Get the name of a new temporary directory inside get_temp_dir_root() with a random 16-character suffix.
    pub fn get_temp_dir_rand16(&self) -> PathBuf {
        self.get_temp_dir_root().join("tmp_".to_string() + &util::random_string(16))
    }

    /// Returns the path to the current app temporary directory as a string.
    pub fn get_temp_dir_as_string(&self) -> String {
        Self::path_as_string(&self.get_temp_dir_root())
    }

    /// Returns the root directory of the current app.
    pub fn get_root_dir(&self) -> PathBuf {
        self.paths.RootAppDir.clone()
    }

    /// Returns the root directory of the current app as a string.
    pub fn get_root_dir_as_string(&self) -> String {
        Self::path_as_string(&self.paths.RootAppDir)
    }

    /// Returns the path to the current app's Update.exe binary.
    pub fn get_update_path(&self) -> PathBuf {
        self.paths.UpdateExePath.clone()
    }

    /// Returns the path to the current app's Update.exe binary as a string.
    pub fn get_update_path_as_string(&self) -> String {
        Self::path_as_string(&self.paths.UpdateExePath)
    }

    /// Returns the path to the current app's main executable.
    pub fn get_main_exe_path(&self) -> PathBuf {
        self.paths.CurrentBinaryDir.join(&self.manifest.main_exe)
    }

    /// Returns the path to the current app's main executable as a string.
    pub fn get_main_exe_path_as_string(&self) -> String {
        Self::path_as_string(&self.get_main_exe_path())
    }

    /// Returns the path to the current app's user binary directory.
    pub fn get_current_bin_dir(&self) -> PathBuf {
        self.paths.CurrentBinaryDir.clone()
    }

    /// Returns the path to the current app's user binary directory as a string.
    pub fn get_current_bin_dir_as_string(&self) -> String {
        Self::path_as_string(&self.paths.CurrentBinaryDir)
    }
    
    /// Returns a clone of the current app's manifest.
    pub fn get_manifest(&self) -> Manifest {
        self.manifest.clone()
    }

    /// Returns the current app's version.
    pub fn get_manifest_version(&self) -> Version {
        self.manifest.version.clone()
    }

    /// Returns the current app's version as a string containing all parts.
    pub fn get_manifest_version_full_string(&self) -> String {
        self.manifest.version.to_string()
    }

    /// Returns the current app's version as a string in short format (eg. '1.2.3'),
    /// not including any semver release groups etc.
    pub fn get_manifest_version_short_string(&self) -> String {
        let ver = &self.manifest.version;
        format!("{}.{}.{}", ver.major, ver.minor, ver.patch)
    }
    
    /// Returns the current app package channel.
    pub fn get_manifest_channel(&self) -> String {
        self.manifest.channel.clone()
    }

    /// Returns the current app's Id.
    pub fn get_manifest_id(&self) -> String {
        self.manifest.id.clone()
    }

    /// Returns the current app's friendly / display name.
    pub fn get_manifest_title(&self) -> String {
        self.manifest.title.clone()
    }

    /// Returns the current app authors / publishers string.
    pub fn get_manifest_authors(&self) -> String {
        self.manifest.authors.clone()
    }

    /// Returns a flags enumeration of desired shortcut locations, or NONE if no shortcuts are desired.
    pub fn get_manifest_shortcut_locations(&self) -> ShortcutLocationFlags {
        if self.manifest.shortcut_locations.is_empty() {
            return ShortcutLocationFlags::NONE;
        }
        if self.manifest.shortcut_locations.to_ascii_lowercase() == "none" {
            return ShortcutLocationFlags::NONE;
        }
        ShortcutLocationFlags::from_string(&self.manifest.shortcut_locations)
    }

    /// Returns the desired shortcut AMUID, or None if no AMUID has been provided.
    pub fn get_manifest_shortcut_amuid(&self) -> Option<String> {
        if self.manifest.shortcut_amuid.is_empty() {
            return None;
        }
        Some(self.manifest.shortcut_amuid.clone())
    }

    /// Returns a copy of the current VelopackLocator with the manifest field set to the given manifest.
    pub fn clone_self_with_new_manifest(&self, manifest: &Manifest) -> VelopackLocator
    {
        VelopackLocator {
            paths: self.paths.clone(),
            manifest: manifest.clone(),
        }
    }

    /// Returns whether the app is portable or installed.
    pub fn get_is_portable(&self) -> bool {
        self.paths.IsPortable
    }

    fn path_as_string(path: &PathBuf) -> String {
        path.to_string_lossy().to_string()
    }
}

/// Create a paths object containing default / ideal paths for a given root directory
/// Generally, this should not be used except for installing the app for the first time.
#[cfg(target_os = "windows")]
pub fn create_config_from_root_dir<P: AsRef<std::path::Path>>(root_dir: P) -> VelopackLocatorConfig
{
    let root_dir = root_dir.as_ref();
    VelopackLocatorConfig {
        RootAppDir: root_dir.to_path_buf(),
        UpdateExePath: root_dir.join("Update.exe"),
        PackagesDir: root_dir.join("packages"),
        ManifestPath: root_dir.join("current").join("sq.version"),
        CurrentBinaryDir: root_dir.join("current"),
        IsPortable: root_dir.join(".portable").exists(),
    }
}

fn config_to_locator(config: &VelopackLocatorConfig) -> Result<VelopackLocator, Error>
{
    if !config.UpdateExePath.exists() {
        return Err(Error::MissingUpdateExe);
    }
    if !config.ManifestPath.exists() {
        return Err(Error::MissingNuspec);
    }

    let manifest = read_current_manifest(&config.ManifestPath)?;
    Ok(VelopackLocator::new(config.clone(), manifest))
}

/// LocationContext is an enumeration of possible contexts for locating the current app manifest.
pub enum LocationContext
{
    /// Should not really be used, will try a few other enumerations to locate the app manifest.
    Unknown,
    /// Locates the app manifest by assuming the current process is Update.exe.
    IAmUpdateExe,
    /// Locates the app manifest by assuming the current process is inside the application current/binary directory.
    FromCurrentExe,
    /// Locates the app manifest by assuming the app is installed in the specified root directory.
    FromSpecifiedRootDir(PathBuf),
    /// Locates the app manifest by assuming the specified path is inside the application current/binary directory.
    FromSpecifiedAppExecutable(PathBuf),
}

#[cfg(target_os = "windows")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate_app_manifest(context: LocationContext) -> Result<VelopackLocator, Error> {
    match context {
        LocationContext::Unknown => {
            warn!("Unknown location context, trying to auto-locate from current exe location...");
            if let Ok(locator) = auto_locate_app_manifest(LocationContext::FromCurrentExe) {
                return Ok(locator);
            }
            if let Ok(locator) = auto_locate_app_manifest(LocationContext::IAmUpdateExe) {
                return Ok(locator);
            }
        }
        LocationContext::FromCurrentExe => {
            let current_exe = std::env::current_exe()?;
            return auto_locate_app_manifest(LocationContext::FromSpecifiedAppExecutable(current_exe));
        }
        LocationContext::FromSpecifiedRootDir(root_dir) => {
            let config = create_config_from_root_dir(&root_dir);
            let locator = config_to_locator(&config)?;
            return Ok(locator);
        }
        LocationContext::FromSpecifiedAppExecutable(exe_path) => {
            // check if Update.exe exists in parent dir, if it does, that's the root dir.
            if let Some(parent_dir) = exe_path.parent() {
                if parent_dir.join("Update.exe").exists() {
                    info!("Found Update.exe in parent directory: {}", parent_dir.to_string_lossy());
                    let config = create_config_from_root_dir(&parent_dir);
                    let locator = config_to_locator(&config)?;
                    return Ok(locator);
                }
            }

            // see if we can find the current dir in the current path, if we're more nested than that.
            let path = exe_path.to_string_lossy();
            let idx = path.rfind("\\current\\");
            if let Some(i) = idx {
                let maybe_root = &path[..i];
                let maybe_root = PathBuf::from(maybe_root);
                if maybe_root.join("Update.exe").exists() {
                    info!("Found Update.exe by current path pattern search in directory: {}", maybe_root.to_string_lossy());
                    let config = create_config_from_root_dir(&maybe_root);
                    let locator = config_to_locator(&config)?;
                    return Ok(locator);
                }
            }
        }
        LocationContext::IAmUpdateExe => {
            let exe_path = std::env::current_exe()?;
            if let Some(parent_dir) = exe_path.parent() {
                let config = create_config_from_root_dir(&parent_dir);
                let locator = config_to_locator(&config)?;
                return Ok(locator);
            }
        }
    };

    Err(Error::NotInstalled("Could not auto-locate app manifest".to_owned()))
}

#[cfg(target_os = "linux")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate_app_manifest(context: LocationContext) -> Result<VelopackLocator, Error> {
    let mut search_path = std::env::current_exe()?;
    match context {
        LocationContext::FromSpecifiedRootDir(dir) => search_path = dir.join("dummy"),
        LocationContext::FromSpecifiedAppExecutable(exe) => search_path = exe,
        _ => {},
    }

    let search_string = search_path.to_string_lossy();
    let idx = search_string.rfind("/usr/bin/");
    if idx.is_none() {
        return Err(Error::NotInstalled(format!("Could not locate '/usr/bin/' in executable path {}", search_string)));
    }
    let idx = idx.unwrap();
    let root_app_dir = PathBuf::from(search_string[..idx].to_string());
    let contents_dir = root_app_dir.join("usr").join("bin");
    let update_exe_path = contents_dir.join("UpdateNix");
    let metadata_path = contents_dir.join("sq.version");

    if !update_exe_path.exists() {
        return Err(Error::MissingUpdateExe);
    }

    let app = read_current_manifest(&metadata_path)?;
    let packages_dir = PathBuf::from("/var/tmp/velopack").join(&app.id).join("packages");

    let config = VelopackLocatorConfig {
        RootAppDir: root_app_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: metadata_path,
        CurrentBinaryDir: contents_dir,
        IsPortable: true,
    };

    config_to_locator(&config)
}

#[cfg(target_os = "macos")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate_app_manifest(context: LocationContext) -> Result<VelopackLocator, Error> {
    let mut search_path = std::env::current_exe()?;
    match context {
        LocationContext::FromSpecifiedRootDir(dir) => search_path = dir.join("dummy"),
        LocationContext::FromSpecifiedAppExecutable(exe) => search_path = exe,
        _ => {},
    }
    
    let search_string = search_path.to_string_lossy();
    let idx = search_string.rfind(".app/");
    if idx.is_none() {
        return Err(Error::NotInstalled(format!("Could not locate '.app' in executable path {}", search_string)));
    }
    let idx = idx.unwrap();
    let path = search_string[..(idx + 4)].to_string();

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

    let config = VelopackLocatorConfig {
        RootAppDir: root_app_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: metadata_path,
        CurrentBinaryDir: contents_dir,
        IsPortable: true,
    };
    
    config_to_locator(&config)
}

fn read_current_manifest(nuspec_path: &PathBuf) -> Result<Manifest, Error> {
    if nuspec_path.exists() {
        if let Ok(nuspec) = util::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
            return Ok(bundle::read_manifest_from_string(&nuspec)?);
        }
    }
    Err(Error::MissingNuspec)
}

