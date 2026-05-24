use serde::{Deserialize, Serialize};

define_struct_case_insensitive! {
    /// A feed of Velopack assets, usually retrieved from a remote location.
    #[allow(non_snake_case)]
    #[derive(Serialize, Debug, Clone, Default)]
    pub struct VelopackAssetFeed {
        /// The list of assets in the (probably remote) update feed.
        pub Assets: Vec<VelopackAsset>,
    }
}

impl VelopackAssetFeed {
    /// Finds a release by name and returns a reference to the VelopackAsset
    /// in the feed, or None if not found.
    pub fn find(&self, release_name: &str) -> Option<&VelopackAsset> {
        self.Assets.iter().find(|x| x.FileName.eq_ignore_ascii_case(release_name))
    }
}

define_struct_case_insensitive! {
    /// An individual Velopack asset, could refer to an asset on-disk or in a
    /// remote package feed.
    #[allow(non_snake_case)]
    #[derive(Serialize, Debug, Clone, Default)]
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
        /// The release notes in markdown format, as passed to Velopack when
        /// packaging the release. This may be an empty string.
        pub NotesMarkdown: String,
        /// The release notes in HTML format, transformed from Markdown when
        /// packaging the release. This may be an empty string.
        pub NotesHtml: String,
    }
}

/// Holds information about the current version and pending updates, such as
/// how many there are, and access to release notes.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct UpdateInfo {
    /// The available version that we are updating to.
    pub TargetFullRelease: VelopackAsset,
    /// The base release that this update is based on. This is only available
    /// if the update is a delta update.
    pub BaseRelease: Option<VelopackAsset>,
    /// The list of delta updates that can be applied to the base version to
    /// get to the target version.
    pub DeltasToTarget: Vec<VelopackAsset>,
    /// True if the update is a version downgrade or lateral move (such as when
    /// switching channels to the same version number). In this case, only full
    /// updates are allowed, and any local packages on disk newer than the
    /// downloaded version will be deleted.
    pub IsDowngrade: bool,
}

/// Options to customise the behaviour of UpdateManager.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct UpdateOptions {
    /// Allows UpdateManager to update to a version that's lower than the
    /// current version (i.e. downgrading). This could happen if a release has
    /// bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest
    /// version on that channel is lower than the current version.
    pub AllowVersionDowngrade: bool,
    /// **This option should usually be left None**.
    /// Overrides the default channel used to fetch updates. The default channel
    /// will be whatever channel was specified on the command line when building
    /// this release. This allows users to automatically receive updates from
    /// the same channel they installed from. This option allows you to
    /// explicitly switch channels.
    pub ExplicitChannel: Option<String>,
    /// Sets the maximum number of deltas to consider before falling back to a
    /// full update. The default is 10. Set to a negative number (eg. -1) to
    /// disable deltas.
    pub MaximumDeltasBeforeFallback: i32,
}

/// Represents the result of a call to check for updates.
pub enum UpdateCheck {
    /// The remote feed is empty, so no update check was performed.
    RemoteIsEmpty,
    /// The remote feed had releases, but none were newer or more relevant than
    /// the current version.
    NoUpdateAvailable,
    /// The remote feed had an update available.
    UpdateAvailable(UpdateInfo),
}

/// Configuration for locating an installed Velopack application.
/// Uses `String` instead of `PathBuf` since WASM targets use POSIX-style
/// paths and do not have native OS path semantics.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct VelopackLocatorConfig {
    /// The root directory of the current app.
    pub RootAppDir: String,
    /// The path to the Update.exe binary.
    pub UpdateExePath: String,
    /// The path to the packages' directory.
    pub PackagesDir: String,
    /// The path to the current app manifest file.
    pub ManifestPath: String,
    /// The directory containing the application's user binaries.
    pub CurrentBinaryDir: String,
    /// Whether the current application is portable or installed.
    pub IsPortable: bool,
}
