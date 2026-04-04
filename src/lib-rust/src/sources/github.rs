use std::{path::Path, sync::mpsc::Sender};

use serde::Deserialize;

use crate::bundle::Manifest;
use crate::*;

use super::{download_git_release_entry, get_git_release_feed, UpdateSource};

#[derive(Deserialize)]
struct GithubRelease {
    name: Option<String>,
    prerelease: bool,
    published_at: Option<String>,
    assets: Vec<GithubReleaseAsset>,
}

#[derive(Deserialize)]
struct GithubReleaseAsset {
    url: Option<String>,
    browser_download_url: Option<String>,
    name: Option<String>,
}

/// Retrieves available releases from a GitHub repository. Supports both github.com
/// and GitHub Enterprise instances.
#[derive(Clone)]
pub struct GithubSource {
    repo_url: url::Url,
    access_token: Option<String>,
    prerelease: bool,
}

impl GithubSource {
    /// Create a new GithubSource.
    /// - `repo_url`: The URL of the GitHub repository (e.g. "https://github.com/myuser/myrepo")
    /// - `access_token`: Optional GitHub access token. Without one, rate limited to 60 req/hr per IP.
    /// - `prerelease`: If true, pre-releases will also be searched/downloaded.
    pub fn new(repo_url: &str, access_token: Option<String>, prerelease: bool) -> GithubSource {
        let url = url::Url::parse(repo_url.trim_end_matches('/')).expect("Invalid GitHub repository URL");
        GithubSource {
            repo_url: url,
            access_token,
            prerelease,
        }
    }

    fn get_api_base_url(&self) -> String {
        let host = self.repo_url.host_str().unwrap_or("github.com");
        if host.eq_ignore_ascii_case("github.com") {
            "https://api.github.com/".to_string()
        } else {
            match self.repo_url.port() {
                Some(port) => format!("{}://{}:{}/api/v3/", self.repo_url.scheme(), host, port),
                None => format!("{}://{}/api/v3/", self.repo_url.scheme(), host),
            }
        }
    }

    fn get_headers(&self, accept: &str) -> Vec<(String, String)> {
        let mut headers = vec![("Accept".to_string(), accept.to_string())];
        if let Some(ref token) = self.access_token {
            headers.push(("Authorization".to_string(), format!("Bearer {}", token)));
        }
        headers
    }

    fn get_releases(&self) -> Result<Vec<GithubRelease>, Error> {
        let base = self.get_api_base_url();
        let path = self.repo_url.path();
        let url = format!("{}repos{}/releases?per_page=10&page=1", base, path);
        let headers = self.get_headers("application/vnd.github.v3+json");
        let header_refs: Vec<(&str, &str)> = headers.iter().map(|(k, v)| (k.as_str(), v.as_str())).collect();
        let json = download::download_url_as_string_with_headers(&url, &header_refs)?;
        let mut releases: Vec<GithubRelease> = serde_json::from_str(&json)?;
        releases.sort_by(|a, b| b.published_at.cmp(&a.published_at));
        if !self.prerelease {
            releases.retain(|r| !r.prerelease);
        }
        Ok(releases)
    }

    fn get_asset_url_from_name(&self, release: &GithubRelease, asset_name: &str) -> Result<String, Error> {
        let release_name = release.name.as_deref().unwrap_or("unknown");
        if release.assets.is_empty() {
            return Err(Error::Other(format!("No assets found in GitHub Release '{}'.", release_name)));
        }

        let asset = release
            .assets
            .iter()
            .find(|a| a.name.as_deref().map(|n| n.eq_ignore_ascii_case(asset_name)).unwrap_or(false))
            .ok_or_else(|| {
                Error::Other(format!(
                    "Could not find asset called '{}' in GitHub Release '{}'.",
                    asset_name, release_name
                ))
            })?;

        if self.access_token.is_none() {
            if let Some(ref url) = asset.browser_download_url {
                return Ok(url.clone());
            }
        }
        if let Some(ref url) = asset.url {
            return Ok(url.clone());
        }
        Err(Error::Other("Could not find a valid asset url for the specified asset.".to_string()))
    }
}

impl UpdateSource for GithubSource {
    fn get_release_feed(&self, channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases = self.get_releases()?;
        let headers = self.get_headers("application/octet-stream");
        let header_refs: Vec<(&str, &str)> = headers.iter().map(|(k, v)| (k.as_str(), v.as_str())).collect();
        get_git_release_feed(channel, &header_refs, releases.len(), |i, name| {
            self.get_asset_url_from_name(&releases[i], name)
        })
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let releases = self.get_releases()?;
        let headers = self.get_headers("application/octet-stream");
        let header_refs: Vec<(&str, &str)> = headers.iter().map(|(k, v)| (k.as_str(), v.as_str())).collect();
        for release in &releases {
            if let Ok(url) = self.get_asset_url_from_name(release, &asset.FileName) {
                return download_git_release_entry(&url, &header_refs, local_file, progress_sender);
            }
        }
        Err(Error::Other(format!("Could not find asset '{}' in any GitHub release.", asset.FileName)))
    }
}
