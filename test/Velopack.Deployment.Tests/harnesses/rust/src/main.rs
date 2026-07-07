//! Rust source-test harness for Velopack.Deployment.Tests.
//!
//! Invocation contract (shared by every language harness):
//!   deployment-test-harness <config.json>
//! The result JSON is written to stdout as the LAST line; all log noise goes to stderr.
//! Exit 0 on success, exit 1 on failure (still emitting {"ok":false,"error":...} as the last stdout line).
//!
//! Config schema:
//!   { "source":  { "kind": "file|http|gitea|gitlab|github", "url": "...", "token": null, "prerelease": false },
//!     "locator": { PascalCase VelopackLocatorConfig fields },
//!     "channel": "stable",
//!     "action":  "check" | "download",
//!     "downloadDir": "..." }
//! Downloads always land in locator.PackagesDir (the core ignores downloadDir); we report the real path.

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use velopack::locator::VelopackLocatorConfig;
use velopack::sources::{FileSource, GiteaSource, GithubSource, GitlabSource, HttpSource, UpdateSource};
use velopack::{UpdateCheck, UpdateManager, UpdateOptions};

#[derive(Deserialize)]
struct SourceConfig {
    kind: String,
    url: String,
    #[serde(default)]
    token: Option<String>,
    #[serde(default)]
    prerelease: bool,
}

#[derive(Deserialize)]
struct Config {
    source: SourceConfig,
    // VelopackLocatorConfig fields are already PascalCase, so the PascalCase JSON produced by
    // LocatorSpec.ToJson deserializes directly with no rename.
    locator: VelopackLocatorConfig,
    channel: String,
    action: String,
    #[serde(default)]
    #[serde(rename = "downloadDir")]
    _download_dir: Option<String>,
}

#[allow(non_snake_case)]
#[derive(Serialize, Default)]
struct HarnessResult {
    ok: bool,
    updateAvailable: bool,
    targetVersion: Option<String>,
    downloadedFile: Option<String>,
    sha256: Option<String>,
    error: Option<String>,
}

fn build_source(cfg: &SourceConfig) -> Result<Box<dyn UpdateSource>, String> {
    let token = cfg.token.clone();
    match cfg.kind.as_str() {
        "file" => Ok(Box::new(FileSource::new(&cfg.url))),
        "http" => Ok(Box::new(HttpSource::new(&cfg.url))),
        "gitea" => Ok(Box::new(GiteaSource::new(&cfg.url, token, cfg.prerelease))),
        "gitlab" => Ok(Box::new(GitlabSource::new(&cfg.url, token, cfg.prerelease))),
        "github" => Ok(Box::new(GithubSource::new(&cfg.url, token, cfg.prerelease))),
        other => Err(format!("unknown source kind: {}", other)),
    }
}

fn sha256_upper(path: &Path) -> Result<String, String> {
    let bytes = std::fs::read(path).map_err(|e| format!("failed to read '{:?}': {}", path, e))?;
    let mut hasher = Sha256::new();
    hasher.update(&bytes);
    let hash = hasher.finalize();
    Ok(hash.iter().map(|b| format!("{:02X}", b)).collect())
}

fn run() -> Result<HarnessResult, String> {
    let config_path = std::env::args()
        .nth(1)
        .ok_or_else(|| "usage: deployment-test-harness <config.json>".to_string())?;

    let json = std::fs::read_to_string(&config_path).map_err(|e| format!("failed to read config '{}': {}", config_path, e))?;
    let config: Config = serde_json::from_str(&json).map_err(|e| format!("failed to parse config: {}", e))?;

    // Capture the packages dir before the locator config is moved into the manager, so the download
    // action can locate the file the core wrote (downloads always go to the locator packages dir).
    let packages_dir: PathBuf = config.locator.PackagesDir.clone();

    let source = build_source(&config.source)?;
    let options = UpdateOptions {
        AllowVersionDowngrade: false,
        ExplicitChannel: Some(config.channel.clone()),
        MaximumDeltasBeforeFallback: 10,
    };

    let um = UpdateManager::new_boxed(source, Some(options), Some(config.locator)).map_err(|e| format!("failed to create UpdateManager: {}", e))?;

    let mut result = HarnessResult::default();
    result.ok = true;

    let update = match um.check_for_updates().map_err(|e| format!("check_for_updates failed: {}", e))? {
        UpdateCheck::UpdateAvailable(info) => Some(*info),
        UpdateCheck::NoUpdateAvailable | UpdateCheck::RemoteIsEmpty => None,
    };

    result.updateAvailable = update.is_some();
    result.targetVersion = update.as_ref().map(|u| u.TargetFullRelease.Version.clone());

    if config.action == "download" {
        let update = update.ok_or_else(|| "no update available to download".to_string())?;
        um.download_updates(&update, None)
            .map_err(|e| format!("download_updates failed: {}", e))?;
        let downloaded = packages_dir.join(&update.TargetFullRelease.FileName);
        if !downloaded.exists() {
            return Err(format!("expected downloaded file not found: {:?}", downloaded));
        }
        result.sha256 = Some(sha256_upper(&downloaded)?);
        result.downloadedFile = Some(downloaded.to_string_lossy().into_owned());
    }

    Ok(result)
}

fn main() {
    match run() {
        Ok(result) => {
            println!("{}", serde_json::to_string(&result).expect("serialize result"));
        }
        Err(err) => {
            eprintln!("{}", err);
            let result = HarnessResult {
                ok: false,
                error: Some(err),
                ..Default::default()
            };
            println!("{}", serde_json::to_string(&result).expect("serialize result"));
            std::process::exit(1);
        }
    }
}
