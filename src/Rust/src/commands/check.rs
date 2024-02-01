use crate::bundle::Manifest;
use anyhow::{bail, Result};
use std::path::PathBuf;
use url::{ParseError, Url};

pub fn check(root_path: &PathBuf, app: &Manifest, path: String, allow_downgrade: bool, channel: Option<String>) -> Result<()> {
    match Url::parse(&path) {
        Ok(url) => check_url(root_path, app, url, allow_downgrade, channel),
        _ => {
            let buf = PathBuf::from(&path);
            if !buf.exists() {
                bail!("Path must be a valid HTTP Url or a path to an existing directory: {}", path);
            }
            check_dir(root_path, app, buf, allow_downgrade, channel)
        }
    }
}

fn check_url(root_path: &PathBuf, app: &Manifest, path: Url, allow_downgrade: bool, channel: Option<String>) -> Result<()> {
    let channel = channel.unwrap_or(app.channel.clone());
    let releases_name = format!("releases.{}.json", channel);

    todo!();
}

fn check_dir(root_path: &PathBuf, app: &Manifest, path: PathBuf, allow_downgrade: bool, channel: Option<String>) -> Result<()> {
    let channel = channel.unwrap_or(app.channel.clone());
    let releases_name = format!("releases.{}.json", channel);
    let releases_path = path.join(&releases_name);
    if !releases_path.exists() {
        bail!("Could not find releases file: {}",  path.to_string_lossy());
    }

    


    todo!();
}
