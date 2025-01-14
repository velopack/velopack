use super::{bundle, Error, UpdateSource, VelopackAsset, VelopackAssetFeed};
use std::{
    path::{Path, PathBuf},
    sync::mpsc::Sender,
};

/// Retrieves available updates from a local or network-attached disk. The directory
/// must contain one or more valid packages, as well as a 'releases.{channel}.json' index file.
#[derive(Clone)]
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
    fn get_release_feed(&self, channel: &str, _: &bundle::Manifest) -> Result<VelopackAssetFeed, Error> {
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
