use crate::bundle::Manifest;
use crate::download::DownloadResult;
use crate::errors::Error;
use crate::types::{VelopackAsset, VelopackAssetFeed};

use super::UpdateSource;

pub struct HttpSource {
    url: String,
}

impl HttpSource {
    pub fn new(url: &str) -> Self {
        HttpSource { url: url.to_string() }
    }
}

impl UpdateSource for HttpSource {
    async fn get_release_feed(&self, channel: &str, app: &Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let base = self.url.trim_end_matches('/').to_owned() + "/";
        let base_url = url::Url::parse(&base).map_err(|e| Error::Network(e.to_string()))?;
        let mut releases_url = base_url.join(&releases_name).map_err(|e| Error::Network(e.to_string()))?;

        releases_url.set_query(Some(&format!("localVersion={}&id={}&stagingId={}", app.version, app.id, staged_user_id)));

        let json = crate::download::download_url_as_string(releases_url.as_str()).await?;
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    async fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress: &dyn Fn(i16)) -> Result<DownloadResult, Error> {
        let base = self.url.trim_end_matches('/').to_owned() + "/";
        let base_url = url::Url::parse(&base).map_err(|e| Error::Network(e.to_string()))?;
        let asset_url = base_url.join(&asset.FileName).map_err(|e| Error::Network(e.to_string()))?;

        crate::download::download_url_to_file(asset_url.as_str(), local_file, |p| progress(p)).await
    }
}
