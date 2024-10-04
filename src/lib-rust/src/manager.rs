#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;
use std::{
    fs,
    process::{exit, Command as Process},
    sync::mpsc::Sender,
};

#[cfg(feature = "async")]
use async_std::channel::Sender as AsyncSender;
#[cfg(feature = "async")]
use async_std::task::JoinHandle;
use semver::Version;
use serde::{Deserialize, Serialize};

use crate::{
    locator::{self, VelopackLocatorConfig, LocationContext, VelopackLocator},
    sources::UpdateSource,
    Error,
    util,
};

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
/// A feed of Velopack assets, usually retrieved from a remote location.
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

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
#[serde(default)]
/// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
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

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
#[serde(default)]
/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
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

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[cfg_attr(feature = "typescript", derive(ts_rs::TS))]
#[serde(default)]
/// Options to customise the behaviour of UpdateManager.
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
        let locator = if let Some(config) = locator {
            let manifest = config.load_manifest()?;
            VelopackLocator::new(config.clone(), manifest)
        } else {
            locator::auto_locate_app_manifest(LocationContext::FromCurrentExe)?
        };
        Ok(UpdateManager {
            options: options.unwrap_or_default(),
            source: source.clone_boxed(),
            locator,
        })
    }

    fn get_practical_channel(&self) -> String {
        let options_channel = self.options.ExplicitChannel.as_deref();
        let app_channel = self.locator.get_manifest_channel();
        let mut channel = options_channel.unwrap_or(&app_channel).to_string();
        if channel.is_empty() {
            channel = locator::default_channel_name();
        }
        channel
    }

    /// The currently installed app version.
    pub fn get_current_version(&self) -> String {
        self.locator.get_manifest_version_full_string()
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

    #[cfg(feature = "async")]
    /// Get a list of available remote releases from the package source.
    pub fn get_release_feed_async(&self) -> JoinHandle<Result<VelopackAssetFeed, Error>>
    {
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
                "Latest remote release is the same version of a different channel, and downgrade is enabled ({} -> {}).",
                app_version, remote_version
            );
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: true }))
        } else {
            Ok(UpdateCheck::NoUpdateAvailable)
        }
    }

    #[cfg(feature = "async")]
    /// Checks for updates, returning None if there are none available. If there are updates available, this method will return an
    /// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
    pub fn check_for_updates_async(&self) -> JoinHandle<Result<UpdateCheck, Error>>
    {
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
        let name = &update.TargetFullRelease.FileName;
        let packages_dir = &self.locator.get_packages_dir();

        fs::create_dir_all(packages_dir)?;
        let target_file = packages_dir.join(name);

        if target_file.exists() {
            info!("Package already exists on disk, skipping download: '{}'", target_file.to_string_lossy());
            return Ok(());
        }

        let g = format!("{}/*.nupkg", packages_dir.to_string_lossy());
        info!("Searching for packages to clean in: '{}'", g);
        let mut to_delete = Vec::new();
        match glob::glob(&g) {
            Ok(paths) => {
                for path in paths {
                    if let Ok(path) = path {
                        to_delete.push(path.clone());
                        debug!("Will delete: '{}'", path.to_string_lossy());
                    }
                }
            }
            Err(e) => {
                error!("Error while searching for packages to clean: {}", e);
            }
        }

        self.source.download_release_entry(&update.TargetFullRelease, &target_file.to_string_lossy(), progress)?;
        info!("Successfully placed file: '{}'", target_file.to_string_lossy());

        // extract new Update.exe on Windows only
        #[cfg(target_os = "windows")]
        match crate::bundle::load_bundle_from_file(&target_file) {
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
            info!("Cleaning up old package: '{}'", path.to_string_lossy());
            let _ = fs::remove_file(&path);
        }

        Ok(())
    }

    #[cfg(feature = "async")]
    /// Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
    /// This function will acquire a global update lock so may fail if there is already another update operation in progress.
    /// - If the update contains delta packages and the delta feature is enabled
    ///   this method will attempt to unpack and prepare them.
    /// - If there is no delta update available, or there is an error preparing delta
    ///   packages, this method will fall back to downloading the full version of the update.
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
        C: IntoIterator<Item=S>,
    {
        self.wait_exit_then_apply_updates(to_apply, false, true, restart_args)?;
        exit(0);
    }

    /// This will exit your app immediately and apply specified updates. It will not restart your app afterwards.
    /// If you need to save state or clean up, you should do that before calling this method.
    /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
    pub fn apply_updates_and_exit<A, C, S>(&self, to_apply: A) -> Result<(), Error>
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
        C: IntoIterator<Item=S>,
    {
        let to_apply = to_apply.as_ref();
        let pkg_path = self.locator.get_packages_dir().join(&to_apply.FileName);
        let pkg_path_str = pkg_path.to_string_lossy();

        let mut args = Vec::new();
        args.push("apply".to_string());
        args.push("--waitPid".to_string());
        args.push(format!("{}", std::process::id()));
        args.push("--package".to_string());
        args.push(pkg_path_str.into_owned());

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
        p.current_dir(&self.locator.get_root_dir());

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