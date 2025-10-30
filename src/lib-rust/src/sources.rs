use std::{
    path::{Path, PathBuf},
    sync::mpsc::Sender,
};

use crate::bundle::Manifest;
use crate::*;

/// Abstraction for finding and downloading updates from a package source / repository.
/// An implementation may copy a file from a local repository, download from a web address,
/// or even use third party services and parse proprietary data to produce a package feed.
pub trait UpdateSource: Send + Sync {
    /// Retrieve the list of available remote releases from the package source. These releases
    /// can subsequently be downloaded with download_release_entry.
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error>;
    /// Download the specified VelopackAsset to the provided local file path.
    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error>;
    /// Clone the source to create a new lifetime.
    fn clone_boxed(&self) -> Box<dyn UpdateSource>;
}

impl Clone for Box<dyn UpdateSource> {
    fn clone(&self) -> Self {
        self.clone_boxed()
    }
}

/// A source that does not provide any update capability.
#[derive(Clone)]
pub struct NoneSource {}

impl UpdateSource for NoneSource {
    fn get_release_feed(&self, _channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        Err(Error::Generic("None source does not checking release feed".to_owned()))
    }
    fn download_release_entry(
        &self,
        _asset: &VelopackAsset,
        _local_file: &str,
        _progress_sender: Option<Sender<i16>>,
    ) -> Result<(), Error> {
        Err(Error::Generic("None source does not support downloads".to_owned()))
    }
    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        Box::new(self.clone())
    }
}

#[derive(Clone)]
/// Automatically delegates to the appropriate source based on the provided input string. If the input is a local path,
/// it will use a FileSource. If the input is a URL, it will use an HttpSource.
pub struct AutoSource {
    source: Box<dyn UpdateSource>,
}

impl AutoSource {
    /// Create a new AutoSource with the specified input string.
    pub fn new(input: &str) -> AutoSource {
        let source: Box<dyn UpdateSource> =
            if Self::is_http_url(input) { Box::new(HttpSource::new(input)) } else { Box::new(FileSource::new(input)) };
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

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        self.source.download_release_entry(asset, local_file, progress_sender)
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        self.source.clone_boxed()
    }
}

#[derive(Clone)]
/// Retrieves updates from a static file host or other web server.
/// Will perform a request for '{baseUri}/RELEASES' to locate the available packages,
/// and provides query parameters to specify the name of the requested package.
pub struct HttpSource {
    url: String,
}

impl HttpSource {
    /// Create a new HttpSource with the specified base URL.
    pub fn new<S: AsRef<str>>(url: S) -> HttpSource {
        HttpSource { url: url.as_ref().to_owned() }
    }
}

impl UpdateSource for HttpSource {
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);

        let path = self.url.trim_end_matches('/').to_owned() + "/";
        let url = url::Url::parse(&path)?;
        let mut releases_url = url.join(&releases_name)?;
        releases_url.set_query(Some(format!("localVersion={}&id={}&stagingId={}", app.version, app.id, staged_user_id).as_str()));

        info!("Downloading releases for channel {} from: {}", channel, releases_url.to_string());
        let json = download::download_url_as_string(releases_url.as_str())?;
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let path = self.url.trim_end_matches('/').to_owned() + "/";
        let url = url::Url::parse(&path)?;
        let asset_url = url.join(&asset.FileName)?;

        info!("About to download from URL '{}' to file '{}'", asset_url, local_file);
        download::download_url_to_file(asset_url.as_str(), local_file, move |p| {
            if let Some(progress_sender) = &progress_sender {
                let _ = progress_sender.send(p);
            }
        })?;
        Ok(())
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        Box::new(self.clone())
    }
}

#[derive(Clone)]
/// Retrieves available updates from a local or network-attached disk. The directory
/// must contain one or more valid packages, as well as a 'releases.{channel}.json' index file.
pub struct FileSource {
    path: PathBuf,
}

impl FileSource {
    /// Create a new FileSource with the specified base directory.
    pub fn new<P: AsRef<Path>>(path: P) -> FileSource {
        let path = path.as_ref();
        FileSource { path: PathBuf::from(path) }
    }
}

impl UpdateSource for FileSource {
    fn get_release_feed(&self, channel: &str, _: &bundle::Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let releases_path = self.path.join(&releases_name);

        info!("Reading releases from file: {}", releases_path.display());
        let json = std::fs::read_to_string(releases_path)?;
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let asset_path = self.path.join(&asset.FileName);
        info!("About to copy from file '{}' to file '{}'", asset_path.display(), local_file);
        if let Some(progress_sender) = &progress_sender {
            let _ = progress_sender.send(50);
        }
        std::fs::copy(asset_path, local_file)?;
        if let Some(progress_sender) = &progress_sender {
            let _ = progress_sender.send(100);
        }
        Ok(())
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        Box::new(self.clone())
    }
}

/// Retrieves available releases from a GitHub repository.
#[derive(Clone)]
pub struct GithubSource {
    /// The URL of the GitHub repository to download releases from
    /// (e.g. https://github.com/myuser/myrepo)
    repo_url: String,
    /// The GitHub access token to use with the request to download releases.
    /// If left empty, the GitHub rate limit for unauthenticated requests allows
    /// for up to 60 requests per hour, limited by IP address.
    access_token: Option<String>,
    /// If true, pre-releases will be also be searched / downloaded. If false, only
    /// stable releases will be considered.
    prerelease: bool,
}

#[derive(serde::Deserialize)]
struct GithubRelease {
    name: Option<String>,
    prerelease: bool,
    published_at: Option<String>,
    assets: Vec<GithubReleaseAsset>,
}

#[derive(serde::Deserialize)]
struct GithubReleaseAsset {
    url: Option<String>,
    browser_download_url: Option<String>,
    name: Option<String>,
}

impl GithubSource {
    /// Create a new GithubSource with the specified repository URL and optional access token.
    pub fn new(repo_url: &str, access_token: Option<String>, prerelease: bool) -> GithubSource {
        GithubSource { repo_url: repo_url.trim_end_matches('/').to_owned(), access_token, prerelease }
    }

    fn get_api_base_url(&self, repo_url: &url::Url) -> Result<url::Url, Error> {
        if repo_url.host_str().map(|h| h.ends_with("github.com")).unwrap_or(false) {
            Ok(url::Url::parse("https://api.github.com/")?)
        } else {
            // if it's not github.com, it's probably an Enterprise server
            // now the problem with Enterprise is that the API doesn't come prefixed
            // it comes suffixed so the API path of http://internal.github.server.local
            // API location is http://internal.github.server.local/api/v3
            let base = format!("{}://{}/api/v3/", repo_url.scheme(), repo_url.host_str().unwrap_or(""));
            Ok(url::Url::parse(&base)?)
        }
    }

    fn get_authorization(&self) -> Vec<(&str, String)> {
        let mut headers = Vec::new();
        if let Some(token) = &self.access_token {
            headers.push(("Authorization", format!("Bearer {}", token)));
        }
        headers
    }

    fn get_releases(&self, include_prereleases: bool) -> Result<Vec<GithubRelease>, Error> {
        // https://docs.github.com/en/rest/reference/releases
        const PER_PAGE: u32 = 100;
        const PAGE: u32 = 1;
        let repo_url = url::Url::parse(&self.repo_url)?;
        let base_uri = self.get_api_base_url(&repo_url)?;
        let releases_path = format!("repos{}/releases?per_page={}&page={}", repo_url.path(), PER_PAGE, PAGE);
        let get_releases_uri = base_uri.join(&releases_path)?;

        info!("Querying GitHub releases: {}", get_releases_uri);
        let auth_headers = self.get_authorization();
        let headers: Vec<(&str, &str)> =
            auth_headers.iter().map(|(k, v)| (*k, v.as_str())).chain(vec![("User-Agent", "velopack-updater-rust")]).collect();

        let response = download::download_url_as_string_with_headers(get_releases_uri.as_str(), &headers)?;
        let mut releases: Vec<GithubRelease> = serde_json::from_str(&response)?;

        // Sort by published_at descending and filter prereleases
        releases.sort_by(|a, b| {
            let a_date = a.published_at.as_deref().unwrap_or("");
            let b_date = b.published_at.as_deref().unwrap_or("");
            b_date.cmp(a_date)
        });

        if include_prereleases {
            Ok(releases)
        } else {
            Ok(releases.into_iter().filter(|r| !r.prerelease).collect())
        }
    }

    fn get_asset_url_from_name(&self, release: &GithubRelease, asset_name: &str) -> Result<String, Error> {
        if release.assets.is_empty() {
            return Err(Error::Generic(format!("No assets found in GitHub Release '{}'.", release.name.as_deref().unwrap_or(""))));
        }

        let matching_assets: Vec<&GithubReleaseAsset> =
            release.assets.iter().filter(|a| a.name.as_deref().map(|n| n.eq_ignore_ascii_case(asset_name)).unwrap_or(false)).collect();

        if matching_assets.is_empty() {
            return Err(Error::Generic(format!(
                "Could not find asset called '{}' in GitHub Release '{}'.",
                asset_name,
                release.name.as_deref().unwrap_or("")
            )));
        }

        let asset = matching_assets[0];

        if self.access_token.as_deref().map(|s| s.is_empty()).unwrap_or(true) {
            // if no access_token provided, we use the browser_download_url which does not
            // count towards the "unauthenticated api request" limit of 60 per hour per IP.
            if let Some(browser_url) = &asset.browser_download_url {
                return Ok(browser_url.clone());
            }
        }

        if let Some(url) = &asset.url {
            // otherwise, we use the regular asset url, which will allow us to retrieve
            // assets from private repositories
            // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
            return Ok(url.clone());
        }

        Err(Error::Generic("Could not find a valid asset url for the specified asset.".to_string()))
    }
}

impl UpdateSource for GithubSource {
    fn get_release_feed(&self, channel: &str, _app: &bundle::Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_filename = format!("releases.{}.json", channel);
        let include_prereleases = self.prerelease;
        let releases = self.get_releases(include_prereleases)?;
        let mut all_assets: Vec<VelopackAsset> = Vec::new();

        for r in releases.into_iter().filter(|r| include_prereleases || !r.prerelease) {
            let Some(asset_url) = self.get_asset_url_from_name(&r, &releases_filename).ok() else { continue };
            info!("Downloading GitHub feed asset '{}' from: {}", releases_filename, asset_url);
            let auth_headers = self.get_authorization();
            let headers: Vec<(&str, &str)> = auth_headers
                .iter()
                .map(|(k, v)| (*k, v.as_str()))
                .chain(vec![("User-Agent", "velopack-updater-rust"), ("Accept", "application/vnd.github.v3+json")])
                .collect();
            let response = download::download_url_as_string_with_headers(&asset_url, &headers)?;
            let feed: VelopackAssetFeed = serde_json::from_str(&response)?;
            all_assets.extend(feed.Assets.into_iter());
        }

        Ok(VelopackAssetFeed { Assets: all_assets })
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let releases = self.get_releases(self.prerelease)?;

        let mut download_url: Option<String> = None;
        for r in releases {
            if let Ok(url) = self.get_asset_url_from_name(&r, &asset.FileName) {
                download_url = Some(url);
                break;
            }
        }

        let Some(url) = download_url else {
            return Err(Error::Generic(format!("Could not find asset '{}' in any GitHub release.", asset.FileName)));
        };

        info!("About to download from URL '{}' to file '{}'", url, local_file);
        let auth_headers = self.get_authorization();
        let headers: Vec<(&str, &str)> = auth_headers
            .iter()
            .map(|(k, v)| (*k, v.as_str()))
            .chain(vec![("User-Agent", "velopack-updater-rust"), ("Accept", "application/octet-stream")])
            .collect();

        download::download_url_to_file_with_headers(&url, local_file, &headers, move |p| {
            if let Some(progress_sender) = &progress_sender {
                let _ = progress_sender.send(p);
            }
        })?;
        Ok(())
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        Box::new(self.clone())
    }
}
