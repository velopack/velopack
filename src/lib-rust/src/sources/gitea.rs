use std::{path::Path, sync::mpsc::Sender};

use serde::Deserialize;

use crate::bundle::Manifest;
use crate::*;

use super::{download_git_release_entry, get_git_release_feed, UpdateSource};

#[derive(Deserialize)]
struct GiteaRelease {
    name: Option<String>,
    prerelease: bool,
    published_at: Option<String>,
    assets: Vec<GiteaReleaseAsset>,
}

#[derive(Deserialize)]
struct GiteaReleaseAsset {
    browser_download_url: Option<String>,
    name: Option<String>,
}

/// Retrieves available releases from a Gitea repository.
#[derive(Clone)]
pub struct GiteaSource {
    repo_url: url::Url,
    access_token: Option<String>,
    prerelease: bool,
}

impl GiteaSource {
    /// Create a new GiteaSource.
    /// - `repo_url`: The URL of the Gitea repository (e.g. "https://gitea.com/myuser/myrepo")
    /// - `access_token`: Optional Gitea access token (sent as `Authorization: token {token}`).
    /// - `prerelease`: If true, pre-releases will also be searched/downloaded.
    pub fn new(repo_url: &str, access_token: Option<String>, prerelease: bool) -> GiteaSource {
        let url = url::Url::parse(repo_url.trim_end_matches('/')).expect("Invalid Gitea repository URL");
        GiteaSource {
            repo_url: url,
            access_token,
            prerelease,
        }
    }

    fn get_api_base_url(&self) -> String {
        let scheme = self.repo_url.scheme();
        let host = self.repo_url.host_str().unwrap_or("gitea.com");
        match self.repo_url.port() {
            Some(port) => format!("{}://{}:{}/api/v1/", scheme, host, port),
            None => format!("{}://{}/api/v1/", scheme, host),
        }
    }

    fn get_http_options(&self, accept: &str) -> HttpOptions {
        let mut headers = vec![HttpHeader {
            Name: "Accept".to_string(),
            Value: accept.to_string(),
        }];
        if let Some(ref token) = self.access_token {
            headers.push(HttpHeader {
                Name: "Authorization".to_string(),
                Value: format!("token {}", token),
            });
        }
        HttpOptions {
            Headers: headers,
            ..Default::default()
        }
    }

    fn get_releases(&self) -> Result<Vec<GiteaRelease>, Error> {
        let base = self.get_api_base_url();
        let path = self.repo_url.path();
        let url = format!("{}repos{}/releases?limit=10&page=1&draft=false", base, path);
        let options = self.get_http_options("application/json");
        let json = download::download_url_as_string(&url, Some(&options))?;
        let mut releases: Vec<GiteaRelease> = serde_json::from_str(&json)?;
        releases.sort_by(|a, b| b.published_at.cmp(&a.published_at));
        if !self.prerelease {
            releases.retain(|r| !r.prerelease);
        }
        Ok(releases)
    }

    fn get_asset_url_from_name(&self, release: &GiteaRelease, asset_name: &str) -> Result<String, Error> {
        let release_name = release.name.as_deref().unwrap_or("unknown");
        if release.assets.is_empty() {
            return Err(Error::Other(format!("No assets found in Gitea Release '{}'.", release_name)));
        }

        let asset = release
            .assets
            .iter()
            .find(|a| a.name.as_deref().map(|n| n.eq_ignore_ascii_case(asset_name)).unwrap_or(false))
            .ok_or_else(|| {
                Error::Other(format!(
                    "Could not find asset called '{}' in Gitea Release '{}'.",
                    asset_name, release_name
                ))
            })?;

        if let Some(ref url) = asset.browser_download_url {
            Ok(url.clone())
        } else {
            Err(Error::Other("Could not find a valid asset url for the specified asset.".to_string()))
        }
    }
}

impl UpdateSource for GiteaSource {
    fn get_release_feed(&self, channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases = self.get_releases()?;
        let options = self.get_http_options("application/octet-stream");
        get_git_release_feed(channel, &options, releases.len(), |i, name| {
            self.get_asset_url_from_name(&releases[i], name)
        })
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let releases = self.get_releases()?;
        let options = self.get_http_options("application/octet-stream");
        for release in &releases {
            if let Ok(url) = self.get_asset_url_from_name(release, &asset.FileName) {
                return download_git_release_entry(&url, &options, local_file, progress_sender);
            }
        }
        Err(Error::Other(format!("Could not find asset '{}' in any Gitea release.", asset.FileName)))
    }
}
