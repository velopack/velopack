use std::{
    path::Path,
    sync::{mpsc::Sender, Arc},
};

use crate::bundle::Manifest;
use crate::*;

mod file;
mod flow;
mod gitea;
mod github;
mod gitlab;
mod http;

pub use file::FileSource;
pub use flow::VelopackFlowSource;
pub use gitea::GiteaSource;
pub use github::GithubSource;
pub use gitlab::GitlabSource;
pub use http::HttpSource;

/// Abstraction for finding and downloading updates from a package source / repository.
/// An implementation may copy a file from a local repository, download from a web address,
/// or even use third party services and parse proprietary data to produce a package feed.
pub trait UpdateSource: Send + Sync {
    /// Retrieve the list of available remote releases from the package source. These releases
    /// can subsequently be downloaded with download_release_entry.
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error>;
    /// Download the specified VelopackAsset to the provided local file path.
    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error>;
}

/// A source that does not provide any update capability.
#[derive(Clone)]
pub struct NoneSource {}

impl UpdateSource for NoneSource {
    fn get_release_feed(&self, _channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        Err(Error::NotSupported("None source does not checking release feed".to_owned()))
    }
    fn download_release_entry(&self, _asset: &VelopackAsset, _local_file: &Path, _progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        Err(Error::NotSupported("None source does not support downloads".to_owned()))
    }
}

/// Automatically delegates to the appropriate source based on the provided input string. If the input is a local path,
/// it will use a FileSource. If the input is a URL, it will detect GitHub, GitLab, or Gitea by domain and use the
/// appropriate source, otherwise it will use an HttpSource.
pub struct AutoSource {
    source: Arc<dyn UpdateSource>,
}

impl Clone for AutoSource {
    fn clone(&self) -> Self {
        AutoSource {
            source: Arc::clone(&self.source),
        }
    }
}

impl AutoSource {
    /// Create a new AutoSource with the specified input string.
    pub fn new(input: &str) -> AutoSource {
        let source: Arc<dyn UpdateSource> = if Self::is_http_url(input) {
            if let Ok(url) = url::Url::parse(input) {
                if let Some(host) = url.host_str() {
                    if host.eq_ignore_ascii_case("github.com") {
                        Arc::new(GithubSource::new(input, None, false))
                    } else if host.eq_ignore_ascii_case("gitlab.com") {
                        Arc::new(GitlabSource::new(input, None, false))
                    } else if host.eq_ignore_ascii_case("gitea.com") {
                        Arc::new(GiteaSource::new(input, None, false))
                    } else {
                        Arc::new(HttpSource::new(input))
                    }
                } else {
                    Arc::new(HttpSource::new(input))
                }
            } else {
                Arc::new(HttpSource::new(input))
            }
        } else {
            Arc::new(FileSource::new(input))
        };
        AutoSource { source }
    }

    fn is_http_url(url: &str) -> bool {
        match url::Url::parse(url) {
            Ok(url) => url.scheme().eq_ignore_ascii_case("http") || url.scheme().eq_ignore_ascii_case("https"),
            _ => false,
        }
    }
}

impl UpdateSource for AutoSource {
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        self.source.get_release_feed(channel, app, staged_user_id)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        self.source.download_release_entry(asset, local_file, progress_sender)
    }
}

// --- Shared helpers for git-based sources ---

/// Given a list of releases (already fetched and filtered by the specific source),
/// downloads the releases.{channel}.json asset from each release, parses it,
/// and merges all assets into a single VelopackAssetFeed.
fn get_git_release_feed<F>(channel: &str, headers: &[(&str, &str)], release_count: usize, get_asset_url: F) -> Result<VelopackAssetFeed, Error>
where
    F: Fn(usize, &str) -> Result<String, Error>,
{
    let releases_file_name = format!("releases.{}.json", channel);
    let mut all_assets: Vec<VelopackAsset> = Vec::new();

    for i in 0..release_count {
        let asset_url = match get_asset_url(i, &releases_file_name) {
            Ok(url) => url,
            Err(e) => {
                trace!("Skipping release {}: {}", i, e);
                continue;
            }
        };

        match download::download_url_as_string_with_headers(&asset_url, headers) {
            Ok(json) => match serde_json::from_str::<VelopackAssetFeed>(&json) {
                Ok(feed) => {
                    all_assets.extend(feed.Assets);
                }
                Err(e) => {
                    trace!("Failed to parse release feed from release {}: {}", i, e);
                }
            },
            Err(e) => {
                trace!("Failed to download release feed from release {}: {}", i, e);
            }
        }
    }

    Ok(VelopackAssetFeed { Assets: all_assets })
}

/// Downloads an asset file from a git release.
fn download_git_release_entry(
    asset_url: &str,
    headers: &[(&str, &str)],
    local_file: &Path,
    progress_sender: Option<Sender<i16>>,
) -> Result<(), Error> {
    info!("About to download from URL '{}' to file '{:?}'", asset_url, local_file);
    download::download_url_to_file_with_headers(asset_url, local_file, headers, move |p| {
        if let Some(progress_sender) = &progress_sender {
            let _ = progress_sender.send(p);
        }
    })?;
    Ok(())
}
