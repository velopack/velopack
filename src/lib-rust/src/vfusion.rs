mod logging;

use anyhow::Result;
use clap::{arg, ArgMatches, Command};
use std::env;
use velopack::{sources::UpdateSource, *};

#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate log;

#[rustfmt::skip]
fn root_command() -> Command {
    let cmd = Command::new("Update")
    .version(env!("CARGO_PKG_VERSION"))
    .about(format!("Velopack Fusion ({}) manages and downloads packages.\nhttps://github.com/velopack/velopack", env!("CARGO_PKG_VERSION")))
    .subcommand(Command::new("get-version")
        .about("Prints the current version of the application")
    )
    .subcommand(Command::new("get-packages")
        .about("Prints the path to the packages directory")
    )
    .subcommand(Command::new("check")
        .about("Checks for available updates")
        .arg(arg!(--url <URL> "URL or local folder containing an update source").required(true))
        .arg(arg!(--downgrade "Allow version downgrade"))
        .arg(arg!(--channel <NAME> "Explicitly switch to a specific channel"))
    )
    .subcommand(Command::new("download")
        .about("Download/copies an available remote file into the packages directory")
        .arg(arg!(--url <URL> "URL or local folder containing an update source").required(true))
        .arg(arg!(--name <NAME> "The name of the release to download").required(true))
        .arg(arg!(--channel <NAME> "Explicitly switch to a specific channel"))
    )
    .arg(arg!(--verbose "Print debug messages to console / log").global(true))
    .disable_help_subcommand(true)
    .flatten_help(true);
    return cmd;
}

fn main() -> Result<()> {
    let matches = root_command().get_matches();
    let (subcommand, subcommand_matches) =
        matches.subcommand().ok_or_else(|| anyhow!("No subcommand was used. Try `--help` for more information."))?;
    let verbose = matches.get_flag("verbose");

    let default_log_file = locator::default_log_location();
    logging::setup_logging(&default_log_file, verbose)?;
    
    info!("--");
    info!("Starting Velopack Fusion ({})", env!("CARGO_PKG_VERSION"));
    info!("    Location: {}", env::current_exe()?.to_string_lossy());
    info!("    Verbose: {}", verbose);

    // change working directory to the containing directory of the exe
    let mut containing_dir = env::current_exe()?;
    containing_dir.pop();
    env::set_current_dir(containing_dir)?;

    let result = match subcommand {
        "check" => check(subcommand_matches).map_err(|e| anyhow!("Check error: {}", e)),
        "download" => download(subcommand_matches).map_err(|e| anyhow!("Download error: {}", e)),
        "get-version" => get_version(subcommand_matches).map_err(|e| anyhow!("Get-version error: {}", e)),
        "get-packages" => get_packages(subcommand_matches).map_err(|e| anyhow!("Get-packages error: {}", e)),
        _ => bail!("Unknown subcommand. Try `--help` for more information."),
    };

    if let Err(e) = result {
        error!("{}", e);
        return Err(e.into());
    }

    Ok(())
}

fn get_version(_matches: &ArgMatches) -> Result<()> {
    info!("Command: Get-Version");
    let loc = locator::auto_locate()?;
    info!("    Version: {}", loc.manifest.version);
    println!("{}", loc.manifest.version);
    Ok(())
}

fn get_packages(_matches: &ArgMatches) -> Result<()> {
    info!("Command: Get-Packages");
    let loc = locator::auto_locate()?;
    info!("    Packages Directory: {}", loc.packages_dir.to_string_lossy());
    println!("{}", loc.packages_dir.to_string_lossy());
    Ok(())
}

fn check(matches: &ArgMatches) -> Result<()> {
    let url = matches.get_one::<String>("url").unwrap();
    let allow_downgrade = matches.get_flag("downgrade");
    let channel = matches.get_one::<String>("channel").map(|x| x.to_owned());

    info!("Command: Check");
    info!("    URL: {:?}", url);
    info!("    Allow Downgrade: {:?}", allow_downgrade);
    info!("    Channel: {:?}", channel);

    let options = UpdateOptions { AllowVersionDowngrade: allow_downgrade, ExplicitChannel: channel };
    let updates = if is_http_url(url) {
        let source = sources::HttpSource::new(url);
        let um = UpdateManager::new(source, Some(options))?;
        um.check_for_updates()?
    } else {
        let source = sources::FileSource::new(url);
        let um = UpdateManager::new(source, Some(options))?;
        um.check_for_updates()?
    };

    if let Some(info) = updates {
        println!("{}", serde_json::to_string(&info)?);
    }

    Ok(())
}

fn download(matches: &ArgMatches) -> Result<()> {
    let url = matches.get_one::<String>("url").unwrap();
    let name = matches.get_one::<String>("name").map(|x| x.to_owned()).unwrap();
    let channel = matches.get_one::<String>("channel").map(|x| x.to_owned());

    info!("Command: Download");
    info!("    URL: {:?}", url);
    info!("    Asset Name: {:?}", name);
    info!("    Channel: {:?}", channel);

    if is_http_url(url) {
        let source = sources::HttpSource::new(url);
        download_generic(source, &name, channel)?;
    } else {
        let source = sources::FileSource::new(url);
        download_generic(source, &name, channel)?;
    };
    Ok(())
}

fn download_generic<T: UpdateSource>(source: T, name: &str, channel: Option<String>) -> Result<()> {
    let options = UpdateOptions { AllowVersionDowngrade: false, ExplicitChannel: channel };
    let um = UpdateManager::new(source, Some(options))?;
    let feed = um.get_release_feed()?;
    let asset = feed.find(&name).ok_or_else(|| anyhow!("Asset not found in feed: {}", name))?;

    let info = UpdateInfo { IsDowngrade: false, TargetFullRelease: asset.clone() };
    um.download_updates(&info, |p| {
        println!("{}", p);
    })?;
    Ok(())
}

fn is_http_url(url: &str) -> bool {
    match url::Url::parse(url) {
        Ok(url) => url.scheme().eq_ignore_ascii_case("http") || url.scheme().eq_ignore_ascii_case("https"),
        _ => false,
    }
}
