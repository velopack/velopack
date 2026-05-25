#![allow(async_fn_in_trait)]

use crate::bundle::Manifest;
use crate::download::DownloadResult;
use crate::errors::Error;
use crate::types::{VelopackAsset, VelopackAssetFeed};

pub mod file;
pub mod http;

pub use self::http::HttpSource;
pub use file::FileSource;

pub trait UpdateSource {
    async fn get_release_feed(&self, channel: &str, app: &Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error>;

    async fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress: &dyn Fn(i16)) -> Result<DownloadResult, Error>;
}

pub struct NoneSource;

impl UpdateSource for NoneSource {
    async fn get_release_feed(&self, _channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        Err(Error::NotSupported("No update source has been configured".into()))
    }

    async fn download_release_entry(&self, _asset: &VelopackAsset, _local_file: &str, _progress: &dyn Fn(i16)) -> Result<DownloadResult, Error> {
        Err(Error::NotSupported("No update source has been configured".into()))
    }
}

pub enum AutoSource {
    File(FileSource),
    Http(HttpSource),
    None(NoneSource),
}

impl AutoSource {
    pub fn new(input: &str) -> Self {
        let lower = input.trim().to_lowercase();
        if lower.starts_with("http://") || lower.starts_with("https://") {
            AutoSource::Http(HttpSource::new(input))
        } else {
            AutoSource::File(FileSource::new(input))
        }
    }
}

impl UpdateSource for AutoSource {
    async fn get_release_feed(&self, channel: &str, app: &Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        match self {
            AutoSource::File(s) => s.get_release_feed(channel, app, staged_user_id).await,
            AutoSource::Http(s) => s.get_release_feed(channel, app, staged_user_id).await,
            AutoSource::None(s) => s.get_release_feed(channel, app, staged_user_id).await,
        }
    }

    async fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress: &dyn Fn(i16)) -> Result<DownloadResult, Error> {
        match self {
            AutoSource::File(s) => s.download_release_entry(asset, local_file, progress).await,
            AutoSource::Http(s) => s.download_release_entry(asset, local_file, progress).await,
            AutoSource::None(s) => s.download_release_entry(asset, local_file, progress).await,
        }
    }
}
