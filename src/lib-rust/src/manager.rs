#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;
use std::{
    fs,
    process::{exit, Command as Process},
    sync::mpsc::Sender,
};

use semver::Version;
use serde::{Deserialize, Serialize};

#[cfg(feature = "async")]
use async_std::channel::Sender as AsyncSender;
#[cfg(feature = "async")]
use async_std::task::JoinHandle;

use crate::{
    locator::{self, LocationContext, VelopackLocator, VelopackLocatorConfig},
    sources::UpdateSource,
    util, Error,
};

/// Configure how the update process should wait before applying updates.
pub enum ApplyWaitMode {
    /// NOT RECOMMENDED: Will not wait for any process before continuing. This could result in the update process being
    /// killed, or the update process itself failing.
    NoWait,
    /// Will wait for the current process to exit before continuing. This is the default and recommended mode.
    WaitCurrentProcess,
    /// Wait for the specified process ID to exit before continuing. This is useful if you are updating a program
    /// different from the one that is currently running.
    WaitPid(u32),
}

/// A feed of Velopack assets, usually retrieved from a remote location.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct VelopackAssetFeed {
    /// The list of assets in the (probably remote) update feed.
    pub Assets: Vec<VelopackAsset>,
}

impl VelopackAssetFeed {
    /// Finds a release by name and returns a reference to the VelopackAsset in the feed, or None if not found.
    pub fn find(&self, release_name: &str) -> Option<&VelopackAsset> {
        self.Assets.iter().find(|x| x.FileName.eq_ignore_ascii_case(release_name))
    }
}

/// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
#[serde(default)]
pub struct VelopackAsset {
    /// The name or Id of the package containing this release.
    pub PackageId: String,
    /// The version of this release.
    pub Version: String,
    /// The type of asset (eg. "Full" or "Delta").
    pub Type: String,
    /// The filename of the update package containing this release.
    pub FileName: String,
    /// The SHA1 checksum of the update package containing this release.
    pub SHA1: String,
    /// The SHA256 checksum of the update package containing this release.
    pub SHA256: String,
    /// The size in bytes of the update package containing this release.
    pub Size: u64,
    /// The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
    pub NotesMarkdown: String,
    /// The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
    pub NotesHtml: String,
}

/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
#[serde(default)]
pub struct UpdateInfo {
    /// The available version that we are updating to.
    pub TargetFullRelease: VelopackAsset,
    /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
    /// In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
    /// deleted.
    pub IsDowngrade: bool,
}

impl AsRef<VelopackAsset> for UpdateInfo {
    fn as_ref(&self) -> &VelopackAsset {
        &self.TargetFullRelease
    }
}

impl AsRef<VelopackAsset> for VelopackAsset {
    fn as_ref(&self) -> &VelopackAsset {
        &self
    }
}

/// Options to customise the behaviour of UpdateManager.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
#[serde(default)]
pub struct UpdateOptions {
    /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
    /// This could happen if a release has bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest version on that
    /// channel is lower than the current version.
    pub AllowVersionDowngrade: bool,
    /// **This option should usually be left None**. <br/>
    /// Overrides the default channel used to fetch updates.
    /// The default channel will be whatever channel was specified on the command line when building this release.
    /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
    /// This allows users to automatically receive updates from the same channel they installed from. This options
    /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
    /// without having to reinstall the application.
    pub ExplicitChannel: Option<String>,
}

/// Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
#[derive(Clone)]
pub struct UpdateManager {
    options: UpdateOptions,
    source: Box<dyn UpdateSource>,
    locator: VelopackLocator,
}

/// Represents the result of a call to check for updates.
pub enum UpdateCheck {
    /// The remote feed is empty, so no update check was performed
    RemoteIsEmpty,
    /// The remote feed had releases, but none were newer or more relevant than the current version
    NoUpdateAvailable,
    /// The remote feed had an update available
    UpdateAvailable(UpdateInfo),
}

impl UpdateManager {
    /// Create a new UpdateManager instance using the specified UpdateSource.
    /// This will return an error if the application is not yet installed.
    /// ## Example:
    /// ```rust
    /// use velopack::*;
    ///
    /// let source = sources::HttpSource::new("https://the.place/you-host/updates");
    /// let um = UpdateManager::new(source, None, None);
    /// ```
    pub fn new<T: UpdateSource>(
        source: T,
        options: Option<UpdateOptions>,
        locator: Option<VelopackLocatorConfig>,
    ) -> Result<UpdateManager, Error> {
        UpdateManager::new_boxed(source.clone_boxed(), options, locator)
    }

    /// Create a new UpdateManager instance using the specified UpdateSource.
    /// This will return an error if the application is not yet installed.
    /// ## Example:
    /// ```rust
    /// use velopack::*;
    ///
    /// let source = sources::HttpSource::new("https://the.place/you-host/updates");
    /// let um = UpdateManager::new_boxed(Box::new(source), None, None);
    /// ```
    pub fn new_boxed(
        source: Box<dyn UpdateSource>,
        options: Option<UpdateOptions>,
        locator: Option<VelopackLocatorConfig>,
    ) -> Result<UpdateManager, Error> {
        let locator = if let Some(config) = locator {
            warn!("Using explicit locator configuration, ignoring auto-locate.");
            let manifest = config.load_manifest()?;
            VelopackLocator::new(config.clone(), manifest)
        } else {
            locator::auto_locate_app_manifest(LocationContext::FromCurrentExe)?
        };
        Ok(UpdateManager { options: options.unwrap_or_default(), source, locator })
    }

    fn get_practical_channel(&self) -> String {
        let options_channel = self.options.ExplicitChannel.as_deref();
        let app_channel = self.locator.get_manifest_channel();
        let mut channel = options_channel.unwrap_or(&app_channel).to_string();
        if channel.is_empty() {
            warn!("Channel is empty, picking default.");
            channel = locator::default_channel_name();
        }
        info!("Chosen channel for updates: {:?} (explicit={:?}, memorized={:?})", channel, options_channel, app_channel);
        channel
    }

    /// The currently installed app version as a string.
    pub fn get_current_version_as_string(&self) -> String {
        self.locator.get_manifest_version_full_string()
    }

    /// The currently installed app version as a semver Version.
    pub fn get_current_version(&self) -> Version {
        self.locator.get_manifest_version()
    }

    /// The currently installed app id.
    pub fn get_app_id(&self) -> String {
        self.locator.get_manifest_id()
    }

    /// Check if the app is in portable mode. This can be true or false on Windows.
    /// On Linux and MacOS, this will always return true.
    pub fn get_is_portable(&self) -> bool {
        self.locator.get_is_portable()
    }

    /// Returns None if there is no local package waiting to be applied. Returns a VelopackAsset
    /// if there is an update downloaded which has not yet been applied. In that case, the
    /// VelopackAsset can be applied by calling apply_updates_and_restart or wait_exit_then_apply_updates.
    pub fn get_update_pending_restart(&self) -> Option<VelopackAsset> {
        let packages_dir = self.locator.get_packages_dir();
        if let Some((path, manifest)) = locator::find_latest_full_package(&packages_dir) {
            if manifest.version > self.locator.get_manifest_version() {
                return Some(VelopackAsset {
                    PackageId: manifest.id,
                    Version: manifest.version.to_string(),
                    Type: "Full".to_string(),
                    FileName: path.file_name().unwrap().to_string_lossy().to_string(),
                    SHA1: util::calculate_file_sha1(&path).unwrap_or_default(),
                    SHA256: util::calculate_file_sha256(&path).unwrap_or_default(),
                    Size: path.metadata().map(|m| m.len()).unwrap_or(0),
                    NotesMarkdown: manifest.release_notes,
                    NotesHtml: manifest.release_notes_html,
                });
            }
        }
        None
    }

    /// Get a list of available remote releases from the package source.
    pub fn get_release_feed(&self) -> Result<VelopackAssetFeed, Error> {
        let channel = self.get_practical_channel();
        self.source.get_release_feed(&channel, &self.locator.get_manifest())
    }

    /// Get a list of available remote releases from the package source.
    #[cfg(feature = "async")]
    pub fn get_release_feed_async(&self) -> JoinHandle<Result<VelopackAssetFeed, Error>> {
        let self_clone = self.clone();
        async_std::task::spawn_blocking(move || self_clone.get_release_feed())
    }

    /// Checks for updates, returning None if there are none available. If there are updates available, this method will return an
    /// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
    pub fn check_for_updates(&self) -> Result<UpdateCheck, Error> {
        let allow_downgrade = self.options.AllowVersionDowngrade;
        let app_channel = self.locator.get_manifest_channel();
        let app_version = self.locator.get_manifest_version();
        let feed = self.get_release_feed()?;
        let assets = feed.Assets;

        let practical_channel = self.get_practical_channel();
        let is_non_default_channel = practical_channel != app_channel;

        if assets.is_empty() {
            return Ok(UpdateCheck::RemoteIsEmpty);
        }

        let mut latest: Option<VelopackAsset> = None;
        let mut latest_version: Version = Version::parse("0.0.0")?;
        for asset in assets {
            if let Ok(sv) = Version::parse(&asset.Version) {
                if asset.Type.eq_ignore_ascii_case("Full") {
                    debug!("Found full release: {} ({}).", asset.FileName, sv.to_string());
                    if latest.is_none() || (sv > latest_version) {
                        latest = Some(asset);
                        latest_version = sv;
                    }
                }
            }
        }

        if latest.is_none() {
            return Ok(UpdateCheck::RemoteIsEmpty);
        }

        let remote_version = latest_version;
        let remote_asset = latest.unwrap();

        debug!("Latest remote release: {} ({}).", remote_asset.FileName, remote_version.to_string());

        if remote_version > app_version {
            info!("Found newer remote release available ({} -> {}).", app_version, remote_version);
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: false }))
        } else if remote_version < app_version && allow_downgrade {
            info!("Found older remote release available and downgrade is enabled ({} -> {}).", app_version, remote_version);
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: true }))
        } else if remote_version == app_version && allow_downgrade && is_non_default_channel {
            info!(
                "Latest remote release is the same version of a different channel, and downgrade is enabled ({} -> {}, {} -> {}).",
                app_version, remote_version, app_channel, practical_channel
            );
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: true }))
        } else {
            Ok(UpdateCheck::NoUpdateAvailable)
        }
    }

    /// Checks for updates, returning None if there are none available. If there are updates available, this method will return an
    /// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
    #[cfg(feature = "async")]
    pub fn check_for_updates_async(&self) -> JoinHandle<Result<UpdateCheck, Error>> {
        let self_clone = self.clone();
        async_std::task::spawn_blocking(move || self_clone.check_for_updates())
    }

    /// Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
    /// This function will acquire a global update lock so may fail if there is already another update operation in progress.
    /// - If the update contains delta packages and the delta feature is enabled
    ///   this method will attempt to unpack and prepare them.
    /// - If there is no delta update available, or there is an error preparing delta
    ///   packages, this method will fall back to downloading the full version of the update.
    pub fn download_updates(&self, update: &UpdateInfo, progress: Option<Sender<i16>>) -> Result<(), Error> {
        let _mutex = &self.locator.try_get_exclusive_lock()?;
        let name = &update.TargetFullRelease.FileName;
        let packages_dir = &self.locator.get_packages_dir();

        fs::create_dir_all(packages_dir)?;
        let final_target_file = packages_dir.join(name);
        let partial_file = packages_dir.join(format!("{}.partial", name));

        if final_target_file.exists() {
            info!("Package already exists on disk, skipping download: '{}'", final_target_file.to_string_lossy());
            return Ok(());
        }

        let old_nupkg_pattern = format!("{}/*.nupkg", packages_dir.to_string_lossy());
        let old_partial_pattern = format!("{}/*.partial", packages_dir.to_string_lossy());
        let mut to_delete = Vec::new();

        fn find_files_to_delete(pattern: &str, to_delete: &mut Vec<String>) {
            info!("Searching for packages to clean: '{}'", pattern);
            match glob::glob(pattern) {
                Ok(paths) => {
                    for path in paths.into_iter().flatten() {
                        to_delete.push(path.to_string_lossy().to_string());
                    }
                }
                Err(e) => {
                    error!("Error while searching for packages to clean: {}", e);
                }
            }
        }

        find_files_to_delete(&old_nupkg_pattern, &mut to_delete);
        find_files_to_delete(&old_partial_pattern, &mut to_delete);

        self.source.download_release_entry(&update.TargetFullRelease, &partial_file.to_string_lossy(), progress)?;
        info!("Successfully placed file: '{}'", partial_file.to_string_lossy());

        info!("Renaming partial file to final target: '{}'", final_target_file.to_string_lossy());
        fs::rename(&partial_file, &final_target_file)?;

        // extract new Update.exe on Windows only
        #[cfg(target_os = "windows")]
        match crate::bundle::load_bundle_from_file(&final_target_file) {
            Ok(bundle) => {
                info!("Bundle loaded successfully.");
                let update_exe_path = self.locator.get_update_path();
                if let Err(e) = bundle.extract_zip_predicate_to_path(|f| f.ends_with("Squirrel.exe"), update_exe_path) {
                    error!("Error extracting Update.exe from bundle: {}", e);
                }
            }
            Err(e) => {
                error!("Error loading bundle: {}", e);
            }
        }

        for path in to_delete {
            info!("Deleting up old package: '{}'", path);
            let _ = fs::remove_file(&path);
        }

        Ok(())
    }

    /// Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
    /// This function will acquire a global update lock so may fail if there is already another update operation in progress.
    /// - If the update contains delta packages and the delta feature is enabled
    ///   this method will attempt to unpack and prepare them.
    /// - If there is no delta update available, or there is an error preparing delta
    ///   packages, this method will fall back to downloading the full version of the update.
    #[cfg(feature = "async")]
    pub fn download_updates_async(&self, update: &UpdateInfo, progress: Option<AsyncSender<i16>>) -> JoinHandle<Result<(), Error>> {
        let mut sync_progress: Option<Sender<i16>> = None;

        if let Some(async_sender) = progress {
            let (sync_sender, sync_receiver) = std::sync::mpsc::channel::<i16>();
            sync_progress = Some(sync_sender);

            // Spawn an async task to bridge from sync to async
            async_std::task::spawn(async move {
                for progress_value in sync_receiver {
                    // Send to the async_std channel, ignore errors (e.g., receiver dropped)
                    let _ = async_sender.send(progress_value).await;
                }
            });
        }

        let self_clone = self.clone();
        let update_clone = update.clone();
        async_std::task::spawn_blocking(move || self_clone.download_updates(&update_clone, sync_progress))
    }

    /// This will exit your app immediately, apply updates, and then relaunch the app.
    /// If you need to save state or clean up, you should do that before calling this method.
    /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
    pub fn apply_updates_and_restart<A>(&self, to_apply: A) -> Result<(), Error>
    where
        A: AsRef<VelopackAsset>,
    {
        self.wait_exit_then_apply_updates(to_apply, false, true, Vec::<String>::new())?;
        exit(0);
    }

    /// This will exit your app immediately, apply updates, and then relaunch the app using the specified
    /// restart arguments. If you need to save state or clean up, you should do that before calling this method.
    /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
    pub fn apply_updates_and_restart_with_args<A, C, S>(&self, to_apply: A, restart_args: C) -> Result<(), Error>
    where
        A: AsRef<VelopackAsset>,
        S: AsRef<str>,
        C: IntoIterator<Item = S>,
    {
        self.wait_exit_then_apply_updates(to_apply, false, true, restart_args)?;
        exit(0);
    }

    /// This will exit your app immediately and apply specified updates. It will not restart your app afterwards.
    /// If you need to save state or clean up, you should do that before calling this method.
    /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
    pub fn apply_updates_and_exit<A>(&self, to_apply: A) -> Result<(), Error>
    where
        A: AsRef<VelopackAsset>,
    {
        self.wait_exit_then_apply_updates(to_apply, false, false, Vec::<String>::new())?;
        exit(0);
    }

    /// This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
    /// You clean up any state and exit your app after calling this method.
    /// Once your app exists, the updater will apply updates and optionally restart your app.
    /// The updater will only wait for 60 seconds before giving up.
    pub fn wait_exit_then_apply_updates<A, C, S>(&self, to_apply: A, silent: bool, restart: bool, restart_args: C) -> Result<(), Error>
    where
        A: AsRef<VelopackAsset>,
        S: AsRef<str>,
        C: IntoIterator<Item = S>,
    {
        self.unsafe_apply_updates(to_apply, silent, ApplyWaitMode::WaitCurrentProcess, restart, restart_args)?;
        Ok(())
    }

    /// This will launch the Velopack updater and optionally wait for a program to exit gracefully.
    /// This method is unsafe because it does not necessarily wait for any / the correct process to exit 
    /// before applying updates. The `wait_exit_then_apply_updates` method is recommended for most use cases.
    pub fn unsafe_apply_updates<A, C, S>(
        &self,
        to_apply: A,
        silent: bool,
        wait_mode: ApplyWaitMode,
        restart: bool,
        restart_args: C,
    ) -> Result<(), Error>
    where
        A: AsRef<VelopackAsset>,
        S: AsRef<str>,
        C: IntoIterator<Item = S>,
    {
        let to_apply = to_apply.as_ref();
        let pkg_path = self.locator.get_packages_dir().join(&to_apply.FileName);
        let pkg_path_str = pkg_path.to_string_lossy();

        let mut args = Vec::new();
        args.push("apply".to_string());

        args.push("--package".to_string());
        args.push(pkg_path_str.to_string());

        if !pkg_path.exists() {
            error!("Package does not exist on disk: '{}'", &pkg_path_str);
            return Err(Error::FileNotFound(pkg_path_str.to_string()));
        }

        match wait_mode {
            ApplyWaitMode::NoWait => {}
            ApplyWaitMode::WaitCurrentProcess => {
                args.push("--waitPid".to_string());
                args.push(format!("{}", std::process::id()));
            }
            ApplyWaitMode::WaitPid(pid) => {
                args.push("--waitPid".to_string());
                args.push(format!("{}", pid));
            }
        }

        if silent {
            args.push("--silent".to_string());
        }
        if !restart {
            args.push("--norestart".to_string());
        }

        let restart_args: Vec<String> = restart_args.into_iter().map(|item| item.as_ref().to_string()).collect();

        if !restart_args.is_empty() {
            args.push("--".to_string());
            for arg in restart_args {
                args.push(arg);
            }
        }

        let mut p = Process::new(&self.locator.get_update_path());
        p.args(&args);

        if let Some(update_exe_parent) = self.locator.get_update_path().parent() {
            p.current_dir(update_exe_parent);
        }

        #[cfg(target_os = "windows")]
        {
            const CREATE_NO_WINDOW: u32 = 0x08000000;
            p.creation_flags(CREATE_NO_WINDOW);
        }

        info!("About to run Update.exe: {} {:?}", self.locator.get_update_path_as_string(), args);
        p.spawn()?;
        Ok(())
    }
}
