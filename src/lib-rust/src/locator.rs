use crate::{
    bundle::{self, Manifest},
    lockfile::LockFile,
    misc, Error,
};
use semver::Version;
use std::path::{Path, PathBuf};
use uuid::Uuid;

#[cfg(windows)]
use crate::known_path::get_local_app_data;

// SHA-256 of "velopack appimage channel override". MUST stay byte-identical to the C#
// implementation (lib-csharp Velopack.Util.AppImageChannelOverride) and the velopack.api
// promotion worker.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
static APPIMAGE_CHANNEL_MAGIC: [u8; 32] = [
    0xde, 0xed, 0x1b, 0xad, 0x30, 0x15, 0xb1, 0x96, 0x9e, 0x6e, 0xbf, 0x7d, 0x09, 0x3f, 0x5d, 0xca, 0x6c, 0x6c, 0x52, 0xa1, 0xa0, 0xa2, 0x57, 0x57,
    0x19, 0x91, 0x62, 0x83, 0x11, 0xd8, 0x03, 0x51,
];

#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
const APPIMAGE_TRAILER_SCAN_WINDOW: u64 = 1024; // trailer is at most 34 + 255 = 289 bytes; window gives slack
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
const APPIMAGE_TRAILER_HEADER: usize = 34; // 32 magic + 2 u16-le length
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
const APPIMAGE_TRAILER_MAX_CHANNEL: usize = 255;

/// Parses the tail window of an AppImage for a channel-override trailer:
/// `[MAGIC(32)][LENGTH u16-le(2)][CHANNEL utf8(1..=255)]` appended after the squashfs.
/// Backward scan: the last VALID trailer wins; malformed occurrences are skipped.
/// Channel bytes must all be printable ASCII (0x21..=0x7E); the value is used verbatim
/// (no case folding, no trimming).
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn parse_appimage_channel_override(window: &[u8]) -> Option<String> {
    if window.len() < APPIMAGE_TRAILER_HEADER + 1 {
        return None;
    }
    for pos in (0..=window.len() - APPIMAGE_TRAILER_HEADER).rev() {
        if window[pos..pos + 32] != APPIMAGE_CHANNEL_MAGIC {
            continue;
        }
        let len = u16::from_le_bytes([window[pos + 32], window[pos + 33]]) as usize;
        if !(1..=APPIMAGE_TRAILER_MAX_CHANNEL).contains(&len) {
            continue;
        }
        let start = pos + APPIMAGE_TRAILER_HEADER;
        if start + len > window.len() {
            continue; // truncated channel
        }
        let channel = &window[start..start + len];
        if channel.iter().any(|&b| !(0x21..=0x7E).contains(&b)) {
            continue;
        }
        return Some(String::from_utf8_lossy(channel).into_owned()); // pure ASCII per the charset check above
    }
    None
}

/// Reads the last `APPIMAGE_TRAILER_SCAN_WINDOW` bytes of the file at `path` and parses a
/// channel-override trailer (written server-side during channel promotion). Never errors:
/// any IO or parse failure logs a warning and returns None so locator initialization can
/// fall back to the manifest channel. Only the auto-locate path applies this override —
/// `VelopackLocator::new` does not (matches C#, where only `LinuxVelopackLocator` reads the
/// trailer). Version floor: apps built by SDK versions predating this reader ignore the
/// override and keep using the manifest channel.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn try_read_appimage_channel_override(path: &Path) -> Option<String> {
    use std::io::{Read, Seek, SeekFrom};
    // Mirrors the C# FileInfo.Exists short-circuit (AppImageChannelOverride.TryReadFromFile):
    // a missing/unopenable AppImage is simply "no trailer" — don't burn retry_io's ~2s of
    // retries and warn logs on it during locator initialization.
    if !path.is_file() {
        return None;
    }
    let result: std::io::Result<Option<String>> = (|| {
        let mut file = misc::retry_io(|| std::fs::File::open(path))?;
        let size = file.metadata()?.len();
        if size < (APPIMAGE_TRAILER_HEADER as u64) + 1 {
            return Ok(None);
        }
        let win = std::cmp::min(APPIMAGE_TRAILER_SCAN_WINDOW, size);
        file.seek(SeekFrom::End(-(win as i64)))?;
        let mut buf = vec![0u8; win as usize];
        file.read_exact(&mut buf)?;
        Ok(parse_appimage_channel_override(&buf))
    })();
    match result {
        Ok(v) => v,
        Err(e) => {
            warn!("Failed reading AppImage channel-override trailer from {:?}: {}", path, e);
            None
        }
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
        for part in input.split([',', ';']) {
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
pub struct VelopackLocatorConfig {
    /// The root directory of the current app, or the path to the AppImage file on Linux.
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

impl TryFrom<VelopackLocatorConfig> for VelopackLocator {
    type Error = Error;
    fn try_from(config: VelopackLocatorConfig) -> Result<Self, Self::Error> {
        VelopackLocator::new(&config)
    }
}

impl TryFrom<&VelopackLocatorConfig> for VelopackLocator {
    type Error = Error;
    fn try_from(config: &VelopackLocatorConfig) -> Result<Self, Self::Error> {
        VelopackLocator::new(config)
    }
}

impl TryFrom<LocationContext> for VelopackLocator {
    type Error = Error;
    fn try_from(context: LocationContext) -> Result<Self, Self::Error> {
        auto_locate_app_manifest(context)
    }
}

impl TryFrom<&LocationContext> for VelopackLocator {
    type Error = Error;
    fn try_from(context: &LocationContext) -> Result<Self, Self::Error> {
        auto_locate_app_manifest(context.clone())
    }
}

impl VelopackLocator {
    /// Creates a new VelopackLocator from the given paths, trying to auto-detect the manifest.
    pub fn new(config: &VelopackLocatorConfig) -> Result<VelopackLocator, Error> {
        if !config.UpdateExePath.exists() {
            return Err(Error::NotInstalled(format!(
                "Update.exe does not exist in the expected path ({})",
                config.UpdateExePath.display()
            )));
        }
        if !config.ManifestPath.exists() {
            return Err(Error::NotInstalled(format!(
                "Manifest file does not exist in the expected path ({})",
                config.ManifestPath.display()
            )));
        }

        let manifest = read_current_manifest(&config.ManifestPath)?;
        Ok(Self::new_with_manifest(config.clone(), manifest))
    }

    /// Creates a new VelopackLocator from the given paths and manifest.
    #[cfg(windows)]
    pub fn new_with_manifest(mut paths: VelopackLocatorConfig, manifest: Manifest) -> Self {
        let root = paths.RootAppDir.clone();
        let default_packages_dir = root.join("packages");
        let has_custom_packages_dir = paths.PackagesDir != default_packages_dir;

        if has_custom_packages_dir {
            // A custom PackagesDir was provided (e.g. via --packageDir)
            if misc::is_directory_writable(&paths.PackagesDir) {
                info!("Using custom packages directory (writable): {}", paths.PackagesDir.display());
            } else {
                warn!(
                    "Custom packages directory is not writable, falling through to standard logic: {}",
                    paths.PackagesDir.display()
                );
                // Fall through to standard logic below
                paths.PackagesDir = default_packages_dir.clone();
            }
        }

        // Standard logic: only run if we don't have a valid custom dir
        if paths.PackagesDir == default_packages_dir {
            let is_writable = misc::is_directory_writable(&root);
            info!("Root directory '{}' writable: {}", root.display(), is_writable);

            if is_writable {
                paths.PackagesDir = root.join("packages");
                info!("Using root packages directory: {}", paths.PackagesDir.display());
            } else if let Ok(app_data) = get_local_app_data() {
                let fallback_base = app_data.join(&manifest.id);
                paths.PackagesDir = fallback_base.join("packages");
                paths.UpdateExePath = fallback_base.join("Update.exe");
                info!("Using fallback directory: {}", fallback_base.display());

                if let Err(e) = std::fs::create_dir_all(&paths.PackagesDir) {
                    error!("Unable to create fallback packages directory: {}", e);
                }

                // If the fallback Update.exe doesn't exist yet (e.g. first launch after MSI install),
                // copy it from the root directory so UpdateExePath always points to an existing file.
                let root_update_exe = root.join("Update.exe");
                if !paths.UpdateExePath.exists() && root_update_exe.exists() {
                    match std::fs::copy(&root_update_exe, &paths.UpdateExePath) {
                        Ok(_) => info!("Copied Update.exe from root to fallback: {}", paths.UpdateExePath.display()),
                        Err(e) => error!("Failed to copy Update.exe to fallback path: {}", e),
                    }
                }
            } else {
                error!("Root directory is not writable and LocalAppData is unavailable. Updates may not work correctly.");
            }
        }

        Self { paths, manifest }
    }

    /// Creates a new VelopackLocator from the given paths and manifest.
    #[cfg(not(windows))]
    pub fn new_with_manifest(paths: VelopackLocatorConfig, manifest: Manifest) -> Self {
        Self { paths, manifest }
    }

    /// Returns the path to the current app's packages directory.
    pub fn get_packages_dir(&self) -> PathBuf {
        self.paths.PackagesDir.clone()
    }

    /// Returns the path to the ideal local nupkg path.
    pub fn get_ideal_local_nupkg_path(&self, id: Option<&str>, version: Option<Version>) -> PathBuf {
        let id = id.unwrap_or(&self.manifest.id);
        let version = version.unwrap_or(self.manifest.version.clone());
        self.paths.PackagesDir.join(format!("{}-{}-full.nupkg", id, version))
    }

    /// Returns the path to the current app temporary directory.
    pub fn get_temp_dir_root(&self) -> PathBuf {
        self.paths.PackagesDir.join("VelopackTemp")
    }

    /// Get the name of a new temporary directory inside get_temp_dir_root() with a random 16-character suffix.
    pub fn get_temp_dir_rand16(&self) -> PathBuf {
        self.get_temp_dir_root().join("tmp_".to_string() + &misc::random_string(16))
    }

    /// Returns the root directory of the current app.
    #[cfg(not(target_os = "linux"))]
    pub fn get_root_dir(&self) -> PathBuf {
        self.paths.RootAppDir.clone()
    }

    /// Returns the path to the AppImage file on Linux.
    #[cfg(target_os = "linux")]
    pub fn get_appimage_path(&self) -> PathBuf {
        self.paths.RootAppDir.clone()
    }

    /// Returns the path to the current app's Update.exe binary.
    pub fn get_update_path(&self) -> PathBuf {
        self.paths.UpdateExePath.clone()
    }

    /// Returns the path to the current app's main executable.
    pub fn get_main_exe_path(&self) -> PathBuf {
        self.paths.CurrentBinaryDir.join(&self.manifest.main_exe)
    }

    /// Returns the path to the current app's user binary directory.
    pub fn get_current_bin_dir(&self) -> PathBuf {
        self.paths.CurrentBinaryDir.clone()
    }

    /// Returns a clone of the current app's manifest.
    pub fn get_manifest(&self) -> Manifest {
        self.manifest.clone()
    }

    /// Returns the current app's version.
    pub fn get_manifest_version(&self) -> Version {
        self.manifest.version.clone()
    }

    /// Returns unique identifier for this user which is used to calculate whether this user is eligible for staged roll outs.
    pub fn get_staged_user_id(&self) -> String {
        self.get_or_create_staged_user_id().clone()
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
        if self.manifest.shortcut_locations.eq_ignore_ascii_case("none") {
            return ShortcutLocationFlags::NONE;
        }
        ShortcutLocationFlags::from_string(&self.manifest.shortcut_locations)
    }

    /// Returns the desired shortcut AUMID, or None if no AUMID has been provided.
    pub fn get_manifest_shortcut_aumid(&self) -> Option<String> {
        if self.manifest.shortcut_aumid.is_empty() {
            return None;
        }
        Some(self.manifest.shortcut_aumid.clone())
    }

    /// Returns the Application User Model ID for this app.
    /// If a custom AUMID was specified during packaging, it will be returned.
    /// Otherwise, falls back to "velopack.{AppId}".
    pub fn get_app_user_model_id(&self) -> String {
        self.get_manifest_shortcut_aumid()
            .unwrap_or_else(|| format!("velopack.{}", self.manifest.id))
    }

    /// Returns a copy of the current VelopackLocator with the manifest field set to the given manifest.
    pub fn clone_self_with_new_manifest(&self, manifest: &Manifest) -> VelopackLocator {
        VelopackLocator {
            paths: self.paths.clone(),
            manifest: manifest.clone(),
        }
    }

    /// Returns whether the app is portable or installed.
    pub fn get_is_portable(&self) -> bool {
        self.paths.IsPortable
    }

    /// Returns whether the app was installed via MSI (indicated by a `.msi-installed` marker file).
    #[cfg(windows)]
    pub fn get_is_msi_install(&self) -> bool {
        self.paths.RootAppDir.join(".msi-installed").exists()
    }

    /// Attemps to open / lock a file in the app's package directory for exclusive write access.
    /// Fails immediately if the lock cannot be acquired.
    pub fn try_get_exclusive_lock(&self) -> Result<LockFile, Error> {
        info!("Attempting to acquire exclusive lock on packages directory (non-blocking)...");
        let packages_dir = self.get_packages_dir();
        std::fs::create_dir_all(&packages_dir)?;
        let lock_file_path = packages_dir.join(".velopack_lock");
        let lock_file = LockFile::try_acquire_lock(&lock_file_path)?;
        Ok(lock_file)
    }

    fn get_or_create_staged_user_id(&self) -> String {
        let packages_dir = self.get_packages_dir();
        let beta_id_path = packages_dir.join(".betaId");
        if beta_id_path.exists() {
            info!("Found existing staged user id...");
            if let Ok(beta_id) = std::fs::read_to_string(&beta_id_path) {
                return beta_id;
            }
        }
        let new_id = Uuid::new_v4();
        if let Err(_e) = std::fs::write(&beta_id_path, new_id.to_string()) {
            warn!("Couldn't write out staging userId.");
        } else {
            info!("Generated new staging userId: {}", new_id);
        }
        new_id.to_string()
    }
}

/// Create a paths object containing default / ideal paths for a given root directory
/// Generally, this should not be used except for installing the app for the first time.
#[cfg(target_os = "windows")]
pub fn create_config_from_root_dir<P: AsRef<std::path::Path>>(root_dir: P) -> VelopackLocatorConfig {
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

/// LocationContext is an enumeration of possible contexts for locating the current app manifest.
#[derive(Debug, Clone)]
pub enum LocationContext {
    /// Should not really be used, will try a few other enumerations to locate the app manifest.
    Unknown,
    /// Locates the app manifest by assuming the current process is Update.exe.
    IAmUpdateExe,
    /// Locates the app manifest by assuming the current process is inside the application current/binary directory.
    FromCurrentExe,
    /// Locates the app manifest by assuming the app is installed in the specified root directory,
    /// with an optional packages directory override.
    FromSpecifiedRootDir(PathBuf, Option<PathBuf>),
    /// Locates the app manifest by assuming the specified path is inside the application current/binary directory.
    FromSpecifiedAppExecutable(PathBuf),
}

/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
#[cfg(target_os = "windows")]
pub fn auto_locate_app_manifest(context: LocationContext) -> Result<VelopackLocator, Error> {
    info!("Auto-locating app manifest...");
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
        LocationContext::FromSpecifiedRootDir(root_dir, package_dir) => {
            let mut config = create_config_from_root_dir(&root_dir);
            if let Some(pkg_dir) = package_dir {
                config.PackagesDir = pkg_dir;
            }
            let locator = VelopackLocator::new(&config)?;
            return Ok(locator);
        }
        LocationContext::FromSpecifiedAppExecutable(exe_path) => {
            // check if Update.exe exists in parent dir, if it does, that's the root dir.
            if let Some(parent_dir) = exe_path.parent() {
                if parent_dir.join("Update.exe").exists() {
                    info!("Found Update.exe in parent directory: {}", parent_dir.to_string_lossy());
                    let config = create_config_from_root_dir(parent_dir);
                    let locator = VelopackLocator::new(&config)?;
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
                    info!(
                        "Found Update.exe by current path pattern search in directory: {}",
                        maybe_root.to_string_lossy()
                    );
                    let config = create_config_from_root_dir(&maybe_root);
                    let locator = VelopackLocator::new(&config)?;
                    return Ok(locator);
                }
            }
        }
        LocationContext::IAmUpdateExe => {
            let exe_path = std::env::current_exe()?;
            if let Some(parent_dir) = exe_path.parent() {
                let config = create_config_from_root_dir(parent_dir);
                let locator = VelopackLocator::new(&config)?;
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
    let mut package_dir_override: Option<PathBuf> = None;
    let mut appimage_path_override: Option<PathBuf> = None;
    match context {
        LocationContext::FromSpecifiedRootDir(dir, pkg_dir) => {
            if dir.is_file() {
                // Newer libraries pass the AppImage file path directly.
                appimage_path_override = Some(dir);
            }
            // If dir is a directory (older libraries pass the mounted root),
            // ignore it — we derive paths from current_exe() and $APPIMAGE instead.
            package_dir_override = pkg_dir;
        }
        LocationContext::FromSpecifiedAppExecutable(exe) => search_path = exe,
        _ => {}
    }

    let search_string = search_path.to_string_lossy();
    let idx = search_string.find("/usr/bin/");
    if idx.is_none() {
        return Err(Error::NotInstalled(format!(
            "Could not locate '/usr/bin/' in executable path {}",
            search_string
        )));
    }
    let idx = idx.unwrap();
    let mount_dir = PathBuf::from(search_string[..idx].to_string());
    let contents_dir = mount_dir.join("usr").join("bin");
    let update_exe_path = contents_dir.join("UpdateNix");
    let metadata_path = contents_dir.join("sq.version");

    if !update_exe_path.exists() {
        return Err(Error::NotInstalled(format!(
            "UpdateNix does not exist at the expected path: {}",
            update_exe_path.to_string_lossy()
        )));
    }

    let appimage_from_env = std::env::var("APPIMAGE")
        .ok()
        .filter(|v| !v.is_empty() && PathBuf::from(v).exists())
        .map(PathBuf::from);

    let appimage_path = if let Some(p) = appimage_path_override {
        if p.exists() {
            p
        } else if let Some(fallback) = appimage_from_env.clone() {
            error!(
                "Specified AppImage path '{}' does not exist, falling back to $APPIMAGE='{}'",
                p.to_string_lossy(),
                fallback.to_string_lossy()
            );
            fallback
        } else {
            return Err(Error::NotInstalled(format!(
                "The specified AppImage path does not exist: {}",
                p.to_string_lossy()
            )));
        }
    } else if let Some(p) = appimage_from_env {
        p
    } else {
        let env_val = std::env::var("APPIMAGE").unwrap_or_default();
        return Err(Error::NotInstalled(if env_val.is_empty() {
            "The $APPIMAGE environment variable is not set. Is this app running as an AppImage?".to_string()
        } else {
            format!("The $APPIMAGE environment variable points to a path that does not exist: {}", env_val)
        }));
    };
    info!("Resolved AppImage path: {}", appimage_path.to_string_lossy());

    let mut app = read_current_manifest(&metadata_path)?;
    if let Some(channel_override) = try_read_appimage_channel_override(&appimage_path) {
        info!(
            "AppImage channel override trailer found: '{}' (manifest channel was '{}')",
            channel_override, app.channel
        );
        app.channel = channel_override;
    }

    let packages_dir = if let Some(pkg_dir) = package_dir_override {
        pkg_dir
    } else {
        PathBuf::from("/var/tmp/velopack").join(&app.id).join("packages")
    };

    let config = VelopackLocatorConfig {
        RootAppDir: appimage_path,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: metadata_path,
        CurrentBinaryDir: contents_dir,
        IsPortable: true,
    };

    Ok(VelopackLocator::new_with_manifest(config, app))
}

#[cfg(target_os = "macos")]
/// Automatically locates the current app's important paths. If the app is not installed, it will return an error.
pub fn auto_locate_app_manifest(context: LocationContext) -> Result<VelopackLocator, Error> {
    let mut search_path = std::env::current_exe()?;
    let mut package_dir_override: Option<PathBuf> = None;
    match context {
        LocationContext::FromSpecifiedRootDir(dir, pkg_dir) => {
            search_path = dir.join("dummy");
            package_dir_override = pkg_dir;
        }
        LocationContext::FromSpecifiedAppExecutable(exe) => search_path = exe,
        _ => {}
    }

    let search_string = search_path.to_string_lossy();
    let idx = search_string.find(".app/");
    if idx.is_none() {
        return Err(Error::NotInstalled(format!(
            "Could not locate '.app' in executable path {}",
            search_string
        )));
    }
    let idx = idx.unwrap();
    let path = search_string[..(idx + 4)].to_string();

    let root_app_dir = PathBuf::from(&path);
    let contents_dir = root_app_dir.join("Contents").join("MacOS");
    let update_exe_path = contents_dir.join("UpdateMac");
    let metadata_path = contents_dir.join("sq.version");
    let resources_metadata_path = root_app_dir.join("Contents").join("Resources").join("sq.version");

    if !update_exe_path.exists() {
        return Err(Error::NotInstalled("UpdateMac does not exist in the expected path".to_owned()));
    }

    let (app, resolved_metadata_path) = read_current_manifest(&metadata_path)
        .map(|m| (m, metadata_path))
        .or_else(|_| read_current_manifest(&resources_metadata_path).map(|m| (m, resources_metadata_path)))?;

    let packages_dir = if let Some(pkg_dir) = package_dir_override {
        pkg_dir
    } else {
        #[allow(deprecated)]
        let mut dir = std::env::home_dir().expect("Could not locate user home directory via $HOME or /etc/passwd");
        dir.push("Library");
        dir.push("Caches");
        dir.push("velopack");
        dir.push(&app.id);
        dir.push("packages");
        dir
    };

    let config = VelopackLocatorConfig {
        RootAppDir: root_app_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: resolved_metadata_path,
        CurrentBinaryDir: contents_dir,
        IsPortable: true,
    };

    Ok(VelopackLocator::new_with_manifest(config, app))
}

fn read_current_manifest(nuspec_path: &Path) -> Result<Manifest, Error> {
    if nuspec_path.exists() {
        if let Ok(nuspec) = misc::retry_io(|| std::fs::read_to_string(nuspec_path)) {
            return bundle::read_manifest_from_string(&nuspec);
        }
    }
    Err(Error::NotInstalled(format!(
        "Manifest file does not exist or is not readable: {:?}",
        nuspec_path
    )))
}

/// Returns all full packages (path + manifest) found in the given directory.
pub fn find_local_full_packages(packages_dir: &Path) -> Vec<(PathBuf, Manifest)> {
    let packages_dir_str = packages_dir.to_string_lossy();
    info!("Searching for local packages in: {:?}", packages_dir_str);
    let mut results = Vec::new();
    let search_glob = format!("{}/*-full.nupkg", packages_dir_str);
    if let Ok(paths) = glob::glob(search_glob.as_str()) {
        for path in paths.into_iter().flatten() {
            trace!("Checking package: '{:?}'", path);
            if let Ok(mut bun) = bundle::load_bundle_from_file(&path) {
                if let Ok(mani) = bun.read_manifest() {
                    info!("Found {}: '{:?}'", mani.version, path);
                    results.push((path, mani));
                }
            }
        }
    }
    results
}

/// Returns the path and manifest of the latest full package in the given directory.
pub fn find_latest_full_package(packages_dir: &Path) -> Option<(PathBuf, Manifest)> {
    find_local_full_packages(packages_dir)
        .into_iter()
        .max_by(|(_, a), (_, b)| a.version.cmp(&b.version))
}

#[test]
fn test_locator_staged_id_for_new_user() {
    //Create new locator with paths to a test directory
    let tmp_dir = tempfile::TempDir::new().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let test_dir = tmp_buf.join(format!("velopack_{}", misc::random_string(8)));

    let mut paths = VelopackLocatorConfig::default();
    paths.PackagesDir = test_dir;
    //Esure the packages directory exists
    assert!(std::fs::create_dir_all(&paths.PackagesDir).is_ok());

    let locator = VelopackLocator::new_with_manifest(paths, Manifest::default());

    let staged_user_id = locator.get_staged_user_id();

    assert_ne!(staged_user_id, "");
    let packages_dir = locator.get_packages_dir();
    let beta_id_path = packages_dir.join(".betaId");
    assert!(beta_id_path.exists());

    if let Ok(beta_id) = std::fs::read_to_string(&beta_id_path) {
        assert_eq!(staged_user_id, beta_id);
    } else {
        assert!(false, "Couldn't read staging userId.");
    }
}

#[test]
fn test_locator_staged_id_for_existing_user() {
    let tmp_dir = tempfile::TempDir::new().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let test_dir = tmp_buf.join(format!("velopack_{}", misc::random_string(8)));

    let mut paths = VelopackLocatorConfig::default();
    paths.PackagesDir = test_dir;
    //Esure the packages directory exists
    assert!(std::fs::create_dir_all(&paths.PackagesDir).is_ok());

    let locator = VelopackLocator::new_with_manifest(paths, Manifest::default());

    let packages_dir = locator.get_packages_dir();
    let beta_id_path = packages_dir.join(".betaId");

    let expected_user_id = "test user id";
    std::fs::write(&beta_id_path, expected_user_id).unwrap();

    let staged_user_id = locator.get_staged_user_id();

    assert_eq!(expected_user_id, staged_user_id);
}

#[cfg(test)]
fn make_trailer(channel: &[u8], length_override: Option<u16>) -> Vec<u8> {
    let len = length_override.unwrap_or(channel.len() as u16);
    let mut v = Vec::with_capacity(APPIMAGE_TRAILER_HEADER + channel.len());
    v.extend_from_slice(&APPIMAGE_CHANNEL_MAGIC);
    v.extend_from_slice(&len.to_le_bytes());
    v.extend_from_slice(channel);
    v
}

#[cfg(test)]
fn temp_file_with(body: &[u8]) -> (tempfile::TempDir, PathBuf) {
    let dir = tempfile::TempDir::new().unwrap();
    let path = dir.path().join("test.AppImage");
    std::fs::write(&path, body).unwrap();
    (dir, path)
}

#[cfg(test)]
fn pseudo_random_body(size: usize, seed: u8) -> Vec<u8> {
    // Deterministic filler that can never contain the 32-byte magic (consecutive bytes
    // always differ by exactly 31, which the magic sequence does not).
    (0..size).map(|i| (i as u8).wrapping_mul(31).wrapping_add(seed)).collect()
}

#[test]
fn test_appimage_trailer_absent() {
    let body = pseudo_random_body(4096, 7);
    assert_eq!(parse_appimage_channel_override(&body), None);
    let (_dir, path) = temp_file_with(&body);
    assert_eq!(try_read_appimage_channel_override(&path), None);
}

#[test]
fn test_appimage_trailer_present_happy_path() {
    let mut body = pseudo_random_body(4096, 1);
    body.extend_from_slice(&make_trailer(b"stable", None));
    assert_eq!(parse_appimage_channel_override(&body).as_deref(), Some("stable"));
    let (_dir, path) = temp_file_with(&body);
    assert_eq!(try_read_appimage_channel_override(&path).as_deref(), Some("stable"));
}

#[test]
fn test_appimage_trailer_tiny_and_empty_files() {
    let (_d1, p1) = temp_file_with(&[0u8; 10]);
    assert_eq!(try_read_appimage_channel_override(&p1), None);
    let (_d2, p2) = temp_file_with(&[]);
    assert_eq!(try_read_appimage_channel_override(&p2), None);
    // a file that is EXACTLY one trailer and nothing else
    let (_d3, p3) = temp_file_with(&make_trailer(b"stable", None));
    assert_eq!(try_read_appimage_channel_override(&p3).as_deref(), Some("stable"));
}

#[test]
fn test_appimage_trailer_length_zero_is_invalid() {
    let mut body = pseudo_random_body(512, 2);
    body.extend_from_slice(&make_trailer(b"junkjunk", Some(0)));
    assert_eq!(parse_appimage_channel_override(&body), None);
}

#[test]
fn test_appimage_trailer_length_over_255_is_invalid() {
    let mut body = pseudo_random_body(512, 3);
    body.extend_from_slice(&make_trailer(&vec![b'a'; 300], Some(300)));
    assert_eq!(parse_appimage_channel_override(&body), None);
}

#[test]
fn test_appimage_trailer_truncated_channel_is_invalid() {
    let mut body = pseudo_random_body(512, 4);
    body.extend_from_slice(&make_trailer(b"short", Some(20))); // declares 20 bytes, only 5 present
    assert_eq!(parse_appimage_channel_override(&body), None);
}

#[test]
fn test_appimage_trailer_invalid_channel_bytes() {
    for bad in [0x20u8, 0x00, 0x7F, 0xC3] {
        let channel = [b's', b't', b'a', bad, b'l', b'e'];
        let mut body = pseudo_random_body(512, 5);
        body.extend_from_slice(&make_trailer(&channel, None));
        assert_eq!(
            parse_appimage_channel_override(&body),
            None,
            "channel byte 0x{:02x} should be invalid",
            bad
        );
    }
}

#[test]
fn test_appimage_trailer_invalid_at_eof_falls_back_to_earlier_valid() {
    let mut body = pseudo_random_body(512, 6);
    body.extend_from_slice(&make_trailer(b"beta", None));
    body.extend_from_slice(&make_trailer(b"", Some(0))); // magic + u16le(0) at EOF
    assert_eq!(parse_appimage_channel_override(&body).as_deref(), Some("beta"));
}

#[test]
fn test_appimage_trailer_double_append_last_valid_wins() {
    let mut body = pseudo_random_body(512, 7);
    body.extend_from_slice(&make_trailer(b"beta", None));
    body.extend_from_slice(&make_trailer(b"stable", None));
    assert_eq!(parse_appimage_channel_override(&body).as_deref(), Some("stable"));
}

#[test]
fn test_appimage_trailer_trailing_garbage_tolerated() {
    let mut body = pseudo_random_body(512, 8);
    body.extend_from_slice(&make_trailer(b"stable", None));
    body.extend_from_slice(&pseudo_random_body(40, 9));
    assert_eq!(parse_appimage_channel_override(&body).as_deref(), Some("stable"));
}

#[test]
fn test_appimage_trailer_long_channel_and_no_case_folding() {
    // exactly 255 chars from the allowed 0x21..=0x7E set, incl. mixed punctuation
    let charset: &[u8] = b"abcdefghijklmnopqrstuvwxyz0123456789-._~!#$%&";
    let channel: Vec<u8> = (0..255).map(|i| charset[i % charset.len()]).collect();
    let mut body = pseudo_random_body(512, 10);
    body.extend_from_slice(&make_trailer(&channel, None));
    let parsed = parse_appimage_channel_override(&body).unwrap();
    assert_eq!(parsed.as_bytes(), &channel[..]);

    // the reader does not normalize: mixed case is returned verbatim
    let mut body2 = pseudo_random_body(512, 11);
    body2.extend_from_slice(&make_trailer(b"StAbLe", None));
    assert_eq!(parse_appimage_channel_override(&body2).as_deref(), Some("StAbLe"));
}

#[test]
fn test_appimage_trailer_scan_window_boundary() {
    // (a) magic starts exactly at the first byte of the 1024-byte window -> found
    let mut body = pseudo_random_body(3072, 12);
    body.extend_from_slice(&make_trailer(b"stable", None));
    let pad = 4096 - body.len();
    body.extend_from_slice(&pseudo_random_body(pad, 13));
    assert_eq!(body.len(), 4096);
    let (_d1, p1) = temp_file_with(&body);
    assert_eq!(try_read_appimage_channel_override(&p1).as_deref(), Some("stable"));

    // (b) magic starts one byte before the window -> not found
    let mut body = pseudo_random_body(3071, 12);
    body.extend_from_slice(&make_trailer(b"stable", None));
    let pad = 4096 - body.len();
    body.extend_from_slice(&pseudo_random_body(pad, 13));
    assert_eq!(body.len(), 4096);
    let (_d2, p2) = temp_file_with(&body);
    assert_eq!(try_read_appimage_channel_override(&p2), None);

    // (c) file smaller than the window with the trailer at offset 0 -> found
    let mut body = make_trailer(b"stable", None);
    body.extend_from_slice(&pseudo_random_body(460, 14));
    assert_eq!(body.len(), 500);
    let (_d3, p3) = temp_file_with(&body);
    assert_eq!(try_read_appimage_channel_override(&p3).as_deref(), Some("stable"));
}

#[test]
fn test_appimage_trailer_magic_constant_matches_contract() {
    // Locks the constant against typos and mirrors the C# magic-sanity test.
    use sha2::Digest;
    let hash = sha2::Sha256::digest(b"velopack appimage channel override");
    assert_eq!(APPIMAGE_CHANNEL_MAGIC[..], hash[..]);
}

#[test]
fn test_appimage_trailer_golden_vector() {
    // Shared cross-repo golden vector (CONTRACTS.md §1): MAGIC ++ u16le(6) ++ "stable".
    // Identical bytes are asserted in the C# AppImageChannelOverrideTests and the
    // velopack.api promotion worker tests — do not change without updating all three.
    let mut vector: Vec<u8> = vec![
        0xde, 0xed, 0x1b, 0xad, 0x30, 0x15, 0xb1, 0x96, 0x9e, 0x6e, 0xbf, 0x7d, 0x09, 0x3f, 0x5d, 0xca, 0x6c, 0x6c, 0x52, 0xa1, 0xa0, 0xa2, 0x57,
        0x57, 0x19, 0x91, 0x62, 0x83, 0x11, 0xd8, 0x03, 0x51,
    ];
    vector.extend_from_slice(&[0x06, 0x00]);
    vector.extend_from_slice(b"stable");
    assert_eq!(vector, make_trailer(b"stable", None));
    assert_eq!(parse_appimage_channel_override(&vector).as_deref(), Some("stable"));
}

#[test]
fn test_appimage_trailer_missing_file_returns_none() {
    let dir = tempfile::TempDir::new().unwrap();
    let path = dir.path().join("does-not-exist.AppImage");
    assert_eq!(try_read_appimage_channel_override(&path), None);
}
