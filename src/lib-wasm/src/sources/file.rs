use crate::bundle::Manifest;
use crate::errors::Error;
use crate::types::{VelopackAsset, VelopackAssetFeed};

use super::UpdateSource;

/// An update source that reads releases from a local filesystem directory.
pub struct FileSource {
    path: String,
}

impl FileSource {
    /// Creates a new `FileSource` rooted at the given directory path.
    pub fn new(path: &str) -> Self {
        FileSource { path: path.to_string() }
    }
}

impl UpdateSource for FileSource {
    async fn get_release_feed(&self, channel: &str, _app: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let base = self.path.trim_end_matches(['/', '\\']);
        let releases_path = format!("{}/{}", base, releases_name);
        let json = std::fs::read_to_string(&releases_path)?;
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    async fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress: &dyn Fn(i16)) -> Result<(), Error> {
        use std::io::{Read, Write};
        let base = self.path.trim_end_matches(['/', '\\']);
        let asset_path = format!("{}/{}", base, asset.FileName);
        progress(0);
        let mut src = std::fs::File::open(&asset_path)
            .map_err(|e| Error::Other(format!("Failed to open source {}: {}", asset_path, e)))?;
        let mut dst = std::fs::File::create(local_file)
            .map_err(|e| Error::Other(format!("Failed to create dest {}: {}", local_file, e)))?;
        let mut buf = vec![0u8; 64 * 1024];
        let mut total = 0u64;
        let file_size = src.metadata().map(|m| m.len()).unwrap_or(0);
        loop {
            let n = src.read(&mut buf)
                .map_err(|e| Error::Other(format!("Read error: {}", e)))?;
            if n == 0 { break; }
            dst.write_all(&buf[..n])
                .map_err(|e| Error::Other(format!("Write error: {}", e)))?;
            total += n as u64;
            if file_size > 0 {
                progress(((total as f64 / file_size as f64) * 100.0) as i16);
            }
        }
        progress(100);
        Ok(())
    }
}
