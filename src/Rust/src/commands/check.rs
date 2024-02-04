use crate::{bundle::Manifest, shared};
use anyhow::{bail, Result};
use semver::Version;
use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf};

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct VelopackAssetFeed {
    pub Assets: Vec<VelopackAsset>,
}

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct VelopackAsset {
    pub PackageId: String,
    pub Version: String,
    pub Type: String,
    pub FileName: String,
    pub SHA1: String,
    pub Size: u64,
    pub NotesMarkdown: String,
    pub NotesHtml: String,
}

#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct UpdateInfo {
    pub TargetFullRelease: VelopackAsset,
    pub IsDowngrade: bool,
}

pub fn check(app: &Manifest, path: &str, allow_downgrade: bool, channel: Option<&str>) -> Result<Option<UpdateInfo>> {
    let result = if shared::is_http_url(&path) {
        info!("Checking for updates from URL: {}", path);
        check_url(app, path, allow_downgrade, channel)
    } else {
        let buf = PathBuf::from(&path);
        info!("Checking for updates from Local Path: {}", buf.to_string_lossy());
        if !buf.exists() {
            bail!("Path must be a valid HTTP Url or a path to an existing directory: {}", path);
        }
        check_dir(app, buf, allow_downgrade, channel)
    };
    result
}

fn get_default_channel() -> String {
    #[cfg(target_os = "windows")]
    return "win".to_owned();
    #[cfg(target_os = "linux")]
    return "linux".to_owned();
    #[cfg(target_os = "macos")]
    return "osx".to_owned();
}

fn check_url(app: &Manifest, path: &str, allow_downgrade: bool, channel: Option<&str>) -> Result<Option<UpdateInfo>> {
    let mut channel = channel.unwrap_or(&app.channel).to_string();
    if channel.is_empty() {
        channel = get_default_channel();
    }

    let non_default_channel = channel != app.channel;
    let releases_name = format!("releases.{}.json", channel);

    let path = path.trim_end_matches('/').to_owned() + "/";
    let url = url::Url::parse(&path)?;
    let mut releases_url = url.join(&releases_name)?;
    releases_url.set_query(Some(format!("localVersion={}&id={}", app.version, app.id).as_str()));

    info!("Downloading releases for channel {} from: {}", channel, releases_url.to_string());

    let json = shared::download::download_url_as_string(releases_url.as_str())?;
    let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
    process_feed(app, feed, allow_downgrade, non_default_channel)
}

fn check_dir(app: &Manifest, path: PathBuf, allow_downgrade: bool, channel: Option<&str>) -> Result<Option<UpdateInfo>> {
    let mut channel = channel.unwrap_or(&app.channel).to_string();
    if channel.is_empty() {
        channel = get_default_channel();
    }

    let non_default_channel = channel != app.channel;
    let releases_name = format!("releases.{}.json", channel);
    let releases_path = path.join(&releases_name);

    info!("Reading releases file for channel {} from: {}", channel, releases_path.to_string_lossy());

    if !releases_path.exists() {
        bail!("Could not find releases file: {}", path.to_string_lossy());
    }

    let json = fs::read_to_string(&releases_path)?;
    let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
    process_feed(app, feed, allow_downgrade, non_default_channel)
}

fn process_feed(app: &Manifest, feed: VelopackAssetFeed, allow_downgrade: bool, is_non_default_channel: bool) -> Result<Option<UpdateInfo>> {
    let assets = feed.Assets;

    if assets.is_empty() {
        bail!("Zero assets found in releases feed.");
    }

    let mut latest: Option<VelopackAsset> = None;
    let mut latest_version: Version = Version::parse("0.0.0")?;
    for asset in assets {
        if let Ok(sv) = Version::parse(&asset.Version) {
            debug!("Found asset: {} ({}).", asset.FileName, sv.to_string());
            if latest.is_none() || (sv > latest_version && asset.Type.eq_ignore_ascii_case("Full")) {
                latest = Some(asset);
                latest_version = sv;
            }
        }
    }

    if latest.is_none() {
        bail!("No valid full releases found in feed.");
    }

    let remote_version = latest_version;
    let remote_asset = latest.unwrap();

    debug!("Latest remote release: {} ({}).", remote_asset.FileName, remote_version.to_string());

    let mut result: Option<UpdateInfo> = None;

    if remote_version > app.version {
        info!("Found newer remote release available ({} -> {}).", app.version, remote_version);
        result = Some(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: false });
    } else if remote_version < app.version && allow_downgrade {
        info!("Found older remote release available and downgrade is enabled ({} -> {}).", app.version, remote_version);
        result = Some(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: true });
    } else if remote_version == app.version && allow_downgrade && is_non_default_channel {
        info!("Latest remote release is the same version of a different channel, and downgrade is enabled ({} -> {}).", app.version, remote_version);
        result = Some(UpdateInfo { TargetFullRelease: remote_asset, IsDowngrade: true });
    } else {
        info!("No update available.");
    }

    Ok(result)
}
