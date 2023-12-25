#![allow(dead_code)]

mod commands;
mod logging;
mod shared;

#[cfg(target_os = "windows")]
mod windows;

#[macro_use]
extern crate log;
extern crate simplelog;
#[macro_use]
extern crate lazy_static;

use anyhow::{anyhow, bail, Result};
use clap::{arg, value_parser, ArgMatches, Command};
use shared::dialogs;
use std::{env, path::PathBuf};

#[rustfmt::skip]
fn root_command() -> Command {
    let cmd = Command::new("Update")
    .version(env!("NGBV_VERSION"))
    .about(format!("Clowd.Squirrel Updater ({}) manages packages and installs updates for Squirrel applications.\nhttps://github.com/clowd/Clowd.Squirrel", env!("NGBV_VERSION")))
    .subcommand(Command::new("apply")
        .about("Applies a staged / prepared update, installing prerequisite runtimes if necessary")
        .arg(arg!(-r --restart "Restart the application after the update"))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before applying the update"))
        .arg(arg!(-p --package <FILE> "Update package to apply").value_parser(value_parser!(PathBuf)))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceeded by '--'.").required(false).last(true).num_args(0..))
    )
    .subcommand(Command::new("start")
        .about("Starts the currently installed version of the application")
        .arg(arg!(-a --args <ARGS> "Legacy args format").aliases(vec!["processStartArgs", "process-start-args"]).hide(true).allow_hyphen_values(true).num_args(1))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before starting the application"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceeded by '--'.").required(false).last(true).num_args(0..))
        .long_flag_aliases(vec!["processStart", "processStartAndWait"])
    )
    .arg(arg!(--verbose "Print debug messages to console / log").global(true))
    .arg(arg!(--nocolor "Disable colored output").hide(true).global(true))
    .arg(arg!(-s --silent "Don't show any prompts / dialogs").global(true))
    .arg(arg!(-l --log <PATH> "Override the default log file location").global(true).value_parser(value_parser!(PathBuf)))
    .disable_help_subcommand(true)
    .flatten_help(true);

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
    #[cfg(target_os = "windows")]
    let matches = parse_command_line_matches(env::args().collect());
    #[cfg(target_os = "macos")]
    let matches = root_command().get_matches();

    let default_log_file = {
        let mut my_dir = env::current_exe().unwrap();
        my_dir.pop();
        my_dir.join("Clowd.Squirrel.log")
    };

    let verbose = matches.get_flag("verbose");
    let silent = matches.get_flag("silent");
    let nocolor = matches.get_flag("nocolor");
    let log_file = matches.get_one("log").unwrap_or(&default_log_file);

    dialogs::set_silent(silent);
    logging::setup_logging(Some(&log_file), true, verbose, nocolor)?;

    info!("Starting Clowd.Squirrel Updater ({})", env!("NGBV_VERSION"));
    info!("    Location: {}", env::current_exe()?.to_string_lossy());
    info!("    Verbose: {}", verbose);
    info!("    Silent: {}", silent);
    info!("    Log File: {}", log_file.to_string_lossy());

    let (subcommand, subcommand_matches) = matches.subcommand().ok_or_else(|| anyhow!("No subcommand was used. Try `--help` for more information."))?;
    let result = match subcommand {
        #[cfg(target_os = "windows")]
        "uninstall" => uninstall(subcommand_matches, log_file).map_err(|e| anyhow!("Uninstall error: {}", e)),
        "start" => start(&subcommand_matches).map_err(|e| anyhow!("Start error: {}", e)),
        "apply" => apply(subcommand_matches).map_err(|e| anyhow!("Apply error: {}", e)),
        _ => bail!("Unknown subcommand. Try `--help` for more information."),
    };

    if let Err(e) = result {
        error!("{}", e);
        return Err(e.into());
    }

    Ok(())
}

fn start(matches: &ArgMatches) -> Result<()> {
    let legacy_args = matches.get_one::<String>("args");
    let wait_for_parent = matches.get_flag("wait");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Command: Start");
    info!("    Wait: {:?}", wait_for_parent);
    info!("    Exe Args: {:?}", exe_args);
    if legacy_args.is_some() {
        info!("    Legacy Args: {:?}", legacy_args);
        warn!("Legacy args format is deprecated and will be removed in a future release. Please update your application to use the new format.");
    }

    commands::start(wait_for_parent, exe_args, legacy_args)
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

    commands::apply(restart, wait_for_parent, package, exe_args)
}

#[cfg(target_os = "windows")]
fn uninstall(_matches: &ArgMatches, log_file: &PathBuf) -> Result<()> {
    info!("Command: Uninstall");
    commands::uninstall(log_file)
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
