#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::{anyhow, bail, Result};
use clap::{arg, value_parser, ArgMatches, Command};
use std::{env, path::PathBuf};
use velopack::*;

#[rustfmt::skip]
fn root_command() -> Command {
    let cmd = Command::new("Update")
    .version(env!("NGBV_VERSION"))
    .about(format!("Velopack Updater ({}) manages packages and installs updates.\nhttps://github.com/velopack/velopack", env!("NGBV_VERSION")))
    .subcommand(Command::new("apply")
        .about("Applies a staged / prepared update, installing prerequisite runtimes if necessary")
        .arg(arg!(-r --restart "Restart the application after the update"))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before applying the update"))
        .arg(arg!(-p --package <FILE> "Update package to apply").value_parser(value_parser!(PathBuf)))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceeded by '--'.").required(false).last(true).num_args(0..))
    )
    .subcommand(Command::new("patch")
        .about("Applies a Zstd patch file")
        .arg(arg!(--old <FILE> "Base / old file to apply the patch to").required(true).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--patch <FILE> "The Zstd patch to apply to the old file").required(true).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--output <FILE> "The file to create with the patch applied").required(true).value_parser(value_parser!(PathBuf)))
    )
    .subcommand(Command::new("check")
        .about("Checks for available updates")
        .arg(arg!(--url <URL> "URL or local folder containing an update source").required(true))
        .arg(arg!(--downgrade "Allow version downgrade"))
        .arg(arg!(--channel <NAME> "Explicitly switch to a specific channel"))
        .arg(arg!(--format <FORMAT> "The format of the program output (json|text)").default_value("json"))
    )
    .subcommand(Command::new("download")
        .about("Download/copies an available remote file into the packages directory")
        .arg(arg!(--url <URL> "URL or local folder containing an update source").required(true))
        .arg(arg!(--name <NAME> "The name of the asset to download").required(true))
        .arg(arg!(--clean "Delete all other packages if download is successful"))
        .arg(arg!(--format <FORMAT> "The format of the program output (json|text)").default_value("json"))
    )
    .subcommand(Command::new("get-version")
        .about("Prints the current version of the application")
    )
    .arg(arg!(--verbose "Print debug messages to console / log").global(true))
    .arg(arg!(--nocolor "Disable colored output").hide(true).global(true))
    .arg(arg!(-s --silent "Don't show any prompts / dialogs").global(true))
    .arg(arg!(-l --log <PATH> "Override the default log file location").global(true).value_parser(value_parser!(PathBuf)))
    .arg(arg!(--forceLatest "Legacy / not used").hide(true))
    .disable_help_subcommand(true)
    .flatten_help(true);

    #[cfg(target_os = "windows")]
    let cmd = cmd.subcommand(Command::new("start")
        .about("Starts the currently installed version of the application")
        .arg(arg!(-a --args <ARGS> "Legacy args format").aliases(vec!["processStartArgs", "process-start-args"]).hide(true).allow_hyphen_values(true).num_args(1))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before starting the application"))
        .arg(arg!([EXE_NAME] "The optional name of the binary to execute"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceeded by '--'.").required(false).last(true).num_args(0..))
        .long_flag_aliases(vec!["processStart", "processStartAndWait"])
    );

    #[cfg(target_os = "windows")]
    let cmd = cmd.subcommand(Command::new("uninstall")
        .about("Remove all app shortcuts, files, and registry entries.")
        .long_flag_alias("uninstall")
    );
    cmd
}

#[cfg(target_os = "windows")]
fn parse_command_line_matches(input_args: Vec<String>) -> ArgMatches {
    // Split the arguments manually to handle the legacy `--flag=value` syntax
    // Also, replace `--processStartAndWait` with `--processStart --wait`
    let args: Vec<String> = input_args
        .into_iter()
        .flat_map(|arg| if arg.contains('=') { arg.splitn(2, '=').map(String::from).collect::<Vec<_>>() } else { vec![arg] })
        .flat_map(|arg| if arg.eq_ignore_ascii_case("--processStartAndWait") { vec!["--processStart".to_string(), "--wait".to_string()] } else { vec![arg] })
        .collect();
    root_command().get_matches_from(&args)
}

fn main() -> Result<()> {
    #[cfg(windows)]
    let matches = parse_command_line_matches(env::args().collect());
    #[cfg(unix)]
    let matches = root_command().get_matches();

    let (subcommand, subcommand_matches) = matches.subcommand().ok_or_else(|| anyhow!("No subcommand was used. Try `--help` for more information."))?;

    let verbose = matches.get_flag("verbose");
    let mut silent = matches.get_flag("silent");
    let nocolor = matches.get_flag("nocolor");
    let log_file = matches.get_one("log");

    // these commands output machine-readable data, so we don't want to show dialogs or logs to stdout
    let no_console = subcommand.eq_ignore_ascii_case("check") || subcommand.eq_ignore_ascii_case("download") || subcommand.eq_ignore_ascii_case("get-version");
    if no_console {
        silent = true;
    }

    dialogs::set_silent(silent);
    if let Some(log_file) = log_file {
        logging::setup_logging(Some(&log_file), !no_console, verbose, nocolor)?;
    } else {
        default_logging(verbose, nocolor, !no_console)?;
    }

    info!("Starting Velopack Updater ({})", env!("NGBV_VERSION"));
    info!("    Location: {}", env::current_exe()?.to_string_lossy());
    info!("    Verbose: {}", verbose);
    info!("    Silent: {}", silent);
    info!("    Log File: {:?}", log_file);

    // change working directory to the containing directory of the exe
    let mut containing_dir = env::current_exe()?;
    containing_dir.pop();
    env::set_current_dir(containing_dir)?;

    let result = match subcommand {
        #[cfg(target_os = "windows")]
        "uninstall" => uninstall(subcommand_matches).map_err(|e| anyhow!("Uninstall error: {}", e)),
        #[cfg(target_os = "windows")]
        "start" => start(subcommand_matches).map_err(|e| anyhow!("Start error: {}", e)),
        "apply" => apply(subcommand_matches).map_err(|e| anyhow!("Apply error: {}", e)),
        "patch" => patch(subcommand_matches).map_err(|e| anyhow!("Patch error: {}", e)),
        "check" => check(subcommand_matches).map_err(|e| anyhow!("Check error: {}", e)),
        "download" => download(subcommand_matches).map_err(|e| anyhow!("Download error: {}", e)),
        "get-version" => get_version(subcommand_matches).map_err(|e| anyhow!("Get-version error: {}", e)),
        _ => bail!("Unknown subcommand. Try `--help` for more information."),
    };

    if let Err(e) = result {
        error!("{}", e);
        return Err(e.into());
    }

    Ok(())
}

fn get_version(_matches: &ArgMatches) -> Result<()> {
    let (_, app) = shared::detect_current_manifest()?;
    println!("{}", app.version);
    Ok(())
}

fn check(matches: &ArgMatches) -> Result<()> {
    let url = matches.get_one::<String>("url").unwrap();
    let format = matches.get_one::<String>("format").unwrap();
    let allow_downgrade = matches.get_flag("downgrade");
    let channel = matches.get_one::<String>("channel").map(|x| x.as_str());
    let is_json = format.eq_ignore_ascii_case("json");

    info!("Command: Check");
    info!("    URL: {:?}", url);
    info!("    Allow Downgrade: {:?}", allow_downgrade);
    info!("    Channel: {:?}", channel);
    info!("    Format: {:?}", format);

    // this is a machine readable command, so we write program output to stdout in the desired format
    let (_, app) = shared::detect_current_manifest()?;
    match commands::check(&app, url, allow_downgrade, channel) {
        Ok(opt) => match opt {
            Some(info) => {
                if is_json {
                    println!("{}", serde_json::to_string(&info)?);
                } else {
                    let asset = info.TargetFullRelease;
                    println!("{} {} {} {}", asset.Version, asset.SHA1, asset.FileName, asset.Size);
                }
            }
            _ => println!("null"),
        },
        Err(e) => {
            if is_json {
                println!("{{ \"error\": \"{}\" }}", e);
            } else {
                println!("err: {}", e);
            }
            return Err(e);
        }
    }
    Ok(())
}

fn download(matches: &ArgMatches) -> Result<()> {
    let url = matches.get_one::<String>("url").unwrap();
    let name = matches.get_one::<String>("name").unwrap();
    let format = matches.get_one::<String>("format").unwrap();
    let clean = matches.get_flag("clean");
    let is_json = format.eq_ignore_ascii_case("json");

    info!("Command: Download");
    info!("    URL: {:?}", url);
    info!("    Asset Name: {:?}", name);
    info!("    Format: {:?}", format);
    info!("    Clean: {:?}", clean);

    // this is a machine readable command, so we write program output to stdout in the desired format
    let (root_path, app) = shared::detect_current_manifest()?;
    #[cfg(target_os = "windows")]
    let _mutex = shared::retry_io(|| windows::create_global_mutex(&app))?;
    match commands::download(&root_path, &app, url, clean, name, |p| {
        if is_json {
            println!("{{ \"progress\": {} }}", p);
        } else {
            println!("{}", p);
        }
    }) {
        Ok(path) => {
            if is_json {
                println!("{{ \"complete\": true, \"progress\": 100, \"file\": \"{}\" }}", path.to_string_lossy());
            } else {
                println!("complete: {}", path.to_string_lossy());
            }
        }
        Err(e) => {
            if is_json {
                println!("{{ \"error\": \"{}\" }}", e);
            } else {
                println!("err: {}", e);
            }
            return Err(e);
        }
    }
    Ok(())
}

fn patch(matches: &ArgMatches) -> Result<()> {
    let old_file = matches.get_one::<PathBuf>("old").unwrap();
    let patch_file = matches.get_one::<PathBuf>("patch").unwrap();
    let output_file = matches.get_one::<PathBuf>("output").unwrap();

    info!("Command: Patch");
    info!("    Old File: {:?}", old_file);
    info!("    Patch File: {:?}", patch_file);
    info!("    Output File: {:?}", output_file);

    commands::patch(old_file, patch_file, output_file)
}

fn apply(matches: &ArgMatches) -> Result<()> {
    let restart = matches.get_flag("restart");
    let wait_for_parent = matches.get_flag("wait");
    let package = matches.get_one::<PathBuf>("package");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Command: Apply");
    info!("    Restart: {:?}", restart);
    info!("    Wait: {:?}", wait_for_parent);
    info!("    Package: {:?}", package);
    info!("    Exe Args: {:?}", exe_args);

    let (root_path, app) = shared::detect_current_manifest()?;
    #[cfg(target_os = "windows")]
    let _mutex = shared::retry_io(|| windows::create_global_mutex(&app))?;
    commands::apply(&root_path, &app, restart, wait_for_parent, package, exe_args, true)
}

#[cfg(target_os = "windows")]
fn start(matches: &ArgMatches) -> Result<()> {
    let legacy_args = matches.get_one::<String>("args");
    let wait_for_parent = matches.get_flag("wait");
    let exe_name = matches.get_one::<String>("EXE_NAME");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Command: Start");
    info!("    Wait: {:?}", wait_for_parent);
    info!("    Exe Name: {:?}", exe_name);
    info!("    Exe Args: {:?}", exe_args);
    if legacy_args.is_some() {
        info!("    Legacy Args: {:?}", legacy_args);
        warn!("Legacy args format is deprecated and will be removed in a future release. Please update your application to use the new format.");
    }

    let (_root_path, app) = shared::detect_current_manifest()?;
    let _mutex = shared::retry_io(|| windows::create_global_mutex(&app))?;
    commands::start(wait_for_parent, exe_name, exe_args, legacy_args)
}

#[cfg(target_os = "windows")]
fn uninstall(_matches: &ArgMatches) -> Result<()> {
    info!("Command: Uninstall");
    let (root_path, app) = shared::detect_current_manifest()?;
    commands::uninstall(&root_path, &app, true)
}

pub fn default_logging(verbose: bool, nocolor: bool, console: bool) -> Result<()> {
    #[cfg(windows)]
    let default_log_file = {
        let mut my_dir = env::current_exe().unwrap();
        my_dir.pop();
        my_dir.join("Velopack.log")
    };

    #[cfg(unix)]
    let default_log_file = std::path::Path::new("/tmp/velopack.log").to_path_buf();

    logging::setup_logging(Some(&default_log_file), console, verbose, nocolor)
}

#[cfg(target_os = "windows")]
#[test]
fn test_start_command_supports_legacy_commands() {
    fn get_start_args(matches: &ArgMatches) -> (bool, Option<&String>, Option<&String>, Option<Vec<&String>>) {
        let legacy_args = matches.get_one::<String>("args");
        let wait_for_parent = matches.get_flag("wait");
        let exe_name = matches.get_one::<String>("EXE_NAME");
        let exe_args: Option<Vec<&String>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.collect());
        return (wait_for_parent, exe_name, legacy_args, exe_args);
    }

    fn try_parse_command_line_matches(input_args: Vec<String>) -> Result<ArgMatches> {
        // Split the arguments manually to handle the legacy `--flag=value` syntax
        // Also, replace `--processStartAndWait` with `--processStart --wait`
        let args: Vec<String> = input_args
            .into_iter()
            .flat_map(|arg| if arg.contains('=') { arg.splitn(2, '=').map(String::from).collect::<Vec<_>>() } else { vec![arg] })
            .flat_map(
                |arg| if arg.eq_ignore_ascii_case("--processStartAndWait") { vec!["--processStart".to_string(), "--wait".to_string()] } else { vec![arg] },
            )
            .collect();
        root_command().try_get_matches_from(&args).map_err(|e| anyhow!("{}", e))
    }

    let command = vec!["Update.exe", "--processStart=hello.exe"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, false);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStart", "hello.exe"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, false);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait=hello.exe"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "-a", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "-a", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "--processStartArgs", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "--process-start-args", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "-a", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, None);
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "-a", "-- -c \" asda --aasd"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, None);
    assert_eq!(legacy_args, Some(&"-- -c \" asda --aasd".to_string()));
    assert_eq!(exe_args, None);
}
