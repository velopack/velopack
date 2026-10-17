use std::{
    fs::File,
    io::{Read, Seek, SeekFrom},
    path::{Path, PathBuf},
    sync::{mpsc::Sender, Mutex},
};

use crate::bundle::Manifest;
use crate::chunks::{RangeError, RangeReader};
use crate::*;

use super::UpdateSource;

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
    fn get_release_feed(&self, channel: &str, _: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let releases_path = self.path.join(&releases_name);

        info!("Reading releases from file: {:?}", releases_path);
        let json = std::fs::read_to_string(releases_path)?;
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let asset_path = self.path.join(&asset.FileName);
        info!("About to copy from file '{:?}' to file '{:?}'", asset_path, local_file);
        if let Some(progress_sender) = &progress_sender {
            let _ = progress_sender.send(50);
        }
        std::fs::copy(asset_path, local_file)?;
        if let Some(progress_sender) = &progress_sender {
            let _ = progress_sender.send(100);
        }
        Ok(())
    }

    fn open_range_reader(&self, asset: &VelopackAsset) -> Result<Option<Box<dyn RangeReader>>, Error> {
        let asset_path = self.path.join(&asset.FileName);
        info!("Opening ranged access to file '{:?}'", asset_path);
        Ok(Some(Box::new(FileRangeReader::open(&asset_path)?)))
    }
}

/// Byte-range access to a package on disk (seek + read_exact on one file handle).
pub struct FileRangeReader {
    file: Mutex<File>,
    len: u64,
}

impl FileRangeReader {
    /// Opens the file for ranged reads.
    pub fn open(path: &Path) -> Result<FileRangeReader, Error> {
        let file = File::open(path)?;
        let len = file.metadata()?.len();
        Ok(FileRangeReader { file: Mutex::new(file), len })
    }
}

impl RangeReader for FileRangeReader {
    fn len(&self) -> Option<u64> {
        Some(self.len)
    }

    fn read_exact_at(&self, offset: u64, buf: &mut [u8]) -> Result<(), RangeError> {
        if offset.checked_add(buf.len() as u64).map(|end| end > self.len).unwrap_or(true) {
            return Err(RangeError::Unavailable(format!(
                "range {}+{} lies outside the {} byte file",
                offset,
                buf.len(),
                self.len
            )));
        }
        let mut file = self.file.lock().unwrap();
        file.seek(SeekFrom::Start(offset)).map_err(|e| RangeError::Transient(e.to_string()))?;
        file.read_exact(buf).map_err(|e| RangeError::Transient(e.to_string()))
    }
}
