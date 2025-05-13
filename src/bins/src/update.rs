#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::{anyhow, bail, Result};
use clap::{arg, value_parser, ArgAction, ArgMatches, Command};
use std::{env, path::PathBuf};
use velopack::locator::{auto_locate_app_manifest, LocationContext};
use velopack::logging::*;
use velopack_bins::{shared::OperationWait, *};

#[rustfmt::skip]
fn root_command() -> Command {
    let cmd = Command::new("Update")
    .version(env!("NGBV_VERSION"))
    .about(format!("Velopack Updater ({}) manages packages and installs updates.\nhttps://velopack.io", env!("NGBV_VERSION")))
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
        .about("Applies a series of delta bundles to a base file")
        .arg(arg!(--old <FILE> "Base / old file to apply the patch to").required(true).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--delta <FILE> "The delta bundle to apply to the base package").required(true).action(ArgAction::Append).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--output <FILE> "The file to create with the patch applied").required(true).value_parser(value_parser!(PathBuf)))
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

    #[cfg(target_os = "windows")]
    let cmd = cmd.subcommand(Command::new("update-self")
        .about("Copy the currently executing Update.exe into the default location.")
        .long_flag_alias("updateSelf")
        .hide(true)
    );
    cmd
}

#[cfg(target_os = "windows")]
fn try_parse_command_line_matches(input_args: Vec<String>) -> Result<ArgMatches> {
    // Split the arguments manually to handle the legacy `--flag=value` syntax
    // Also, replace `--processStartAndWait` with `--processStart --wait`
    let mut args = Vec::new();
    let mut preserve = false;
    let mut first = true;
    for arg in input_args {
        if preserve || first {
            args.push(arg);
            first = false;
        } else if arg == "--" {
            args.push("--".to_string());
            preserve = true;
        } else if arg.eq_ignore_ascii_case("--processStartAndWait") {
            args.push("--processStart".to_string());
            args.push("--wait".to_string());
        } else if arg.to_ascii_lowercase().starts_with("--processstartandwait=") {
            let mut split_arg = arg.splitn(2, '=');
            split_arg.next(); // Skip the `--processStartAndWait` part
            args.push("--processStart".to_string());
            args.push("--wait".to_string());
            if let Some(rest) = split_arg.next() {
                args.push(rest.to_string());
            }
        } else if arg.to_ascii_lowercase().starts_with("--processstart=") {
            let mut split_arg = arg.splitn(2, '=');
            split_arg.next(); // Skip the `--processStart` part
            args.push("--processStart".to_string());
            if let Some(rest) = split_arg.next() {
                args.push(rest.to_string());
            }
        } else {
            args.push(arg);
        }
    }
    Ok(root_command().try_get_matches_from(&args)?)
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

// fn main() -> Result<()> {
//     shared::cli_host::clap_run_main("Update", main_inner)
// }

fn main() -> Result<()> {
    #[cfg(windows)]
    windows::mitigate::pre_main_sideload_mitigation();

    #[cfg(windows)]
    let matches = try_parse_command_line_matches(env::args().collect())?;
    #[cfg(unix)]
    let matches = root_command().try_get_matches()?;

    let silent = get_flag_or_false(&matches, "silent");
    dialogs::set_silent(silent);

    let verbose = get_flag_or_false(&matches, "verbose");
    let log_file = matches.get_one("log");
    let desired_log_file = log_file.cloned().unwrap_or(default_logfile_path(LocationContext::IAmUpdateExe));
    init_logging("update", Some(&desired_log_file), true, verbose, None);

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

    let (subcommand, subcommand_matches) =
        matches.subcommand().ok_or_else(|| anyhow!("No known subcommand was used. Try `--help` for more information."))?;

    let result = match subcommand {
        #[cfg(target_os = "windows")]
        "uninstall" => uninstall(subcommand_matches).map_err(|e| anyhow!("Uninstall error: {}", e)),
        #[cfg(target_os = "windows")]
        "update-self" => update_self(subcommand_matches).map_err(|e| anyhow!("Update-self error: {}", e)),
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
    let old_file = matches.get_one::<PathBuf>("old");
    let deltas: Vec<&PathBuf> = matches.get_many::<PathBuf>("delta").unwrap_or_default().collect();
    let output_file = matches.get_one::<PathBuf>("output");

    info!("Command: Patch");
    info!("    Old File: {:?}", old_file);
    info!("    Delta Files: {:?}", deltas);
    info!("    Output File: {:?}", output_file);

    if old_file.is_none() || deltas.is_empty() || output_file.is_none() {
        bail!("Missing required arguments. Please provide --old, --delta, and --output.");
    }

    let temp_dir = match auto_locate_app_manifest(LocationContext::IAmUpdateExe) {
        Ok(locator) => locator.get_temp_dir_rand16(),
        Err(_) => {
            let mut temp_dir = std::env::temp_dir();
            let rand = shared::random_string(16);
            temp_dir.push("velopack_".to_owned() + &rand);
            temp_dir
        }
    };

    let result = commands::delta(old_file.unwrap(), deltas, &temp_dir, output_file.unwrap());
    let _ = remove_dir_all::remove_dir_all(temp_dir);

    if let Err(e) = result {
        bail!("Delta error: {}", e);
    }
    Ok(())
}

fn get_exe_args(matches: &ArgMatches) -> Option<Vec<&str>> {
    matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect())
}

fn get_apply_args(matches: &ArgMatches) -> (OperationWait, bool, Option<&PathBuf>, Option<Vec<&str>>) {
    let restart = !get_flag_or_false(&matches, "norestart");
    let package = matches.get_one::<PathBuf>("package");
    let exe_args = get_exe_args(matches);
    let wait = get_op_wait(&matches);
    (wait, restart, package, exe_args)
}

fn apply(matches: &ArgMatches) -> Result<()> {
    let (wait, restart, package, exe_args) = get_apply_args(matches);
    info!("Command: Apply");
    info!("    Restart: {:?}", restart);
    info!("    Wait: {:?}", wait);
    info!("    Package: {:?}", package);
    info!("    Exe Args: {:?}", exe_args);

    let locator = auto_locate_app_manifest(LocationContext::IAmUpdateExe)?;
    let _mutex = locator.try_get_exclusive_lock()?;
    let _ = commands::apply(&locator, restart, wait, package, exe_args, true)?;
    Ok(())
}

fn get_start_args(matches: &ArgMatches) -> (OperationWait, Option<&String>, Option<&String>, Option<Vec<&str>>) {
    let legacy_args = matches.get_one::<String>("args");
    let exe_name = matches.get_one::<String>("EXE_NAME");
    let exe_args = get_exe_args(matches);
    let wait = get_op_wait(&matches);
    (wait, exe_name, legacy_args, exe_args)
}

fn start(matches: &ArgMatches) -> Result<()> {
    let (wait, exe_name, legacy_args, exe_args) = get_start_args(matches);

    info!("Command: Start");
    info!("    Wait: {:?}", wait);
    info!("    Exe Name: {:?}", exe_name);
    info!("    Exe Args: {:?}", exe_args);
    if legacy_args.is_some() {
        info!("    Legacy Args: {:?}", legacy_args);
        warn!("Legacy args format is deprecated and will be removed in a future release. Please update your application to use the new format.");
    }

    commands::start(wait, exe_name, exe_args, legacy_args)
}

#[cfg(target_os = "windows")]
fn uninstall(_matches: &ArgMatches) -> Result<()> {
    info!("Command: Uninstall");
    let locator = auto_locate_app_manifest(LocationContext::IAmUpdateExe)?;
    commands::uninstall(&locator, true)
}

#[cfg(target_os = "windows")]
fn update_self(_matches: &ArgMatches) -> Result<()> {
    info!("Command: Update Self");
    let my_path = env::current_exe()?;
    const RETRY_DELAY: i32 = 500;
    const RETRY_COUNT: i32 = 20;
    match auto_locate_app_manifest(LocationContext::IAmUpdateExe) {
        Ok(locator) => {
            let target_update_path = locator.get_update_path();
            if same_file::is_same_file(&target_update_path, &my_path)? {
                bail!("Update.exe is already in the default location. No need to update.");
            } else {
                info!("Copying Update.exe to the default location: {:?}", target_update_path);
                shared::retry_io_ex(|| std::fs::copy(&my_path, &target_update_path), RETRY_DELAY, RETRY_COUNT)?;
                info!("Update.exe copied successfully.");
            }
        }
        Err(e) => {
            warn!("Failed to initialise locator: {}", e);
            // search for an Update.exe in parent directories (at least 2 levels up)
            let mut current_dir = env::current_dir()?;
            let mut found = false;
            for _ in 0..2 {
                current_dir.pop();
                let target_update_path = current_dir.join("Update.exe");
                if target_update_path.exists() {
                    info!("Found Update.exe in parent directory: {:?}", target_update_path);
                    shared::retry_io_ex(|| std::fs::copy(&my_path, &target_update_path), RETRY_DELAY, RETRY_COUNT)?;
                    info!("Update.exe copied successfully.");
                    found = true;
                    break;
                }
            }
            if !found {
                bail!("Failed to locate Update.exe in parent directories, so it could not be updated.");
            }
        }
    }
    Ok(())
}

#[cfg(target_os = "windows")]
#[test]
fn test_cli_parse_handles_equals_spaces() {
    let command = vec!["C:\\Some Path\\With = Spaces\\Update.exe", "apply", "--package", "C:\\Some Path\\With = Spaces\\Package.zip"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait, restart, package, exe_args) = get_apply_args(matches.subcommand_matches("apply").unwrap());

    assert_eq!(wait, OperationWait::NoWait);
    assert_eq!(restart, true);
    assert_eq!(package, Some(&PathBuf::from("C:\\Some Path\\With = Spaces\\Package.zip")));
    assert_eq!(exe_args, None);
}

#[cfg(target_os = "windows")]
#[test]
fn test_start_command_supports_legacy_commands() {
    let command = vec!["Update.exe", "--processStart=hello.exe"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::NoWait);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStart", "hello.exe"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::NoWait);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait=hello.exe"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait=hello.exe", "--", "Foo=Bar"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, None);
    assert_eq!(exe_args, Some(vec!["Foo=Bar"]));

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "-a", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "-a", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "--processStartArgs", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "hello.exe", "--process-start-args", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, Some(&"hello.exe".to_string()));
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "-a", "myarg"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, None);
    assert_eq!(legacy_args, Some(&"myarg".to_string()));
    assert_eq!(exe_args, None);

    let command = vec!["Update.exe", "--processStartAndWait", "-a", "-- -c \" asda --aasd"];
    let matches = try_parse_command_line_matches(command.iter().map(|s| s.to_string()).collect()).unwrap();
    let (wait_for_parent, exe_name, legacy_args, exe_args) = get_start_args(matches.subcommand_matches("start").unwrap());
    assert_eq!(wait_for_parent, OperationWait::WaitParent);
    assert_eq!(exe_name, None);
    assert_eq!(legacy_args, Some(&"-- -c \" asda --aasd".to_string()));
    assert_eq!(exe_args, None);
}
