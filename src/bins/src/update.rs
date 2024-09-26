#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::{anyhow, bail, Result};
use clap::{arg, value_parser, ArgMatches, Command};
use std::{env, path::PathBuf};
use velopack::locator;
use velopack::locator::{auto_locate_app_manifest, LocationContext};
use velopack_bins::*;

#[rustfmt::skip]
fn root_command() -> Command {
    let cmd = Command::new("Update")
    .version(env!("NGBV_VERSION"))
    .about(format!("Velopack Updater ({}) manages packages and installs updates.\nhttps://github.com/velopack/velopack", env!("NGBV_VERSION")))
    .subcommand(Command::new("apply")
        .about("Applies a staged / prepared update, installing prerequisite runtimes if necessary")
        .arg(arg!(--norestart "Do not restart the application after the update"))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before applying the update").hide(true))
        .arg(arg!(--waitPid <PID> "Wait for the specified process to terminate before applying the update").value_parser(value_parser!(u32)))
        .arg(arg!(-p --package <FILE> "Update package to apply").value_parser(value_parser!(PathBuf)))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceded by '--'.").required(false).last(true).num_args(0..))
    )
    .subcommand(Command::new("start")
        .about("Starts the currently installed version of the application")
        .arg(arg!(-a --args <ARGS> "Legacy args format").aliases(vec!["processStartArgs", "process-start-args"]).hide(true).allow_hyphen_values(true).num_args(1))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before starting the application").hide(true))
        .arg(arg!(--waitPid <PID> "Wait for the specified process to terminate before applying the update").value_parser(value_parser!(u32)))
        .arg(arg!([EXE_NAME] "The optional name of the binary to execute"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceded by '--'.").required(false).last(true).num_args(0..))
        .long_flag_aliases(vec!["processStart", "processStartAndWait"])
    )
    .subcommand(Command::new("patch")
        .about("Applies a Zstd patch file")
        .arg(arg!(--old <FILE> "Base / old file to apply the patch to").required(true).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--patch <FILE> "The Zstd patch to apply to the old file").required(true).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--output <FILE> "The file to create with the patch applied").required(true).value_parser(value_parser!(PathBuf)))
    )
    .subcommand(Command::new("get-version")
        .about("Prints the current version of the application")
    )
    .arg(arg!(--verbose "Print debug messages to console / log").global(true))
    .arg(arg!(-s --silent "Don't show any prompts / dialogs").global(true))
    .arg(arg!(-l --log <PATH> "Override the default log file location").global(true).value_parser(value_parser!(PathBuf)))
        // Legacy arguments should not be fully removed if it's possible to keep them
        // Reason being is clap.ignore_errors(true) is not 100%, and sometimes old args can trip things up.
    .arg(arg!(--forceLatest "Legacy argument").hide(true).global(true))
    .arg(arg!(-r --restart "Legacy argument").hide(true).global(true))
    .arg(arg!(--nocolor "Legacy argument").hide(true).global(true))
    .ignore_errors(true)
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
    let mut args = Vec::new();
    let mut preserve = false;
    for arg in input_args {
        if preserve {
            args.push(arg);
        } else if arg == "--" {
            args.push("--".to_string());
            preserve = true;
        } else if arg.eq_ignore_ascii_case("--processStartAndWait") {
            args.push("--processStart".to_string());
            args.push("--wait".to_string());
        } else if arg.starts_with("--processStartAndWait=") {
            let mut split_arg = arg.splitn(2, '=');
            split_arg.next(); // Skip the `--processStartAndWait` part
            args.push("--processStart".to_string());
            args.push("--wait".to_string());
            if let Some(rest) = split_arg.next() {
                args.push(rest.to_string());
            }
        } else if arg.contains('=') {
            let mut split_arg = arg.splitn(2, '=');
            args.push(split_arg.next().unwrap().to_string());
            args.push(split_arg.next().unwrap().to_string());
        } else {
            args.push(arg);
        }
    }
    root_command().get_matches_from(&args)
}

fn get_flag_or_false(matches: &ArgMatches, id: &str) -> bool {
    // matches.get_flag throws when used with ignore_errors when any unknown arg is encountered and the flag is not present
    matches.try_get_one::<bool>(id).unwrap_or(None).map(|x| x.to_owned()).unwrap_or(false)
}

fn get_op_wait(matches: &ArgMatches) -> shared::OperationWait {
    let wait_for_parent = get_flag_or_false(&matches, "wait");
    let wait_pid = matches.try_get_one::<u32>("waitPid").unwrap_or(None).map(|v| v.to_owned());
    if wait_pid.is_some() {
        shared::OperationWait::WaitPid(wait_pid.unwrap())
    } else if wait_for_parent {
        shared::OperationWait::WaitParent
    } else {
        shared::OperationWait::NoWait
    }
}

fn main() -> Result<()> {
    #[cfg(windows)]
    windows::mitigate::pre_main_sideload_mitigation();

    #[cfg(windows)]
    let matches = parse_command_line_matches(env::args().collect());
    #[cfg(unix)]
    let matches = root_command().get_matches();

    let (subcommand, subcommand_matches) = matches.subcommand().ok_or_else(|| anyhow!("No subcommand was used. Try `--help` for more information."))?;

    let verbose = get_flag_or_false(&matches, "verbose");
    let silent = get_flag_or_false(&matches, "silent");
    let log_file = matches.get_one("log");

    dialogs::set_silent(silent);
    let desired_log_file = log_file.cloned().unwrap_or(locator::default_log_location(LocationContext::IAmUpdateExe));
    logging::setup_logging("update", Some(&desired_log_file), true, verbose)?;
    
    // change working directory to the parent directory of the exe
    let mut containing_dir = env::current_exe()?;
    containing_dir.pop();
    env::set_current_dir(containing_dir)?;

    info!("--");
    info!("Starting Velopack Updater ({})", env!("NGBV_VERSION"));
    info!("    Location: {}", env::current_exe()?.to_string_lossy());
    info!("    CWD: {}", env::current_dir()?.to_string_lossy());
    info!("    Verbose: {}", verbose);
    info!("    Silent: {}", silent);
    info!("    Log File: {:?}", log_file);

    let result = match subcommand {
        #[cfg(target_os = "windows")]
        "uninstall" => uninstall(subcommand_matches).map_err(|e| anyhow!("Uninstall error: {}", e)),
        "start" => start(subcommand_matches).map_err(|e| anyhow!("Start error: {}", e)),
        "apply" => apply(subcommand_matches).map_err(|e| anyhow!("Apply error: {}", e)),
        "patch" => patch(subcommand_matches).map_err(|e| anyhow!("Patch error: {}", e)),
        _ => bail!("Unknown subcommand '{subcommand}'. Try `--help` for more information."),
    };

    if let Err(e) = result {
        error!("{}", e);
        return Err(e.into());
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

    velopack::delta::zstd_patch_single(old_file, patch_file, output_file)?;
    Ok(())
}

fn apply(matches: &ArgMatches) -> Result<()> {
    let restart = !get_flag_or_false(&matches, "norestart");
    let package = matches.get_one::<PathBuf>("package");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());
    let wait = get_op_wait(&matches);

    info!("Command: Apply");
    info!("    Restart: {:?}", restart);
    info!("    Wait: {:?}", wait);
    info!("    Package: {:?}", package);
    info!("    Exe Args: {:?}", exe_args);

    let locator = auto_locate_app_manifest(LocationContext::IAmUpdateExe)?;
    #[cfg(target_os = "windows")]
    let _mutex = shared::retry_io(|| windows::create_global_mutex(&locator.get_manifest_id()))?;
    commands::apply(&locator, restart, wait, package, exe_args, true)
}

fn start(matches: &ArgMatches) -> Result<()> {
    let legacy_args = matches.get_one::<String>("args");
    let exe_name = matches.get_one::<String>("EXE_NAME");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());
    let wait = get_op_wait(&matches);

    info!("Command: Start");
    info!("    Wait: {:?}", wait);
    info!("    Exe Name: {:?}", exe_name);
    info!("    Exe Args: {:?}", exe_args);
    if legacy_args.is_some() {
        info!("    Legacy Args: {:?}", legacy_args);
        warn!("Legacy args format is deprecated and will be removed in a future release. Please update your application to use the new format.");
    }

    let locator = auto_locate_app_manifest(LocationContext::IAmUpdateExe)?;
    #[cfg(target_os = "windows")]
    let _mutex = shared::retry_io(|| windows::create_global_mutex(&locator.get_manifest_id()))?;
    commands::start(&locator, wait, exe_name, exe_args, legacy_args)
}

#[cfg(target_os = "windows")]
fn uninstall(_matches: &ArgMatches) -> Result<()> {
    info!("Command: Uninstall");
    let locator = auto_locate_app_manifest(LocationContext::IAmUpdateExe)?;
    commands::uninstall(&locator, true)
}

#[cfg(target_os = "windows")]
#[test]
fn test_start_command_supports_legacy_commands() {
    fn get_start_args(matches: &ArgMatches) -> (bool, Option<&String>, Option<&String>, Option<Vec<&String>>) {
        let legacy_args = matches.get_one::<String>("args");
        let wait_for_parent = get_flag_or_false(&matches, "wait");
        let exe_name = matches.get_one::<String>("EXE_NAME");
        let exe_args: Option<Vec<&String>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.collect());
        return (wait_for_parent, exe_name, legacy_args, exe_args);
    }

    fn try_parse_command_line_matches(input_args: Vec<String>) -> Result<ArgMatches> {
        // Split the arguments manually to handle the legacy `--flag=value` syntax
        // Also, replace `--processStartAndWait` with `--processStart --wait`
        let mut args = Vec::new();
        let mut preserve = false;
        for arg in input_args {
            if preserve {
                args.push(arg);
            } else if arg == "--" {
                args.push("--".to_string());
                preserve = true;
            } else if arg.eq_ignore_ascii_case("--processStartAndWait") {
                args.push("--processStart".to_string());
                args.push("--wait".to_string());
            } else if arg.starts_with("--processStartAndWait=") {
                let mut split_arg = arg.splitn(2, '=');
                split_arg.next(); // Skip the `--processStartAndWait` part
                args.push("--processStart".to_string());
                args.push("--wait".to_string());
                if let Some(rest) = split_arg.next() {
                    args.push(rest.to_string());
                }
            } else if arg.contains('=') {
                let mut split_arg = arg.splitn(2, '=');
                args.push(split_arg.next().unwrap().to_string());
                args.push(split_arg.next().unwrap().to_string());
            } else {
                args.push(arg);
            }
        }
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

    let command = vec!["Update.exe", "--processStartAndWait=hello.exe", "--", "Foo=Bar"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, true);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, Some(vec![&"Foo=Bar".to_string()]));

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
