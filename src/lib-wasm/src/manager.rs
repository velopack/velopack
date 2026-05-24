#![allow(non_snake_case)]

use semver::Version;
use std::path::{Path, PathBuf};

use crate::{
    bundle::Manifest,
    constants,
    errors::Error,
    locator::{self, VelopackLocator},
    misc,
    sources::{AutoSource, UpdateSource},
    types::*,
};

/// Provides functionality for checking for updates, downloading updates,
/// and applying updates to the current application.
///
/// This is the WASM variant: single-threaded, no `Arc`, all async methods
/// where I/O is involved, and `String` paths instead of `PathBuf`.
pub struct UpdateManager {
    options: UpdateOptions,
    source: AutoSource,
    locator: VelopackLocator,
}

impl UpdateManager {
    /// Create a new `UpdateManager` instance.
    ///
    /// In the WASM environment a `VelopackLocatorConfig` is always required
    /// because there is no auto-detection of the install location.
    pub fn new(
        source: AutoSource,
        options: Option<UpdateOptions>,
        locator: Option<VelopackLocatorConfig>,
    ) -> Result<UpdateManager, Error> {
        let locator = match locator {
            Some(config) => VelopackLocator::new(&config)?,
            None => {
                return Err(Error::NotInstalled(
                    "No locator config provided (required in WASM)".into(),
                ))
            }
        };
        let mut options = options.unwrap_or_default();
        if options.MaximumDeltasBeforeFallback == 0 {
            options.MaximumDeltasBeforeFallback = 10;
        }
        Ok(UpdateManager { options, source, locator })
    }

    fn get_practical_channel(&self) -> String {
        let options_channel = self.options.ExplicitChannel.as_deref();
        let app_channel = self.locator.get_manifest_channel();
        let mut channel = options_channel.unwrap_or(&app_channel).to_string();
        if channel.is_empty() {
            channel = constants::default_channel_name();
        }
        channel
    }

    /// The currently installed app version as a string.
    pub fn get_current_version_as_string(&self) -> String {
        self.locator.get_manifest_version_full_string()
    }

    /// The currently installed app version as a semver `Version`.
    pub fn get_current_version(&self) -> Version {
        self.locator.get_manifest_version()
    }

    /// The currently installed app id.
    pub fn get_app_id(&self) -> String {
        self.locator.get_manifest_id()
    }

    /// Whether the current installation is portable.
    pub fn get_is_portable(&self) -> bool {
        self.locator.get_is_portable()
    }

    /// Returns a reference to the underlying locator.
    pub(crate) fn get_locator(&self) -> &VelopackLocator {
        &self.locator
    }

    /// Returns `None` if there is no local package waiting to be applied.
    /// Returns a `VelopackAsset` if there is an update downloaded which has
    /// not yet been applied.
    pub fn get_update_pending_restart(&self) -> Option<VelopackAsset> {
        let packages_dir = self.locator.get_packages_dir();
        if let Some((path, manifest)) = locator::find_latest_full_package(&packages_dir) {
            if manifest.version > self.locator.get_manifest_version() {
                return Some(local_path_to_asset(&manifest, &path));
            }
        }
        None
    }

    /// Checks for updates, returning an `UpdateCheck` that indicates whether
    /// an update is available, the remote is empty, or no update is needed.
    pub async fn check_for_updates(&self) -> Result<UpdateCheck, Error> {
        let allow_downgrade = self.options.AllowVersionDowngrade;
        let app_channel = self.locator.get_manifest_channel();
        let app_version = self.locator.get_manifest_version();

        let channel = self.get_practical_channel();
        let staged_user_id = self.locator.get_staged_user_id();
        let feed = self
            .source
            .get_release_feed(&channel, self.locator.get_manifest(), &staged_user_id)
            .await?;
        let assets = feed.Assets;

        let is_non_default_channel = channel != app_channel;

        if assets.is_empty() {
            return Ok(UpdateCheck::RemoteIsEmpty);
        }

        let mut latest: Option<&VelopackAsset> = None;
        let mut latest_version = Version::parse("0.0.0")?;
        for asset in &assets {
            if let Ok(sv) = Version::parse(&asset.Version) {
                if asset.Type.eq_ignore_ascii_case("Full") {
                    if latest.is_none() || sv > latest_version {
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

        if remote_version > app_version {
            Ok(UpdateCheck::UpdateAvailable(
                self.create_delta_update_strategy(&assets, (remote_asset, remote_version)),
            ))
        } else if remote_version < app_version && allow_downgrade {
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo::new_full(
                remote_asset.clone(),
                true,
            )))
        } else if remote_version == app_version && allow_downgrade && is_non_default_channel {
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo::new_full(
                remote_asset.clone(),
                true,
            )))
        } else {
            Ok(UpdateCheck::NoUpdateAvailable)
        }
    }

    fn create_delta_update_strategy(
        &self,
        feed: &[VelopackAsset],
        latest_remote: (&VelopackAsset, Version),
    ) -> UpdateInfo {
        let packages_dir = self.locator.get_packages_dir();
        let latest_local = locator::find_latest_full_package(&packages_dir);

        if latest_local.is_none() {
            return UpdateInfo::new_full(latest_remote.0.clone(), false);
        }

        let (latest_local_path, latest_local_manifest) = latest_local.unwrap();
        let local_asset = local_path_to_asset(&latest_local_manifest, &latest_local_path);

        let assets_and_versions: Vec<(&VelopackAsset, Version)> = feed
            .iter()
            .filter_map(|asset| {
                Version::parse(&asset.Version)
                    .ok()
                    .map(|ver| (asset, ver))
            })
            .collect();

        let matching_latest_delta = assets_and_versions
            .iter()
            .find(|(asset, version)| {
                asset.Type.eq_ignore_ascii_case("Delta") && version == &latest_remote.1
            });

        if matching_latest_delta.is_none() {
            return UpdateInfo::new_full(latest_remote.0.clone(), false);
        }

        let mut remotes_greater_than_local: Vec<_> = assets_and_versions
            .iter()
            .filter(|(asset, _)| asset.Type.eq_ignore_ascii_case("Delta"))
            .filter(|(_, version)| {
                version > &latest_local_manifest.version && version <= &latest_remote.1
            })
            .collect();

        remotes_greater_than_local.sort_by(|a, b| a.1.cmp(&b.1));
        let deltas = remotes_greater_than_local
            .iter()
            .map(|(asset, _)| (*asset).clone())
            .collect();

        UpdateInfo::new_delta(latest_remote.0.clone(), local_asset, deltas)
    }

    /// Downloads the specified updates to the local app packages directory.
    /// Progress is reported back to the caller via the callback (0-100).
    ///
    /// If the update contains delta packages this method will attempt to
    /// download and apply them. If there is an error, it will fall back to
    /// downloading the full package.
    pub async fn download_updates(
        &self,
        update: &UpdateInfo,
        progress: &dyn Fn(i16),
    ) -> Result<(), Error> {
        let name = &update.TargetFullRelease.FileName;
        let packages_dir = self.locator.get_packages_dir();

        std::fs::create_dir_all(&packages_dir)
            .map_err(|e| Error::Other(format!("create_dir_all({}) failed: {}", packages_dir, e)))?;
        let final_target = PathBuf::from(&packages_dir).join(name);
        let partial_file = final_target.with_extension("partial");

        if final_target.exists() {
            return Ok(());
        }

        // Collect old packages and partials for cleanup
        let mut to_delete = Vec::new();
        collect_files_with_suffix(&packages_dir, ".nupkg", &mut to_delete);
        collect_files_with_suffix(&packages_dir, ".partial", &mut to_delete);

        // Download - try deltas first if available, otherwise go straight to full
        if update.BaseRelease.is_some() && !update.DeltasToTarget.is_empty() {
            if self
                .download_and_apply_delta_updates(update, &partial_file, progress)
                .await
                .is_err()
            {
                let partial_str = partial_file.to_string_lossy().to_string();
                self.source
                    .download_release_entry(
                        &update.TargetFullRelease,
                        &partial_str,
                        progress,
                    )
                    .await?;
                self.verify_package_checksum(&partial_file, &update.TargetFullRelease)?;
            }
        } else {
            let partial_str = partial_file.to_string_lossy().to_string();
            self.source
                .download_release_entry(
                    &update.TargetFullRelease,
                    &partial_str,
                    progress,
                )
                .await
                .map_err(|e| Error::Other(format!("download_release_entry failed: {}", e)))?;
            self.verify_package_checksum(&partial_file, &update.TargetFullRelease)
                .map_err(|e| Error::Other(format!("verify_checksum failed: {}", e)))?;
        }

        wasi_rename(&partial_file, &final_target)
            .map_err(|e| Error::Other(format!("rename {:?} -> {:?} failed: {}", partial_file, final_target, e)))?;

        // Also clean up delta packages
        collect_files_with_suffix(&packages_dir, "-delta.nupkg", &mut to_delete);

        for path in to_delete {
            let _ = std::fs::remove_file(&path);
        }

        Ok(())
    }

    async fn download_and_apply_delta_updates(
        &self,
        update: &UpdateInfo,
        output_file: &Path,
        progress: &dyn Fn(i16),
    ) -> Result<(), Error> {
        let packages_dir = self.locator.get_packages_dir();
        let base_release = update.BaseRelease.as_ref().unwrap();
        let base_path = PathBuf::from(&packages_dir).join(&base_release.FileName);

        let mut patch_args: Vec<String> = vec![
            "patch".into(),
            "--old".into(),
            base_path.to_string_lossy().to_string(),
            "--output".into(),
            output_file.to_string_lossy().to_string(),
        ];

        for (i, delta) in update.DeltasToTarget.iter().enumerate() {
            let delta_file = PathBuf::from(&packages_dir).join(&delta.FileName);
            let partial_file = delta_file.with_extension("partial");
            let partial_str = partial_file.to_string_lossy().to_string();

            self.source
                .download_release_entry(delta, &partial_str, &|_| {})
                .await?;
            self.verify_package_checksum(&partial_file, delta)?;
            wasi_rename(&partial_file, &delta_file)?;

            let pct = ((i as f64 / update.DeltasToTarget.len() as f64) * 70.0) as i16;
            progress(pct);

            patch_args.push("--delta".into());
            patch_args.push(delta_file.to_string_lossy().to_string());
        }

        progress(70);

        // Call the update binary to apply patches via host-process
        let update_path = self.locator.get_update_path();
        launch_host_process(&update_path, &patch_args, None)?;

        progress(100);
        Ok(())
    }

    fn verify_package_checksum(
        &self,
        file: &Path,
        asset: &VelopackAsset,
    ) -> Result<(), Error> {
        let file_size = std::fs::File::open(file)
            .and_then(|f| f.metadata())
            .map(|m| m.len())
            .map_err(|e| Error::Other(format!("Failed to get file size for {:?}: {}", file, e)))?;
        if file_size != asset.Size {
            return Err(Error::SizeInvalid(
                file.to_path_buf(),
                asset.Size,
                file_size,
            ));
        }

        let (sha1, sha256) = misc::calculate_sha1_sha256(file)?;
        if !asset.SHA256.is_empty() {
            if !sha256.eq_ignore_ascii_case(&asset.SHA256) {
                return Err(Error::ChecksumInvalid(
                    file.to_path_buf(),
                    asset.SHA256.clone(),
                    sha256,
                ));
            }
        } else if !sha1.eq_ignore_ascii_case(&asset.SHA1) {
            return Err(Error::ChecksumInvalid(
                file.to_path_buf(),
                asset.SHA1.clone(),
                sha1,
            ));
        }
        Ok(())
    }

    /// Launches the update binary and tells it to wait for this process to
    /// exit before applying the specified update. The caller should exit the
    /// application after calling this method.
    pub fn wait_exit_then_apply_updates(
        &self,
        to_apply: &VelopackAsset,
        silent: bool,
        restart: bool,
        restart_args: Vec<String>,
    ) -> Result<(), Error> {
        let pkg_path =
            PathBuf::from(&self.locator.get_packages_dir()).join(&to_apply.FileName);
        if !pkg_path.exists() {
            return Err(Error::FileNotFound(pkg_path));
        }

        let mut args = vec![
            "apply".to_string(),
            "--package".to_string(),
            pkg_path.to_string_lossy().to_string(),
            "--waitPid".to_string(),
            std::process::id().to_string(),
        ];

        if silent {
            args.push("--silent".to_string());
        }
        if !restart {
            args.push("--norestart".to_string());
        }

        args.push("--root".to_string());
        args.push(self.locator.get_root_dir());

        if !restart_args.is_empty() {
            args.push("--".to_string());
            args.extend(restart_args);
        }

        let update_path = self.locator.get_update_path();
        launch_host_process(&update_path, &args, None)?;
        Ok(())
    }
}

// ---------------------------------------------------------------------------
// Helper: UpdateInfo constructors
// ---------------------------------------------------------------------------

impl UpdateInfo {
    /// Create an `UpdateInfo` for a full (non-delta) update.
    pub fn new_full(target: VelopackAsset, is_downgrade: bool) -> UpdateInfo {
        UpdateInfo {
            TargetFullRelease: target,
            BaseRelease: None,
            DeltasToTarget: Vec::new(),
            IsDowngrade: is_downgrade,
        }
    }

    /// Create an `UpdateInfo` for a delta update.
    pub fn new_delta(
        target: VelopackAsset,
        base: VelopackAsset,
        deltas: Vec<VelopackAsset>,
    ) -> UpdateInfo {
        UpdateInfo {
            TargetFullRelease: target,
            BaseRelease: Some(base),
            DeltasToTarget: deltas,
            IsDowngrade: false,
        }
    }
}

// ---------------------------------------------------------------------------
// Helper: launch host process via WIT import
// ---------------------------------------------------------------------------

/// Calls the host-provided process launching function (WIT import
/// `host-process.launch-detached`).
///
/// The concrete WIT binding path will be wired up in `lib.rs` once the
/// generated module structure is finalised. For now this is a thin wrapper
/// that maps the WIT error into `crate::errors::Error`.
fn launch_host_process(
    path: &str,
    args: &[String],
    work_dir: Option<&str>,
) -> Result<(), Error> {
    crate::velopack::core::host_process::launch_detached(path, args, work_dir)
        .map_err(|e| Error::Other(format!("Failed to launch process: {}", e)))
}

// ---------------------------------------------------------------------------
// Helper: collect files matching a suffix in a directory
// ---------------------------------------------------------------------------

fn collect_files_with_suffix(dir: &str, suffix: &str, out: &mut Vec<PathBuf>) {
    if let Ok(entries) = std::fs::read_dir(dir) {
        for entry in entries.flatten() {
            let path = entry.path();
            if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                if name.to_lowercase().ends_with(suffix) {
                    out.push(path);
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Helper: convert local manifest + path into a VelopackAsset
// ---------------------------------------------------------------------------

/// Converts a local manifest and path string into a `VelopackAsset`.
pub fn local_path_to_asset(manifest: &Manifest, path: &str) -> VelopackAsset {
    let file_name = Path::new(path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_default();
    let size = std::fs::metadata(path).map(|m| m.len()).unwrap_or(0);
    VelopackAsset {
        PackageId: manifest.id.clone(),
        Version: manifest.version.to_string(),
        Type: "Full".to_string(),
        FileName: file_name,
        SHA1: String::new(),
        SHA256: String::new(),
        Size: size,
        NotesMarkdown: manifest.release_notes.clone(),
        NotesHtml: manifest.release_notes_html.clone(),
    }
}

fn wasi_rename(from: &Path, to: &Path) -> Result<(), std::io::Error> {
    match std::fs::rename(from, to) {
        Ok(()) => Ok(()),
        Err(_) => {
            let data = std::fs::read(from)?;
            std::fs::write(to, &data)?;
            let _ = std::fs::remove_file(from);
            Ok(())
        }
    }
}
