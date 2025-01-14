use std::sync::mpsc::Sender;

use super::{bundle, Error, FileSource, HttpSource, UpdateSource, VelopackAsset, VelopackAssetFeed};

/// Automatically delegates to the appropriate source based on the provided input string. If the input is a local path,
/// it will use a FileSource. If the input is a URL, it will use an HttpSource.
#[derive(Clone)]
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
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest) -> Result<VelopackAssetFeed, Error> {
        self.source.get_release_feed(channel, app)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        self.source.download_release_entry(asset, local_file, progress_sender)
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        self.source.clone_boxed()
    }
}
