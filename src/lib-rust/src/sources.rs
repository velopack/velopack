use std::{
    path::{Path, PathBuf},
    sync::mpsc::Sender,
};

use crate::*;
use crate::bundle::Manifest;

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
    fn download_release_entry(&self, _asset: &VelopackAsset, _local_file: &str, _progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
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
        let source: Box<dyn UpdateSource> = if Self::is_http_url(input) {
            Box::new(HttpSource::new(input))
        } else {
            Box::new(FileSource::new(input))
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
