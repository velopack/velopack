use semver::Version;
use serde::{Deserialize, Serialize};
use std::ffi::{OsStr, OsString};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::{fs, process::exit, sync::mpsc::Sender};

use crate::{
    bundle::Manifest,
    chunks::{self, staging, ChunkIndex, RangeReader, StagedLayout, StagedReadyMarker},
    constants,
    locator::{self, LocationContext, VelopackLocator, VelopackLocatorConfig},
    misc,
    sources::{UpdateSource, VelopackFlowSource},
    Error,
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
    /// Finds a release by name and returns a reference to the VelopackAsset in the feed, or None if not found.
    pub fn find(&self, release_name: &str) -> Option<&VelopackAsset> {
        self.Assets.iter().find(|x| x.FileName.eq_ignore_ascii_case(release_name))
    }
}

define_struct_case_insensitive! {
    /// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
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
        /// The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
        pub NotesMarkdown: String,
        /// The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
        pub NotesHtml: String,
        /// The byte offset of the chunk index trailer inside the package, or 0 if the package has no chunk index.
        pub ChunkIndexOffset: u64,
        /// The byte length of the chunk index trailer inside the package, or 0 if the package has no chunk index.
        pub ChunkIndexLength: u64,
        /// The SHA256 checksum (uppercase hex) of the chunk index trailer, or an empty string if the package has no chunk index.
        pub ChunkIndexSHA256: String,
    }
}

/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct UpdateInfo {
    /// The available version that we are updating to.
    pub TargetFullRelease: VelopackAsset,
    /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
    /// In this case, any local packages on disk newer than the downloaded version will be deleted.
    pub IsDowngrade: bool,
}

impl UpdateInfo {
    pub(crate) fn new_full(target: VelopackAsset, is_downgrade: bool) -> UpdateInfo {
        UpdateInfo {
            TargetFullRelease: target,
            IsDowngrade: is_downgrade,
        }
    }
}

impl AsRef<VelopackAsset> for UpdateInfo {
    fn as_ref(&self) -> &VelopackAsset {
        &self.TargetFullRelease
    }
}

impl AsRef<VelopackAsset> for VelopackAsset {
    fn as_ref(&self) -> &VelopackAsset {
        self
    }
}

/// Options to customise the behaviour of UpdateManager.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct UpdateOptions {
    /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
    /// This could happen if a release has bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest version on that
    /// channel is lower than the current version.
    pub AllowVersionDowngrade: bool,
    /// **This option should usually be left None**.
    /// Overrides the default channel used to fetch updates.
    /// The default channel will be whatever channel was specified on the command line when building this release.
    /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
    /// This allows users to automatically receive updates from the same channel they installed from. This options
    /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
    /// without having to reinstall the application.
    pub ExplicitChannel: Option<String>,
    /// Disables chunk-based updates, so the full update package is always downloaded.
    pub DisableChunkedUpdates: bool,
}

struct UpdateManagerInner {
    options: UpdateOptions,
    source: Box<dyn UpdateSource>,
    locator: VelopackLocator,
}

/// Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
#[derive(Clone)]
pub struct UpdateManager {
    inner: Arc<UpdateManagerInner>,
}

/// Represents the result of a call to check for updates.
pub enum UpdateCheck {
    /// The remote feed is empty, so no update check was performed
    RemoteIsEmpty,
    /// The remote feed had releases, but none were newer or more relevant than the current version
    NoUpdateAvailable,
    /// The remote feed had an update available
    UpdateAvailable(Box<UpdateInfo>),
}

impl UpdateManager {
    /// Create a new UpdateManager instance using a `VelopackFlowSource`, which retrieves
    /// updates from the hosted Velopack Flow service (https://velopack.io).
    /// This is a convenience function which is equivalent to creating a `VelopackFlowSource`
    /// and passing it to `UpdateManager::new`.
    /// This will return an error if the application is not yet installed.
    /// ## Example:
    /// ```rust
    /// use velopack::*;
    ///
    /// let um = UpdateManager::new_flow(None, None);
    /// ```
    pub fn new_flow(options: Option<UpdateOptions>, locator: Option<VelopackLocatorConfig>) -> Result<UpdateManager, Error> {
        UpdateManager::new(VelopackFlowSource::new(None), options, locator)
    }

    /// Create a new UpdateManager instance using the specified UpdateSource.
    /// This will return an error if the application is not yet installed.
    /// ## Example:
    /// ```rust
    /// use velopack::*;
    ///
    /// let source = sources::HttpSource::new("https://the.place/you-host/updates");
    /// let um = UpdateManager::new(source, None, None);
    /// ```
    pub fn new<T: UpdateSource + 'static>(
        source: T,
        options: Option<UpdateOptions>,
        locator: Option<VelopackLocatorConfig>,
    ) -> Result<UpdateManager, Error> {
        UpdateManager::new_boxed(Box::new(source), options, locator)
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
            VelopackLocator::new(&config)?
        } else {
            locator::auto_locate_app_manifest(LocationContext::FromCurrentExe)?
        };
        let options = options.unwrap_or_default();
        Ok(UpdateManager {
            inner: Arc::new(UpdateManagerInner { options, source, locator }),
        })
    }

    fn get_practical_channel(&self) -> String {
        let options_channel = self.inner.options.ExplicitChannel.as_deref();
        let app_channel = self.inner.locator.get_manifest_channel();
        let mut channel = options_channel.unwrap_or(&app_channel).to_string();
        if channel.is_empty() {
            warn!("Channel is empty, using default.");
            channel = constants::DEFAULT_CHANNEL_NAME.to_owned();
        }
        info!(
            "Chosen channel for updates: {:?} (explicit={:?}, memorized={:?})",
            channel, options_channel, app_channel
        );
        channel
    }

    /// Returns a reference to the current VelopackLocator.
    pub(crate) fn get_locator(&self) -> &VelopackLocator {
        &self.inner.locator
    }

    /// The currently installed app version as a string.
    pub fn get_current_version_as_string(&self) -> String {
        self.inner.locator.get_manifest_version_full_string()
    }

    /// The currently installed app version as a semver Version.
    pub fn get_current_version(&self) -> Version {
        self.inner.locator.get_manifest_version()
    }

    /// The currently installed app id.
    pub fn get_app_id(&self) -> String {
        self.inner.locator.get_manifest_id()
    }

    /// The release notes (markdown) of the currently installed version, as provided to `vpk pack --releaseNotes`.
    /// Returns None if the release was packed without release notes.
    pub fn get_current_release_notes_markdown(&self) -> Option<String> {
        self.inner.locator.get_manifest_release_notes()
    }

    /// The release notes (rendered HTML) of the currently installed version, as provided to `vpk pack --releaseNotes`.
    /// Returns None if the release was packed without release notes.
    pub fn get_current_release_notes_html(&self) -> Option<String> {
        self.inner.locator.get_manifest_release_notes_html()
    }

    /// Check if the app is in portable mode. This can be true or false on Windows.
    /// On Linux and MacOS, this will always return true.
    pub fn get_is_portable(&self) -> bool {
        self.inner.locator.get_is_portable()
    }

    /// Returns None if there is no local package waiting to be applied. Returns a VelopackAsset
    /// if there is an update downloaded which has not yet been applied. In that case, the
    /// VelopackAsset can be applied by calling apply_updates_and_restart or wait_exit_then_apply_updates.
    pub fn get_update_pending_restart(&self) -> Option<VelopackAsset> {
        let packages_dir = self.inner.locator.get_packages_dir();
        find_pending_update(&packages_dir, &self.inner.locator.get_manifest_version()).map(|(_, asset)| asset)
    }

    /// Get a list of available remote releases from the package source.
    pub fn get_release_feed(&self) -> Result<VelopackAssetFeed, Error> {
        let channel = self.get_practical_channel();
        let staged_user_id = self.inner.locator.get_staged_user_id();
        self.inner
            .source
            .get_release_feed(&channel, &self.inner.locator.get_manifest(), staged_user_id.as_str())
    }

    /// Checks for updates, returning None if there are none available. If there are updates available, this method will return an
    /// UpdateInfo object containing the latest available release.
    pub fn check_for_updates(&self) -> Result<UpdateCheck, Error> {
        let allow_downgrade = self.inner.options.AllowVersionDowngrade;
        let app_channel = self.inner.locator.get_manifest_channel();
        let app_version = self.inner.locator.get_manifest_version();
        let feed = self.get_release_feed()?;
        let assets = feed.Assets;

        let practical_channel = self.get_practical_channel();
        let is_non_default_channel = practical_channel != app_channel;

        if assets.is_empty() {
            return Ok(UpdateCheck::RemoteIsEmpty);
        }

        let mut latest: Option<&VelopackAsset> = None;
        let mut latest_version: Version = Version::parse("0.0.0")?;
        for asset in &assets {
            if let Ok(sv) = Version::parse(&asset.Version) {
                if asset.Type.eq_ignore_ascii_case("Full") {
                    debug!("Found full release: {} ({}).", asset.FileName, sv);
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

        debug!("Latest remote release: {} ({}).", remote_asset.FileName, remote_version);

        if remote_version > app_version {
            info!("Found newer remote release available ({} -> {}).", app_version, remote_version);
            Ok(UpdateCheck::UpdateAvailable(Box::new(UpdateInfo::new_full(remote_asset.clone(), false))))
        } else if remote_version < app_version && allow_downgrade {
            info!(
                "Found older remote release available and downgrade is enabled ({} -> {}).",
                app_version, remote_version
            );
            Ok(UpdateCheck::UpdateAvailable(Box::new(UpdateInfo::new_full(remote_asset.clone(), true))))
        } else if remote_version == app_version && allow_downgrade && is_non_default_channel {
            info!(
                "Latest remote release is the same version of a different channel, and downgrade is enabled ({} -> {}, {} -> {}).",
                app_version, remote_version, app_channel, practical_channel
            );
            Ok(UpdateCheck::UpdateAvailable(Box::new(UpdateInfo::new_full(remote_asset.clone(), true))))
        } else {
            Ok(UpdateCheck::NoUpdateAvailable)
        }
    }

    /// Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
    /// This function will acquire a global update lock so may fail if there is already another update operation in progress.
    ///
    /// On Windows, when the installed app and the target package both carry a chunk index, only the parts of the
    /// package that are not already installed are fetched (by HTTP range requests) and the new version is assembled
    /// into a staged directory, ready to be swapped in by `apply_updates_and_restart`. Any failure of that path falls
    /// back to downloading the full package. Reported progress never decreases within one call.
    pub fn download_updates(&self, update: &UpdateInfo, progress: Option<Sender<i16>>) -> Result<(), Error> {
        let _mutex = &self.inner.locator.try_get_exclusive_lock()?;
        let packages_dir = &self.inner.locator.get_packages_dir();
        fs::create_dir_all(packages_dir)?;
        let target = &update.TargetFullRelease;
        let report = MonotonicProgress::new(progress);

        if let Some((dir, _)) = chunks::find_ready_staged_for(packages_dir, target) {
            info!("Staged update is already assembled, skipping download: '{:?}'", dir);
            report.send(100);
            return Ok(());
        }

        let final_target_file = local_package_path(packages_dir, target);
        if final_target_file.exists() {
            info!("Package already exists on disk, skipping download: '{:?}'", final_target_file);
            report.send(100);
            return Ok(());
        }

        if let Some((local, reader)) = self.chunk_path_eligibility(target) {
            match self.download_chunked(target, local, reader.as_ref(), &report) {
                Ok(()) => return Ok(()),
                Err(ChunkPathError::Cancelled) => return Err(Error::Other("The update was cancelled".to_string())),
                Err(e) => {
                    warn!("Chunk update failed, falling back to a full download: {}", e);
                    if let Some(id) = chunks::staged_id(&target.ChunkIndexSHA256).filter(|_| e.deletes_staged_dir()) {
                        let dir = StagedLayout::new(packages_dir, &id).dir;
                        if dir.exists() {
                            if let Err(e) = misc::retry_io(|| fs::remove_dir_all(&dir)) {
                                warn!("Failed to delete staged directory {:?}: {}", dir, e);
                            }
                        }
                    }
                }
            }
        }

        self.download_full(target, &final_target_file, &report)?;
        chunks::gc_staged(packages_dir, None);
        Ok(())
    }

    /// The classic path: download the whole package, verify it, extract the new updater (Windows) and delete
    /// older packages. Progress is mapped onto whatever is left of the 0–100 band.
    fn download_full(&self, target: &VelopackAsset, final_target_file: &Path, report: &MonotonicProgress) -> Result<(), Error> {
        let packages_dir = &self.inner.locator.get_packages_dir();
        let partial_file = final_target_file.with_extension("partial");

        let old_nupkg_pattern = format!("{}/*.nupkg", packages_dir.to_string_lossy());
        let old_partial_pattern = format!("{}/*.partial", packages_dir.to_string_lossy());
        let mut to_delete = Vec::new();
        find_files_to_delete(&old_nupkg_pattern, &mut to_delete);
        find_files_to_delete(&old_partial_pattern, &mut to_delete);

        let band_start = report.last().max(0);
        let (sender, receiver) = std::sync::mpsc::channel::<i16>();
        let forwarder = std::thread::spawn({
            let report = report.clone();
            move || {
                for p in receiver {
                    report.send_band(band_start, 100, p);
                }
            }
        });
        let result = self.inner.source.download_release_entry(target, &partial_file, Some(sender));
        let _ = forwarder.join();
        result?;
        self.verify_package_checksum(&partial_file, target)?;
        info!("Successfully downloaded file: '{:?}'", partial_file);

        info!("Renaming partial file to final target: '{:?}'", final_target_file);
        fs::rename(&partial_file, final_target_file)?;

        // extract new Update.exe on Windows only
        #[cfg(target_os = "windows")]
        match crate::bundle::load_bundle_from_file(final_target_file) {
            Ok(bundle) => {
                info!("Bundle loaded successfully.");
                let update_exe_path = self.inner.locator.get_update_path();
                if let Err(e) = bundle.extract_zip_predicate_to_path(|f| f.ends_with("Squirrel.exe"), update_exe_path) {
                    error!("Error extracting Update.exe from bundle: {}", e);
                }
            }
            Err(e) => {
                error!("Error loading bundle: {}", e);
            }
        }

        for path in to_delete {
            info!("Deleting up old package: '{:?}'", path);
            let _ = fs::remove_file(&path);
        }
        report.send(100);
        Ok(())
    }

    /// Evaluates the chunk path eligibility rules (§5.1). Returns the parsed local content map and the range
    /// reader when every rule holds, or None (with the reason logged) when the full package must be downloaded.
    fn chunk_path_eligibility(&self, target: &VelopackAsset) -> Option<(ChunkIndex, Box<dyn RangeReader>)> {
        if !cfg!(windows) {
            debug!("Chunk updates are only supported on Windows, downloading the full package.");
            return None;
        }
        if self.inner.options.DisableChunkedUpdates {
            info!("Chunk updates are disabled by UpdateOptions, downloading the full package.");
            return None;
        }
        if !asset_has_chunk_index(target) {
            info!("The target package has no chunk index, downloading the full package.");
            return None;
        }
        let local_path = self.inner.locator.get_current_bin_dir().join(chunks::LOCAL_INDEX_NAME);
        let local = match fs::read(&local_path) {
            Ok(bytes) => match ChunkIndex::parse_local(&bytes) {
                Ok(index) => index,
                Err(e) => {
                    info!(
                        "The installed content map {:?} is invalid ({}), downloading the full package.",
                        local_path, e
                    );
                    return None;
                }
            },
            Err(e) => {
                info!("No installed content map at {:?} ({}), downloading the full package.", local_path, e);
                return None;
            }
        };
        match self.inner.source.open_range_reader(target) {
            Ok(Some(reader)) => Some((local, reader)),
            Ok(None) => {
                info!("The update source does not support ranged downloads, downloading the full package.");
                None
            }
            Err(e) => {
                warn!("Failed to open ranged access to the package ({}), downloading the full package.", e);
                None
            }
        }
    }

    /// The chunk path (§8.4): fetch the index, plan, fetch the needed ranges, assemble in-process (with up to two
    /// extra rounds for chunks the local files turned out not to provide) and copy the new updater sidecar.
    fn download_chunked(
        &self,
        target: &VelopackAsset,
        local: ChunkIndex,
        reader: &dyn RangeReader,
        report: &MonotonicProgress,
    ) -> Result<(), ChunkPathError> {
        let locator = &self.inner.locator;
        let packages_dir = locator.get_packages_dir();
        let id = chunks::staged_id(&target.ChunkIndexSHA256)
            .ok_or_else(|| ChunkPathError::Invalid("the feed's ChunkIndexSHA256 is not a SHA-256".to_string()))?;
        info!("Starting chunk update of {} to staged id {}", target.FileName, id);

        chunks::gc_staged(&packages_dir, Some(&id));
        let layout = StagedLayout::new(&packages_dir, &id);
        fs::create_dir_all(&layout.dir)?;
        staging::write_asset_json(&layout, target).map_err(|e| ChunkPathError::Fatal(format!("failed to write asset.json: {}", e)))?;

        let policy = chunks::RetryPolicy::new();
        let index_bytes = match fs::read(layout.index_bin()) {
            Ok(bytes) if chunks::sha256_hex(&chunks::sha256(&bytes)).eq_ignore_ascii_case(&target.ChunkIndexSHA256) => {
                info!("Reusing the previously fetched chunk index.");
                bytes
            }
            _ => {
                let bytes = chunks::fetch_index(reader, target, &policy, &|done, total| {
                    report.send_band(0, chunks::PROGRESS_INDEX_END, percent(done, total));
                })?;
                staging::write_file_atomic(&layout.index_bin(), &bytes, true)?;
                bytes
            }
        };
        report.send(chunks::PROGRESS_INDEX_END);
        let remote = ChunkIndex::parse_trailer(&index_bytes).map_err(|e| ChunkPathError::Invalid(e.to_string()))?;
        remote
            .validate_asset_size(target.Size)
            .map_err(|e| ChunkPathError::Invalid(e.to_string()))?;

        let roots = chunks::LocalRoots {
            current_dir: locator.get_current_bin_dir(),
            updater_path: locator.get_update_path(),
            root_dir: root_dir_of(locator),
        };
        let blob = chunks::ChunkBlob::open(layout.chunks_bin())?;
        let plan = chunks::plan(&remote, Some(&local), &roots, &|h| blob.contains(h));
        info!(
            "Chunk plan: {} chunks ({} bytes compressed, {} bytes plaintext) of a {} byte package; {} chunks already staged",
            plan.needed.len(),
            plan.bytes_needed,
            plan.plaintext_needed,
            target.Size,
            blob.len()
        );
        if chunks::exceeds_transport_ceiling(&plan, target.Size) {
            return Err(ChunkPathError::Fatal(format!(
                "the plan needs {} of {} bytes, a full download is cheaper",
                plan.bytes_needed, target.Size
            )));
        }
        let required = (plan.bytes_total + plan.plaintext_needed) as f64 * chunks::FREE_SPACE_FACTOR + chunks::FREE_SPACE_MARGIN as f64;
        if let Some(free) = free_space_bytes(&packages_dir) {
            if (free as f64) < required {
                return Err(ChunkPathError::Fatal(format!(
                    "not enough free space for a staged update ({} bytes free, {} required)",
                    free, required as u64
                )));
            }
        }

        let requests = chunks::coalesce(&plan);
        let blob = Mutex::new(blob);
        let fetch_progress = |done: u64, total: u64| {
            report.send_band(chunks::PROGRESS_INDEX_END, chunks::PROGRESS_FETCH_END, percent(done, total));
        };
        chunks::fetch_ranges(reader, &requests, target.Size, &blob, &policy, &fetch_progress, false)?;
        report.send(chunks::PROGRESS_FETCH_END);

        let ctx = chunks::AssembleContext {
            staged_dir: layout.dir.clone(),
            current_dir: roots.current_dir.clone(),
            updater_local_source: roots.updater_path.clone(),
            root_dir: roots.root_dir.clone(),
        };
        let assemble_progress = |p: i16| report.send_band(chunks::PROGRESS_FETCH_END, chunks::PROGRESS_ASSEMBLE_END, p);
        let mut rounds = 0u32;
        loop {
            match chunks::assemble(&ctx, &assemble_progress) {
                Ok(()) => break,
                Err(chunks::AssembleError::Missing(n)) => {
                    rounds += 1;
                    if rounds > chunks::MAX_MISSING_ROUNDS {
                        return Err(ChunkPathError::Fatal(format!(
                            "assembly still needs {} chunks after {} extra rounds",
                            n,
                            rounds - 1
                        )));
                    }
                    let hashes = staging::read_missing_bin(&layout.missing_bin())?;
                    let missing_plan = chunks::plan_for_hashes(&remote, &hashes);
                    if missing_plan.needed.len() != hashes.len() {
                        return Err(ChunkPathError::Invalid("assembly reported chunks the index does not contain".to_string()));
                    }
                    info!("Assembly needs {} more chunks, fetching them (round {})", hashes.len(), rounds);
                    let requests = chunks::coalesce(&missing_plan);
                    chunks::fetch_ranges(reader, &requests, target.Size, &blob, &policy, &|_, _| {}, true)?;
                    let _ = fs::remove_file(layout.missing_bin());
                }
                Err(chunks::AssembleError::Invalid(m)) => return Err(ChunkPathError::Invalid(m)),
                Err(chunks::AssembleError::Io(e)) => return Err(ChunkPathError::Io(e)),
                Err(chunks::AssembleError::Other(e)) => return Err(ChunkPathError::Fatal(e.to_string())),
            }
        }
        report.send(chunks::PROGRESS_ASSEMBLE_END);

        // Best-effort: the mandatory copy happens right before apply-staged is launched.
        let sidecar = layout.updater();
        let update_exe = locator.get_update_path();
        let same = match (chunks::sha256_file(&sidecar), chunks::sha256_file(&update_exe)) {
            (Ok(a), Ok(b)) => a == b,
            _ => false,
        };
        if !same {
            match misc::retry_io(|| fs::copy(&sidecar, &update_exe)) {
                Ok(_) => info!("Copied the new updater to {:?}", update_exe),
                Err(e) => warn!("Failed to copy the new updater to {:?}: {}", update_exe, e),
            }
        }

        let mut to_delete = Vec::new();
        find_files_to_delete(&format!("{}/*.nupkg", packages_dir.to_string_lossy()), &mut to_delete);
        find_files_to_delete(&format!("{}/*.partial", packages_dir.to_string_lossy()), &mut to_delete);
        for path in to_delete {
            info!("Deleting old package: '{:?}'", path);
            let _ = fs::remove_file(&path);
        }
        info!("Chunk update staged successfully in {:?}", layout.dir);
        report.send(100);
        Ok(())
    }

    fn verify_package_checksum(&self, file: &Path, asset: &VelopackAsset) -> Result<(), Error> {
        let file_size = file.metadata()?.len();
        if file_size != asset.Size {
            error!("File size mismatch for file '{:?}': expected {}, got {}", file, asset.Size, file_size);
            return Err(Error::SizeInvalid(file.to_path_buf(), asset.Size, file_size));
        }

        // SHA1 is required and should always be present in the remote feed.
        // SHA256 is optional and only supported by newer clients, so if it's
        // missing from the remote asset we fall back to verifying SHA1 only.
        let (sha1, sha256) = misc::calculate_sha1_sha256(file)?;
        if !asset.SHA256.is_empty() {
            if !sha256.eq_ignore_ascii_case(&asset.SHA256) {
                error!(
                    "SHA256 checksum mismatch for file '{:?}': expected '{}', got '{}'",
                    file, asset.SHA256, sha256
                );
                return Err(Error::ChecksumInvalid(file.to_path_buf(), asset.SHA256.clone(), sha256));
            }
        } else if !sha1.eq_ignore_ascii_case(&asset.SHA1) {
            error!("SHA1 checksum mismatch for file '{:?}': expected '{}', got '{}'", file, asset.SHA1, sha1);
            return Err(Error::ChecksumInvalid(file.to_path_buf(), asset.SHA1.clone(), sha1));
        }
        Ok(())
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
        S: AsRef<OsStr>,
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
        S: AsRef<OsStr>,
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
        S: AsRef<OsStr>,
        C: IntoIterator<Item = S>,
    {
        let to_apply = to_apply.as_ref();
        let packages_dir = self.inner.locator.get_packages_dir();

        let mut args: Vec<OsString> = Vec::new();
        if let Some((staged_dir, marker)) = chunks::find_ready_staged_for(&packages_dir, to_apply) {
            info!("Applying staged update from '{:?}'", staged_dir);
            self.prepare_staged_updater(&staged_dir, &marker)?;
            args.push("apply-staged".into());
            args.push("--staged".into());
            args.push(staged_dir.into());
        } else {
            let pkg_path = local_package_path(&packages_dir, to_apply);
            if !pkg_path.exists() {
                error!("Package does not exist on disk: '{:?}'", pkg_path);
                return Err(Error::FileNotFound(pkg_path));
            }
            args.push("apply".into());
            args.push("--package".into());
            args.push(pkg_path.into());
        }

        match wait_mode {
            ApplyWaitMode::NoWait => {}
            ApplyWaitMode::WaitCurrentProcess => {
                args.push("--waitPid".into());
                args.push(format!("{}", std::process::id()).into());
            }
            ApplyWaitMode::WaitPid(pid) => {
                args.push("--waitPid".into());
                args.push(format!("{}", pid).into());
            }
        }

        if silent {
            args.push("--silent".into());
        }

        if !restart {
            args.push("--norestart".into());
        }

        args.push("--root".into());
        #[cfg(target_os = "linux")]
        args.push(self.inner.locator.get_appimage_path().into());
        #[cfg(not(target_os = "linux"))]
        args.push(self.inner.locator.get_root_dir().into());
        args.push("--packageDir".into());
        args.push(self.inner.locator.get_packages_dir().into());

        let restart_args: Vec<OsString> = restart_args.into_iter().map(|item| item.as_ref().to_os_string()).collect();
        if !restart_args.is_empty() {
            args.push("--".into());
            for arg in restart_args {
                args.push(arg);
            }
        }

        let update_path = self.inner.locator.get_update_path();
        crate::process::run_process(&update_path, args, update_path.parent(), false, None)?;
        Ok(())
    }

    /// `apply-staged` only ever runs through the target's own updater: verifies the staged sidecar against the
    /// marker and copies it over the current updater when they differ (mandatory, a failure is an error).
    fn prepare_staged_updater(&self, staged_dir: &Path, marker: &StagedReadyMarker) -> Result<(), Error> {
        let sidecar = StagedLayout::from_dir(staged_dir).updater();
        let sidecar_sha = chunks::sha256_hex(&chunks::sha256_file(&sidecar)?);
        if !sidecar_sha.eq_ignore_ascii_case(&marker.UpdaterSHA256) {
            error!("The staged updater {:?} does not match ready.json, deleting the staged update.", sidecar);
            let _ = fs::remove_dir_all(staged_dir);
            return Err(Error::InvalidPackage("the staged updater does not match its ready marker".to_string()));
        }
        let update_exe = self.inner.locator.get_update_path();
        let current_sha = chunks::sha256_file(&update_exe).map(|h| chunks::sha256_hex(&h)).unwrap_or_default();
        if !current_sha.eq_ignore_ascii_case(&sidecar_sha) {
            info!("Copying the staged updater over {:?}", update_exe);
            misc::retry_io(|| fs::copy(&sidecar, &update_exe))?;
        }
        Ok(())
    }
}

/// Why the chunk path gave up; decides whether the staged directory survives for a later resume.
#[derive(Debug, thiserror::Error)]
enum ChunkPathError {
    /// The index or the staged tree is invalid: the staged directory is deleted.
    #[error("invalid chunk update: {0}")]
    Invalid(String),
    /// Disk full, access denied, readback mismatch: the staged directory is deleted to free the space.
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    /// A network budget, an unsupported range response or a cost ceiling: the staged directory is kept.
    #[error("{0}")]
    Fatal(String),
    /// The caller cancelled the update: the staged directory is kept.
    #[error("cancelled")]
    Cancelled,
}

impl ChunkPathError {
    fn deletes_staged_dir(&self) -> bool {
        matches!(self, ChunkPathError::Invalid(_) | ChunkPathError::Io(_))
    }
}

impl From<chunks::FetchError> for ChunkPathError {
    fn from(e: chunks::FetchError) -> Self {
        match e {
            chunks::FetchError::Fatal(m) => ChunkPathError::Fatal(m),
            chunks::FetchError::Cancelled => ChunkPathError::Cancelled,
        }
    }
}

/// Forwards progress to the caller's sender, reporting only integer increases so values never go backwards.
#[derive(Clone)]
struct MonotonicProgress {
    sender: Option<Sender<i16>>,
    last: Arc<Mutex<i16>>,
}

impl MonotonicProgress {
    fn new(sender: Option<Sender<i16>>) -> MonotonicProgress {
        MonotonicProgress {
            sender,
            last: Arc::new(Mutex::new(-1)),
        }
    }

    fn last(&self) -> i16 {
        *self.last.lock().unwrap()
    }

    fn send(&self, value: i16) {
        let value = value.clamp(0, 100);
        let mut last = self.last.lock().unwrap();
        if value > *last {
            *last = value;
            if let Some(sender) = &self.sender {
                let _ = sender.send(value);
            }
        }
    }

    /// Maps a 0–100 value onto `[start, end]`.
    fn send_band(&self, start: i16, end: i16, value: i16) {
        let value = value.clamp(0, 100) as i32;
        self.send(start + ((end - start) as i32 * value / 100) as i16);
    }
}

fn percent(done: u64, total: u64) -> i16 {
    if total == 0 {
        100
    } else {
        ((done.min(total) as u128 * 100) / total as u128) as i16
    }
}

fn find_files_to_delete(pattern: &str, to_delete: &mut Vec<PathBuf>) {
    match glob::glob(pattern) {
        Ok(paths) => {
            for path in paths.into_iter().flatten() {
                to_delete.push(path);
            }
        }
        Err(e) => {
            error!("Error while searching for packages to clean: {}", e);
        }
    }
}

/// Whether a feed asset points at a chunk index trailer inside bounds (§5.1 rule 3).
fn asset_has_chunk_index(asset: &VelopackAsset) -> bool {
    asset.ChunkIndexLength > 0
        && asset.ChunkIndexLength <= chunks::MAX_TRAILER_LEN
        && chunks::staged_id(&asset.ChunkIndexSHA256).is_some()
        && asset
            .ChunkIndexOffset
            .checked_add(asset.ChunkIndexLength)
            .map(|end| end <= asset.Size)
            .unwrap_or(false)
}

/// The root directory for chunk purposes (stub records live there).
fn root_dir_of(locator: &VelopackLocator) -> PathBuf {
    #[cfg(target_os = "linux")]
    {
        locator.get_appimage_path()
    }
    #[cfg(not(target_os = "linux"))]
    {
        locator.get_root_dir()
    }
}

/// The highest pending update above `current`: the ready staged tree (its asset carries the chunk index
/// pointer, so a later apply matches it by id) or the latest local full package. Returns the version and asset.
pub(crate) fn find_pending_update(packages_dir: &Path, current: &Version) -> Option<(Version, VelopackAsset)> {
    let mut best: Option<(Version, VelopackAsset)> = None;
    if let Some((_, marker)) = chunks::find_ready_staged(packages_dir) {
        if let Some(version) = marker.version() {
            if version > *current {
                best = Some((version, marker.Asset));
            }
        }
    }
    if let Some((path, manifest)) = locator::find_latest_full_package(packages_dir) {
        let newer = best.as_ref().map(|(v, _)| manifest.version > *v).unwrap_or(true);
        if manifest.version > *current && newer {
            best = Some((manifest.version.clone(), local_path_to_asset(&manifest, &path)));
        }
    }
    best
}

/// Free bytes available to the caller on the volume holding `path`; None when the query fails (treated as "enough").
fn free_space_bytes(path: &Path) -> Option<u64> {
    #[cfg(windows)]
    {
        use windows::Win32::Storage::FileSystem::GetDiskFreeSpaceExW;
        let wide = crate::wide_strings::string_to_wide(path.to_string_lossy().as_ref());
        let mut free: u64 = 0;
        match unsafe { GetDiskFreeSpaceExW(wide.as_pcwstr(), Some(&mut free), None, None) } {
            Ok(()) => Some(free),
            Err(e) => {
                debug!("Free space query failed for {:?}: {}", path, e);
                None
            }
        }
    }
    #[cfg(unix)]
    {
        use std::os::unix::ffi::OsStrExt;
        let c_path = std::ffi::CString::new(path.as_os_str().as_bytes()).ok()?;
        let mut stat: libc::statvfs = unsafe { std::mem::zeroed() };
        if unsafe { libc::statvfs(c_path.as_ptr(), &mut stat) } != 0 {
            debug!("Free space query failed for {:?}: {}", path, std::io::Error::last_os_error());
            return None;
        }
        Some(stat.f_bavail as u64 * stat.f_frsize as u64)
    }
}

/// Returns the path an asset is stored at in the packages directory. Only the (sanitised) file name part of the
/// asset's FileName is used, so feed entries in a sub-folder or at an absolute URL land directly in the packages
/// directory and can never escape it. Mirrors `GetLocalPackagePath` in the C# library, so both pick the same file.
fn local_package_path(packages_dir: &Path, asset: &VelopackAsset) -> PathBuf {
    packages_dir.join(misc::get_safe_filename(&asset.FileName))
}

/// Converts a local manifest and path into a VelopackAsset.
pub(crate) fn local_path_to_asset(manifest: &Manifest, path: &Path) -> VelopackAsset {
    VelopackAsset {
        PackageId: manifest.id.clone(),
        Version: manifest.version.to_string(),
        Type: "Full".to_string(),
        FileName: path.file_name().unwrap().to_string_lossy().to_string(),
        SHA1: String::new(),
        SHA256: String::new(),
        Size: path.metadata().map(|m| m.len()).unwrap_or(0),
        NotesMarkdown: manifest.release_notes.clone(),
        NotesHtml: manifest.release_notes_html.clone(),
        ..Default::default()
    }
}
