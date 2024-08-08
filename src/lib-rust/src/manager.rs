use std::{fs, process::{exit, Command as Process}, rc::Rc, sync::mpsc::Sender};
#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[cfg(feature = "async")]
use async_std::channel::Sender;
#[cfg(feature = "async")]
use async_std::task::JoinHandle;
use semver::Version;
use serde::{Deserialize, Serialize};

use crate::{Error, locator::{self, VelopackLocator}, sources::UpdateSource};

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

#[derive(Clone)]
#[allow(non_snake_case)]
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
pub struct UpdateManager<'a>
{
    allow_version_downgrade: bool,
    explicit_channel: Option<String>,
    source: Rc<Box<dyn UpdateSource + 'a>>,
    paths: VelopackLocator,
}

// impl Clone for UpdateManager {
//     fn clone(&self) -> Self {
//         UpdateManager {
//             allow_version_downgrade: self.allow_version_downgrade,
//             explicit_channel: self.explicit_channel.clone(),
//             source: self.source.clone(),
//             paths: self.paths.clone(),
//         }
//     }
// }

/// Arguments to pass to the Update.exe process when restarting the application after applying updates.
pub enum RestartArgs<'a> {
    /// No arguments to pass to the restart process.
    None,
    /// Arguments to pass to the restart process, as borrowed strings.
    Some(Vec<&'a str>),
    /// Arguments to pass to the restart process, as owned strings.
    SomeOwned(Vec<String>),
}

impl<'a> IntoIterator for RestartArgs<'a> {
    type Item = String;
    type IntoIter = std::vec::IntoIter<String>;

    fn into_iter(self) -> Self::IntoIter {
        match self {
            RestartArgs::None => Vec::new().into_iter(),
            RestartArgs::Some(args) => args.into_iter().map(|s| s.to_string()).collect::<Vec<String>>().into_iter(),
            RestartArgs::SomeOwned(args) => args.into_iter().collect::<Vec<String>>().into_iter(),
        }
    }
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

impl<'a> UpdateManager<'a> {
    /// Create a new UpdateManager instance using the specified UpdateSource.
    /// This will return an error if the application is not yet installed.
    /// ## Example:
    /// ```rust
    /// use velopack::*;
    ///
    /// let source = sources::HttpSource::new("https://the.place/you-host/updates");
    /// let um = UpdateManager::new(source, None);
    /// ```
    pub fn new<T: UpdateSource + 'a>(source: T, options: Option<UpdateOptions>) -> Result<UpdateManager::<'a>, Error> {
        Ok(UpdateManager {
            paths: locator::auto_locate()?,
            allow_version_downgrade: options.as_ref().map(|f| f.AllowVersionDowngrade).unwrap_or(false),
            explicit_channel: options.as_ref().map(|f| f.ExplicitChannel.clone()).unwrap_or(None),
            source: Rc::new(Box::new(source)),
        })
    }

    fn get_practical_channel(&self) -> String {
        let channel = self.explicit_channel.as_deref();
        let mut channel = channel.unwrap_or(&self.paths.manifest.channel).to_string();
        if channel.is_empty() {
            channel = get_default_channel();
        }
        channel
    }

    /// The currently installed app version when you created your release.
    pub fn current_version(&self) -> Result<String, Error> {
        Ok(self.paths.manifest.version.to_string())
    }

    /// Get a list of available remote releases from the package source.
    pub fn get_release_feed(&self) -> Result<VelopackAssetFeed, Error> {
        let channel = self.get_practical_channel();
        self.source.get_release_feed(&channel, &self.paths.manifest)
    }

    #[cfg(feature = "async")]
    /// Get a list of available remote releases from the package source.
    pub fn get_release_feed_async(&self) -> JoinHandle<Result<VelopackAssetFeed, Error>>
        where
            T: 'static,
    {
        let self_clone = self.clone();
        async_std::task::spawn_blocking(move || self_clone.get_release_feed())
    }

    /// Checks for updates, returning None if there are none available. If there are updates available, this method will return an
    /// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
    pub fn check_for_updates(&self) -> Result<UpdateCheck, Error> {
        let allow_downgrade = self.allow_version_downgrade;
        let app = &self.paths.manifest;
        let feed = self.get_release_feed()?;
        let assets = feed.Assets;

        let practical_channel = self.get_practical_channel();
        let is_non_default_channel = practical_channel != app.channel;

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

        if remote_version > app.version {
            info!("Found newer remote release available ({} -> {}).", app.version, remote_version);
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: false }))
        } else if remote_version < app.version && allow_downgrade {
            info!("Found older remote release available and downgrade is enabled ({} -> {}).", app.version, remote_version);
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: true }))
        } else if remote_version == app.version && allow_downgrade && is_non_default_channel {
            info!(
                "Latest remote release is the same version of a different channel, and downgrade is enabled ({} -> {}).",
                app.version, remote_version
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
        where T: 'static,
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
    pub fn download_updates(&self, update: &UpdateInfo, progress: Option<Sender<i16>>) -> Result<(), Error>
    {
        let name = &update.TargetFullRelease.FileName;
        let packages_dir = &self.paths.packages_dir;
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
                if let Err(e) = bundle.extract_zip_predicate_to_path(|f| f.ends_with("Squirrel.exe"), &self.paths.update_exe_path) {
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
    pub fn download_updates_async(&self, update: &UpdateInfo, progress: Option<Sender<i16>>) -> JoinHandle<Result<(), Error>>
    {
        let self_clone = self.clone();
        let update_clone = update.clone();
        async_std::task::spawn_blocking(move || self_clone.download_updates(&update_clone, progress))
    }

    /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified
    /// restart arguments. If you need to save state or clean up, you should do that before calling this method.
    /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
    pub fn apply_updates_and_restart<A: AsRef<VelopackAsset>>(&self, to_apply: A, restart_args: RestartArgs) -> Result<(), Error> {
        self.wait_exit_then_apply_updates(to_apply, false, true, restart_args)?;
        exit(0);
    }

    /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified
    /// restart arguments. If you need to save state or clean up, you should do that before calling this method.
    /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
    pub fn apply_updates_and_exit<A: AsRef<VelopackAsset>>(&self, to_apply: A) -> Result<(), Error> {
        self.wait_exit_then_apply_updates(to_apply, false, false, RestartArgs::None)?;
        exit(0);
    }

    /// This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
    /// You should then clean up any state and exit your app. The updater will apply updates and then
    /// optionally restart your app. The updater will only wait for 60 seconds before giving up.
    pub fn wait_exit_then_apply_updates<A: AsRef<VelopackAsset>>(
        &self,
        to_apply: A,
        silent: bool,
        restart: bool,
        restart_args: RestartArgs,
    ) -> Result<(), Error> {
        let to_apply = to_apply.as_ref();
        let pkg_path = self.paths.packages_dir.join(&to_apply.FileName);
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
        if restart {
            args.push("--restart".to_string());
        }

        match restart_args {
            RestartArgs::None => {}
            RestartArgs::Some(ref ra) => {
                args.push("--".to_string());
                for arg in ra {
                    args.push(arg.to_string());
                }
            }
            RestartArgs::SomeOwned(ref ra) => {
                args.push("--".to_string());
                for arg in ra {
                    args.push(arg.clone());
                }
            }
        }

        let mut p = Process::new(&self.paths.update_exe_path);
        p.args(&args);
        p.current_dir(&self.paths.root_app_dir);

        #[cfg(target_os = "windows")]
        {
            const CREATE_NO_WINDOW: u32 = 0x08000000;
            p.creation_flags(CREATE_NO_WINDOW);
        }

        info!("About to run Update.exe: {} {:?}", self.paths.update_exe_path.to_string_lossy(), args);
        p.spawn()?;
        Ok(())
    }
}

fn get_default_channel() -> String {
    #[cfg(target_os = "windows")]
    return "win".to_owned();
    #[cfg(target_os = "linux")]
    return "linux".to_owned();
    #[cfg(target_os = "macos")]
    return "osx".to_owned();
}
