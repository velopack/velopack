use std::{
    collections::HashMap,
    path::Path,
    sync::{mpsc::Sender, Mutex},
};

use serde::Deserialize;

use crate::bundle::Manifest;
use crate::*;

use super::UpdateSource;

#[derive(Deserialize)]
struct FlowReleaseAsset {
    #[serde(rename = "Id")]
    id: Option<String>,
    #[serde(flatten)]
    asset: VelopackAsset,
}

/// Retrieves updates from the hosted Velopack service.
pub struct VelopackFlowSource {
    base_uri: String,
    asset_ids: Mutex<HashMap<String, String>>,
}

impl VelopackFlowSource {
    /// Create a new VelopackFlowSource.
    /// - `base_uri`: Optional base URL, defaults to "https://api.velopack.io/"
    pub fn new(base_uri: Option<&str>) -> VelopackFlowSource {
        let uri = base_uri.unwrap_or("https://api.velopack.io/");
        let uri = if uri.ends_with('/') {
            uri.to_string()
        } else {
            format!("{}/", uri)
        };
        VelopackFlowSource {
            base_uri: uri,
            asset_ids: Mutex::new(HashMap::new()),
        }
    }
}

impl Clone for VelopackFlowSource {
    fn clone(&self) -> Self {
        let ids = self.asset_ids.lock().unwrap().clone();
        VelopackFlowSource {
            base_uri: self.base_uri.clone(),
            asset_ids: Mutex::new(ids),
        }
    }
}

impl UpdateSource for VelopackFlowSource {
    fn get_release_feed(&self, channel: &str, app: &Manifest, staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let manifest_url = format!("{}v1.0/manifest/{}/{}", self.base_uri, app.id, channel);
        let mut url = url::Url::parse(&manifest_url)?;

        {
            let mut query = url.query_pairs_mut();
            let arch = std::env::consts::ARCH;
            if !arch.is_empty() {
                query.append_pair("arch", arch);
            }
            let os = std::env::consts::OS;
            if !os.is_empty() {
                query.append_pair("os", os);
            }
            query.append_pair("localVersion", &app.version.to_string());
            if !staged_user_id.is_empty() {
                query.append_pair("stagingId", staged_user_id);
            }
        }

        info!("Downloading releases from '{}'.", url);
        let json = download::download_url_as_string(url.as_str())?;
        let flow_assets: Vec<FlowReleaseAsset> = serde_json::from_str(&json)?;

        let mut ids = self.asset_ids.lock().unwrap();
        let mut assets = Vec::new();
        for fa in flow_assets {
            if let Some(ref id) = fa.id {
                ids.insert(fa.asset.FileName.clone(), id.clone());
            }
            assets.push(fa.asset);
        }

        Ok(VelopackAssetFeed { Assets: assets })
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let ids = self.asset_ids.lock().unwrap();
        let release_id = ids.get(&asset.FileName).ok_or_else(|| {
            Error::Other(format!(
                "Could not find release ID for asset '{}'. Make sure to call get_release_feed first.",
                asset.FileName
            ))
        })?;

        let download_url = format!("{}v1.0/download/{}", self.base_uri, release_id);
        info!("Downloading '{}' from '{}'.", asset.FileName, download_url);
        download::download_url_to_file(&download_url, local_file, move |p| {
            if let Some(progress_sender) = &progress_sender {
                let _ = progress_sender.send(p);
            }
        })?;
        Ok(())
    }
}
