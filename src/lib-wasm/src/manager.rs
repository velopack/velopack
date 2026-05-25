#![allow(non_snake_case)]

use semver::Version;

use crate::{
    bundle::Manifest,
    constants,
    download::DownloadResult,
    errors::Error,
    host_fs,
    locator::{self, VelopackLocator},
    sources::{AutoSource, UpdateSource},
    types::*,
};

pub struct UpdateManager {
    options: UpdateOptions,
    source: AutoSource,
    locator: VelopackLocator,
}

impl UpdateManager {
    pub fn new(source: AutoSource, options: Option<UpdateOptions>, locator: Option<VelopackLocatorConfig>) -> Result<UpdateManager, Error> {
        let locator = match locator {
            Some(config) => VelopackLocator::new(&config)?,
            None => return Err(Error::NotInstalled("No locator config provided (required in WASM)".into())),
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

    pub fn get_current_version_as_string(&self) -> String {
        self.locator.get_manifest_version_full_string()
    }

    pub fn get_current_version(&self) -> Version {
        self.locator.get_manifest_version()
    }

    pub fn get_app_id(&self) -> String {
        self.locator.get_manifest_id()
    }

    pub fn get_is_portable(&self) -> bool {
        self.locator.get_is_portable()
    }

    pub(crate) fn get_locator(&self) -> &VelopackLocator {
        &self.locator
    }

    pub fn get_update_pending_restart(&self) -> Option<VelopackAsset> {
        let packages_dir = self.locator.get_packages_dir();
        if let Some((path, manifest)) = locator::find_latest_full_package(&packages_dir) {
            if manifest.version > self.locator.get_manifest_version() {
                return Some(local_path_to_asset(&manifest, &path));
            }
        }
        None
    }

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
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo::new_full(remote_asset.clone(), true)))
        } else if remote_version == app_version && allow_downgrade && is_non_default_channel {
            Ok(UpdateCheck::UpdateAvailable(UpdateInfo::new_full(remote_asset.clone(), true)))
        } else {
            Ok(UpdateCheck::NoUpdateAvailable)
        }
    }

    fn create_delta_update_strategy(&self, feed: &[VelopackAsset], latest_remote: (&VelopackAsset, Version)) -> UpdateInfo {
        let packages_dir = self.locator.get_packages_dir();
        let latest_local = locator::find_latest_full_package(&packages_dir);

        if latest_local.is_none() {
            return UpdateInfo::new_full(latest_remote.0.clone(), false);
        }

        let (latest_local_path, latest_local_manifest) = latest_local.unwrap();
        let local_asset = local_path_to_asset(&latest_local_manifest, &latest_local_path);

        let assets_and_versions: Vec<(&VelopackAsset, Version)> = feed
            .iter()
            .filter_map(|asset| Version::parse(&asset.Version).ok().map(|ver| (asset, ver)))
            .collect();

        let matching_latest_delta = assets_and_versions
            .iter()
            .find(|(asset, version)| asset.Type.eq_ignore_ascii_case("Delta") && version == &latest_remote.1);

        if matching_latest_delta.is_none() {
            return UpdateInfo::new_full(latest_remote.0.clone(), false);
        }

        let mut remotes_greater_than_local: Vec<_> = assets_and_versions
            .iter()
            .filter(|(asset, _)| asset.Type.eq_ignore_ascii_case("Delta"))
            .filter(|(_, version)| version > &latest_local_manifest.version && version <= &latest_remote.1)
            .collect();

        remotes_greater_than_local.sort_by(|a, b| a.1.cmp(&b.1));
        let deltas = remotes_greater_than_local.iter().map(|(asset, _)| (*asset).clone()).collect();

        UpdateInfo::new_delta(latest_remote.0.clone(), local_asset, deltas)
    }

    pub async fn download_updates(&self, update: &UpdateInfo, progress: &dyn Fn(i16)) -> Result<(), Error> {
        let name = &update.TargetFullRelease.FileName;
        validate_filename(name)?;
        for delta in &update.DeltasToTarget {
            validate_filename(&delta.FileName)?;
        }
        let packages_dir = self.locator.get_packages_dir();

        let final_target = format!("{}/{}", packages_dir, name);
        let partial_file = format!("{}/{}.partial", packages_dir, name);

        if host_fs::file_exists(&final_target) {
            return Ok(());
        }

        let mut to_delete = Vec::new();
        collect_files_with_suffix(&packages_dir, ".nupkg", &mut to_delete);
        collect_files_with_suffix(&packages_dir, ".partial", &mut to_delete);

        if update.BaseRelease.is_some() && !update.DeltasToTarget.is_empty() {
            if self.download_and_apply_delta_updates(update, &partial_file, progress).await.is_err() {
                let result = self
                    .source
                    .download_release_entry(&update.TargetFullRelease, &partial_file, progress)
                    .await?;
                verify_download(&partial_file, &update.TargetFullRelease, &result)?;
            }
        } else {
            let result = self
                .source
                .download_release_entry(&update.TargetFullRelease, &partial_file, progress)
                .await
                .map_err(|e| Error::Other(format!("download_release_entry failed: {}", e)))?;
            verify_download(&partial_file, &update.TargetFullRelease, &result).map_err(|e| Error::Other(format!("verify_checksum failed: {}", e)))?;
        }

        host_fs::rename_file(&partial_file, &final_target)
            .map_err(|e| Error::Other(format!("rename {} -> {} failed: {}", partial_file, final_target, e)))?;

        collect_files_with_suffix(&packages_dir, "-delta.nupkg", &mut to_delete);

        for path in to_delete {
            let _ = host_fs::delete_file(&path);
        }

        Ok(())
    }

    async fn download_and_apply_delta_updates(&self, update: &UpdateInfo, output_file: &str, progress: &dyn Fn(i16)) -> Result<(), Error> {
        let packages_dir = self.locator.get_packages_dir();
        let base_release = update.BaseRelease.as_ref().unwrap();
        let base_path = format!("{}/{}", packages_dir, base_release.FileName);

        let mut patch_args: Vec<String> = vec!["patch".into(), "--old".into(), base_path, "--output".into(), output_file.to_string()];

        for (i, delta) in update.DeltasToTarget.iter().enumerate() {
            let delta_file = format!("{}/{}", packages_dir, delta.FileName);
            let partial_file = format!("{}.partial", delta_file);

            let result = self.source.download_release_entry(delta, &partial_file, &|_| {}).await?;
            verify_download(&partial_file, delta, &result)?;
            host_fs::rename_file(&partial_file, &delta_file)?;

            let pct = ((i as f64 / update.DeltasToTarget.len() as f64) * 70.0) as i16;
            progress(pct);

            patch_args.push("--delta".into());
            patch_args.push(delta_file);
        }

        progress(70);

        let update_path = self.locator.get_update_path();
        launch_host_process(&update_path, &patch_args, None)?;

        progress(100);
        Ok(())
    }

    pub fn wait_exit_then_apply_updates(
        &self,
        to_apply: &VelopackAsset,
        silent: bool,
        restart: bool,
        restart_args: Vec<String>,
    ) -> Result<(), Error> {
        let pkg_path = format!("{}/{}", self.locator.get_packages_dir(), to_apply.FileName);
        if !host_fs::file_exists(&pkg_path) {
            return Err(Error::FileNotFound(pkg_path));
        }

        let mut args = vec![
            "apply".to_string(),
            "--package".to_string(),
            pkg_path,
            "--waitPid".to_string(),
            crate::velopack::core::host_process::get_process_id().to_string(),
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

impl UpdateInfo {
    pub fn new_full(target: VelopackAsset, is_downgrade: bool) -> UpdateInfo {
        UpdateInfo {
            TargetFullRelease: target,
            BaseRelease: None,
            DeltasToTarget: Vec::new(),
            IsDowngrade: is_downgrade,
        }
    }

    pub fn new_delta(target: VelopackAsset, base: VelopackAsset, deltas: Vec<VelopackAsset>) -> UpdateInfo {
        UpdateInfo {
            TargetFullRelease: target,
            BaseRelease: Some(base),
            DeltasToTarget: deltas,
            IsDowngrade: false,
        }
    }
}

fn verify_download(file_path: &str, asset: &VelopackAsset, result: &DownloadResult) -> Result<(), Error> {
    if result.size != asset.Size {
        return Err(Error::SizeInvalid(file_path.to_string(), asset.Size, result.size));
    }
    if !asset.SHA256.is_empty() {
        if !result.sha256.eq_ignore_ascii_case(&asset.SHA256) {
            return Err(Error::ChecksumInvalid(file_path.to_string(), asset.SHA256.clone(), result.sha256.clone()));
        }
    } else if !asset.SHA1.is_empty() && !result.sha1.eq_ignore_ascii_case(&asset.SHA1) {
        return Err(Error::ChecksumInvalid(file_path.to_string(), asset.SHA1.clone(), result.sha1.clone()));
    }
    Ok(())
}

fn launch_host_process(path: &str, args: &[String], work_dir: Option<&str>) -> Result<(), Error> {
    crate::velopack::core::host_process::launch_detached(path, args, work_dir).map_err(|e| Error::Other(format!("Failed to launch process: {}", e)))
}

fn validate_filename(name: &str) -> Result<(), Error> {
    if name.contains("..") || name.starts_with('/') || name.starts_with('\\') || name.contains(':') {
        return Err(Error::Other(format!("Invalid filename in update feed: {}", name)));
    }
    Ok(())
}

fn collect_files_with_suffix(dir: &str, suffix: &str, out: &mut Vec<String>) {
    if let Ok(entries) = host_fs::list_dir(dir) {
        for name in entries {
            if name.to_lowercase().ends_with(suffix) {
                out.push(format!("{}/{}", dir, name));
            }
        }
    }
}

pub fn local_path_to_asset(manifest: &Manifest, path: &str) -> VelopackAsset {
    let file_name = path.rsplit('/').next().unwrap_or(path).to_string();
    let size = host_fs::get_file_size(path).unwrap_or(Some(0)).unwrap_or(0);
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
