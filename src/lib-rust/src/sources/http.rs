use super::{bundle, download, Error, UpdateSource, VelopackAsset, VelopackAssetFeed};
use std::sync::mpsc::Sender;

/// Retrieves updates from a static file host or other web server.
/// Will perform a request for '{baseUri}/RELEASES' to locate the available packages,
/// and provides query parameters to specify the name of the requested package.
#[derive(Clone)]
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
    fn get_release_feed(&self, channel: &str, app: &bundle::Manifest) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);

        let path = self.url.trim_end_matches('/').to_owned() + "/";
        let url = url::Url::parse(&path)?;
        let mut releases_url = url.join(&releases_name)?;
        releases_url.set_query(Some(format!("localVersion={}&id={}", app.version, app.id).as_str()));

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
