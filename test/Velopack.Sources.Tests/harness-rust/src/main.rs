use std::path::PathBuf;
use std::process::ExitCode;

use clap::Parser;
use serde::Serialize;
use velopack::locator::VelopackLocatorConfig;
use velopack::{sources, UpdateCheck, UpdateManager, UpdateOptions, VelopackAsset};

#[derive(Parser)]
#[command(name = "velopack-source-harness")]
struct Cli {
    /// Source type: gitea, gitlab, http, or file
    source_type: String,

    /// URL or filesystem path for the source
    url_or_path: String,

    /// Access token (empty string means no token)
    token: Option<String>,

    /// Update channel
    #[arg(long)]
    channel: String,

    /// Path to sq.version manifest file
    #[arg(long)]
    manifest: String,

    /// Path to packages directory
    #[arg(long)]
    packages_dir: String,
}

#[allow(non_snake_case)]
#[derive(Serialize)]
struct HarnessOutput {
    target: Option<VelopackAsset>,
    deltas: Option<Vec<VelopackAsset>>,
    isDowngrade: bool,
    feed: Option<Vec<VelopackAsset>>,
}

fn run(cli: Cli) -> Result<(), Box<dyn std::error::Error>> {
    let token = cli.token.filter(|t| !t.is_empty());

    let config = VelopackLocatorConfig {
        ManifestPath: PathBuf::from(&cli.manifest),
        PackagesDir: PathBuf::from(&cli.packages_dir),
        UpdateExePath: std::env::current_exe()?,
        RootAppDir: PathBuf::from(&cli.packages_dir),
        CurrentBinaryDir: PathBuf::from(&cli.packages_dir),
        IsPortable: true,
    };

    let options = UpdateOptions {
        ExplicitChannel: Some(cli.channel.clone()),
        AllowVersionDowngrade: false,
        MaximumDeltasBeforeFallback: 10,
    };

    let um = match cli.source_type.as_str() {
        "gitea" => {
            let source = sources::GiteaSource::new(&cli.url_or_path, token, false);
            UpdateManager::new(source, Some(options), Some(config))?
        }
        "gitlab" => {
            let source = sources::GitlabSource::new(&cli.url_or_path, token, false);
            UpdateManager::new(source, Some(options), Some(config))?
        }
        "http" => {
            let source = sources::HttpSource::new(&cli.url_or_path);
            UpdateManager::new(source, Some(options), Some(config))?
        }
        "file" => {
            let source = sources::FileSource::new(&cli.url_or_path);
            UpdateManager::new(source, Some(options), Some(config))?
        }
        other => {
            return Err(format!("Unknown source type: {}", other).into());
        }
    };

    let check_result = um.check_for_updates()?;
    let feed_result = um.get_release_feed()?;

    let (target, deltas, is_downgrade) = match check_result {
        UpdateCheck::UpdateAvailable(info) => {
            let deltas = if info.DeltasToTarget.is_empty() { None } else { Some(info.DeltasToTarget) };
            (Some(info.TargetFullRelease), deltas, info.IsDowngrade)
        }
        UpdateCheck::NoUpdateAvailable | UpdateCheck::RemoteIsEmpty => (None, None, false),
    };

    let output = HarnessOutput {
        target,
        deltas,
        isDowngrade: is_downgrade,
        feed: Some(feed_result.Assets),
    };

    let json = serde_json::to_string(&output)?;
    println!("{}", json);

    Ok(())
}

fn main() -> ExitCode {
    let cli = Cli::parse();
    match run(cli) {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("Error: {}", e);
            ExitCode::FAILURE
        }
    }
}
