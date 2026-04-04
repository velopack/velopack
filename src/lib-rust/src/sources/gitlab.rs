use std::{path::Path, sync::mpsc::Sender};

use serde::Deserialize;

use crate::bundle::Manifest;
use crate::*;

use super::{download_git_release_entry, get_git_release_feed, UpdateSource};

#[derive(Deserialize)]
struct GitlabRelease {
    name: Option<String>,
    upcoming_release: bool,
    released_at: Option<String>,
    assets: Option<GitlabReleaseAssets>,
}

#[derive(Deserialize)]
struct GitlabReleaseAssets {
    count: usize,
    links: Vec<GitlabReleaseLink>,
}

#[derive(Deserialize)]
struct GitlabReleaseLink {
    name: Option<String>,
    url: Option<String>,
    direct_asset_url: Option<String>,
}

/// Retrieves available releases from a GitLab repository.
#[derive(Clone)]
pub struct GitlabSource {
    repo_url: url::Url,
    access_token: Option<String>,
    prerelease: bool,
}

impl GitlabSource {
    /// Create a new GitlabSource.
    /// - `repo_url`: The GitLab API URL (e.g. "https://gitlab.com/api/v4/projects/ProjectId")
    /// - `access_token`: Optional GitLab access token (sent as PRIVATE-TOKEN header).
    /// - `prerelease`: If true, upcoming/pre-releases will also be searched.
    pub fn new(repo_url: &str, access_token: Option<String>, prerelease: bool) -> GitlabSource {
        let url = url::Url::parse(repo_url.trim_end_matches('/')).expect("Invalid GitLab repository URL");
        GitlabSource {
            repo_url: url,
            access_token,
            prerelease,
        }
    }

    fn get_headers(&self, accept: &str) -> Vec<(String, String)> {
        let mut headers = vec![("Accept".to_string(), accept.to_string())];
        if let Some(ref token) = self.access_token {
            headers.push(("PRIVATE-TOKEN".to_string(), token.clone()));
        }
        headers
    }

    fn get_releases(&self) -> Result<Vec<GitlabRelease>, Error> {
        let base = self.repo_url.as_str().trim_end_matches('/');
        let url = format!("{}/releases?per_page=10&page=1", base);
        let headers = self.get_headers("application/json");
        let header_refs: Vec<(&str, &str)> = headers.iter().map(|(k, v)| (k.as_str(), v.as_str())).collect();
        let json = download::download_url_as_string_with_headers(&url, &header_refs)?;
        let mut releases: Vec<GitlabRelease> = serde_json::from_str(&json)?;
        releases.sort_by(|a, b| b.released_at.cmp(&a.released_at));
        if !self.prerelease {
            releases.retain(|r| !r.upcoming_release);
        }
        Ok(releases)
    }

    fn get_asset_url_from_name(&self, release: &GitlabRelease, asset_name: &str) -> Result<String, Error> {
        let release_name = release.name.as_deref().unwrap_or("unknown");
        let assets = release
            .assets
            .as_ref()
            .ok_or_else(|| Error::Other(format!("No assets found in GitLab Release '{}'.", release_name)))?;
        if assets.count == 0 {
            return Err(Error::Other(format!("No assets found in GitLab Release '{}'.", release_name)));
        }

        let link = assets
            .links
            .iter()
            .find(|l| l.name.as_deref().map(|n| n.eq_ignore_ascii_case(asset_name)).unwrap_or(false))
            .ok_or_else(|| {
                Error::Other(format!(
                    "Could not find asset called '{}' in GitLab Release '{}'.",
                    asset_name, release_name
                ))
            })?;

        if self.access_token.is_none() {
            if let Some(ref url) = link.direct_asset_url {
                return Ok(url.clone());
            }
        }
        if let Some(ref url) = link.url {
            return Ok(url.clone());
        }
        Err(Error::Other(format!(
            "Could not find a valid URL for asset '{}' in GitLab Release '{}'.",
            asset_name, release_name
        )))
    }
}

impl UpdateSource for GitlabSource {
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
        Err(Error::Other(format!("Could not find asset '{}' in any GitLab release.", asset.FileName)))
    }
}
