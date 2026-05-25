use crate::bundle::Manifest;
use crate::download::DownloadResult;
use crate::errors::Error;
use crate::misc;
use crate::types::{VelopackAsset, VelopackAssetFeed};

use super::UpdateSource;

pub struct FileSource {
    path: String,
}

impl FileSource {
    pub fn new(path: &str) -> Self {
        FileSource { path: path.to_string() }
    }
}

impl UpdateSource for FileSource {
    async fn get_release_feed(&self, channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let base = self.path.trim_end_matches(['/', '\\']);
        let releases_path = format!("{}/{}", base, releases_name);
        let json = crate::host_fs::read_to_string(&releases_path)?;
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    async fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress: &dyn Fn(i16)) -> Result<DownloadResult, Error> {
        let base = self.path.trim_end_matches(['/', '\\']);
        let asset_path = format!("{}/{}", base, asset.FileName);
        progress(0);
        let (size, sha1, sha256) = misc::copy_file_with_checksums(&asset_path, local_file)?;
        progress(100);
        Ok(DownloadResult { size, sha1, sha256 })
    }
}
