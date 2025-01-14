use serde::{Deserialize, Serialize};

use super::{bundle, download, Error, UpdateSource, VelopackAsset, VelopackAssetFeed};
use std::sync::mpsc::Sender;

#[derive(Debug, Serialize, Deserialize)]
pub struct GithubRelease {
    /// The name of this release.
    #[serde(rename = "name")]
    pub name: Option<String>,

    /// True if this release is a prerelease.
    #[serde(rename = "prerelease")]
    pub prerelease: bool,

    /// The date which this release was published publicly.
    ///
    /// We use `chrono` with an RFC 3339 parser. If your JSON has a date/time
    /// in RFC 3339 format (e.g., `"2021-01-01T12:34:56Z"`), then this works.
    /// Otherwise, you can remove the `with` attribute and store it as a string.
    #[serde(rename = "published_at", with = "chrono::serde::ts_rfc3339_option")]
    pub published_at: Option<DateTime<Utc>>,

    /// A list of assets (files) uploaded to this release.
    #[serde(rename = "assets")]
    pub assets: Vec<GithubReleaseAsset>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct GithubReleaseAsset {
    /// The asset URL for this release asset.
    #[serde(rename = "url")]
    pub url: Option<String>,

    /// The browser URL for this release asset.
    #[serde(rename = "browser_download_url")]
    pub browser_download_url: Option<String>,

    /// The name of this release asset.
    #[serde(rename = "name")]
    pub name: Option<String>,

    /// The mime type of this release asset.
    #[serde(rename = "content_type")]
    pub content_type: Option<String>,
}
