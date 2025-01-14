use std::sync::mpsc::Sender;

use crate::bundle::Manifest;
use crate::*;

mod auto;
pub use auto::AutoSource;

mod file;
pub use file::FileSource;

mod github;
pub use github::GithubSource;

mod http;
pub use http::HttpSource;

/// Abstraction for finding and downloading updates from a package source / repository.
/// An implementation may copy a file from a local repository, download from a web address,
/// or even use third party services and parse proprietary data to produce a package feed.
pub trait UpdateSource: Send + Sync {
    /// Retrieve the list of available remote releases from the package source. These releases
    /// can subsequently be downloaded with download_release_entry.
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest) -> Result<VelopackAssetFeed, Error>;
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
    fn get_release_feed(&self, _channel: &str, _app: &Manifest) -> Result<VelopackAssetFeed, Error> {
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
