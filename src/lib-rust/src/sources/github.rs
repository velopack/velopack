use super::{bundle, download, Error, UpdateSource, VelopackAsset, VelopackAssetFeed};
use serde::{Deserialize, Serialize};
use std::sync::mpsc::Sender;
use url::Url;

#[derive(Debug, Serialize, Deserialize)]
struct GithubRelease {
    /// The name of this release.
    #[serde(default)]
    #[serde(rename = "name")]
    pub name: String,

    /// True if this release is a prerelease.
    #[serde(default)]
    #[serde(rename = "prerelease")]
    pub prerelease: bool,

    /// The date which this release was published publicly.
    #[serde(default)]
    #[serde(rename = "published_at")]
    pub published_at: String,

    /// A list of assets (files) uploaded to this release.
    #[serde(default)]
    #[serde(rename = "assets")]
    pub assets: Vec<GithubReleaseAsset>,
}

#[derive(Debug, Serialize, Deserialize)]
struct GithubReleaseAsset {
    /// The asset URL for this release asset.
    #[serde(default)]
    #[serde(rename = "url")]
    pub url: String,

    /// The browser URL for this release asset.
    #[serde(default)]
    #[serde(rename = "browser_download_url")]
    pub browser_download_url: String,

    /// The name of this release asset.
    #[serde(default)]
    #[serde(rename = "name")]
    pub name: String,

    /// The mime type of this release asset.
    #[serde(default)]
    #[serde(rename = "content_type")]
    pub content_type: String,
}

#[derive(Clone)]
pub struct GithubSource {
    url: String,
    access_token: Option<String>,
    prerelease: bool,
}

impl GithubSource {
    /// Create a new GithubSource with the specified repository URL.
    pub fn new<S1: AsRef<str>>(url: S1, prerelease: bool, access_token: Option<&str>) -> Self {
        GithubSource { url: url.as_ref().to_string(), prerelease: prerelease, access_token: access_token.map(|s| s.to_string()) }
    }

    fn get_api_base_url(&self) -> Result<String, Error> {
        // https://github.com/velopack/velopack/blob/23d27db4b5147a650e24673eaadfe832db50c567/src/Velopack/Sources/GithubSource.cs#L135
        let base_url: String;

        let url = Url::parse(&self.url)?;

        // handle 2 cases:
        // if github.com url
        if url.host_str() == Some("github.com") {
            base_url = String::from("https://api.github.com/");
        } else {
            // if not github.com url, it's probably an enterprise server
            base_url = format!("{}://{}/api/v3/", url.scheme(), url.host_str().unwrap());
        }

        return Ok(base_url);
    }
}

impl UpdateSource for GithubSource {
    fn get_release_feed(&self, channel: &str, _: &bundle::Manifest) -> Result<VelopackAssetFeed, Error> {
        let per_page = 30;
        let page = 1;
        let url = Url::parse(&self.url)?;
        let releases_path = format!("repos{}/releases?per_page{per_page}&page={page}", url.path().trim_end_matches('/'));
        let base_path = self.get_api_base_url()?;
        let get_releases_uri = format!("{base_path}{releases_path}");
        let response = download::download_url_as_string(&get_releases_uri)?;
        let releases: Vec<GithubRelease> = serde_json::from_str(&response)?;
        let releases_name = format!("releases.{channel}.json");
        let latest_release_gh_asset: &GithubReleaseAsset = releases
            .iter()
            .filter(|release| !release.prerelease)
            .flat_map(|release| &release.assets)
            .filter(|asset| asset.name == releases_name)
            .next()
            .ok_or(Error::FileNotFound(releases_name))?;

        let response = download::download_url_as_string(&latest_release_gh_asset.browser_download_url)?;
        let velopack_asset: VelopackAssetFeed = serde_json::from_str(&response)?;
        println!("{releases:#?}");
        Ok(velopack_asset)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let url = Url::parse(&self.url)?;
        let host = url.host_str().unwrap().trim_end_matches('/');
        let path = url.path().trim_end_matches('/');
        let asset_url = format!("{}{}/releases/download/{}/{}", host, path, asset.Version, asset.FileName);
        println!("{asset_url}");
        info!("About to download from URL '{}' to file '{}'", asset_url, local_file);
        download::download_url_to_file(asset_url.as_str(), local_file, progress)?;
        Ok(())
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        Box::new(self.clone())
    }
}

#[cfg(test)]
mod test {
    use super::*;
    use bundle::Manifest;

    #[test]
    fn get_github_api_base_url() {
        let normal_gh_url = "https://github.com/velopack/velopack/";
        let enterprise_gh_url = "http://internal.github.server.local/";

        let normal_gh_source = GithubSource::new(normal_gh_url, false, None);
        let enterprise_gh_source = GithubSource::new(enterprise_gh_url, false, None);

        assert_eq!("https://api.github.com/", normal_gh_source.get_api_base_url().unwrap());
        assert_eq!("http://internal.github.server.local/api/v3/", enterprise_gh_source.get_api_base_url().unwrap());
    }

    #[test]
    fn get_release_feed() {
        let normal_gh_url = "https://github.com/caesay/VelopackHackathonTest";

        let normal_gh_source = GithubSource::new(normal_gh_url, false, None);
        let _app = Manifest::default();

        //write a better assert
        println!("{:#?}", normal_gh_source.get_release_feed("win", &_app).unwrap());
    }

    #[test]
    fn get_release_entry() {
        let normal_gh_url = "https://github.com/caesay/VelopackHackathonTest";
        let normal_gh_source = GithubSource::new(normal_gh_url, false, None);
        let _app = Manifest::default();
        let asset = normal_gh_source.get_release_feed("win", &_app).unwrap();
        normal_gh_source.download_release_entry(&asset.Assets[0], "C:\\Users\\user\\Documents\\velopack.fusion\\for-rust", None);
    }
}
