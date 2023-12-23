#![allow(dead_code)]

mod bundle;
mod platform;
mod util;

#[macro_use]
extern crate lazy_static;

#[macro_use]
extern crate log;
extern crate simplelog;

use anyhow::{anyhow, bail, Result};
use bundle::Manifest;
use clap::{arg, value_parser, ArgMatches, Command};
use glob::glob;
use std::fs::File;
use std::path::Path;
use std::time::Duration;
use std::{env, fs, path::PathBuf};

use crate::bundle::BundleInfo;

#[rustfmt::skip]
fn root_command() -> Command {
    Command::new("Update")
    .version(env!("NGBV_VERSION"))
    .about(format!("Clowd.Squirrel Updater ({}) manages packages and installs updates for Squirrel applications.\nhttps://github.com/clowd/Clowd.Squirrel", env!("NGBV_VERSION")))
    .subcommand(Command::new("apply")
        .about("Applies a staged / prepared update, installing prerequisite runtimes if necessary")
        .arg(arg!(-r --restart "Restart the application after the update"))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before applying the update"))
        .arg(arg!(-p --package <FILE> "Update package to apply").value_parser(value_parser!(PathBuf)))
        .arg(arg!([EXE_NAME] "The optional name of the binary to execute"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceeded by '--'.").required(false).last(true).num_args(0..))
    )
    .subcommand(Command::new("start")
        .about("Starts the currently installed version of the application")
        .arg(arg!(-a --args <ARGS> "Legacy args format").aliases(vec!["processStartArgs", "process-start-args"]).hide(true).allow_hyphen_values(true).num_args(1))
        .arg(arg!(-w --wait "Wait for the parent process to terminate before starting the application"))
        .arg(arg!([EXE_NAME] "The optional name of the binary to execute"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceeded by '--'.").required(false).last(true).num_args(0..))
        .long_flag_aliases(vec!["processStart", "processStartAndWait"])
    )
    .subcommand(Command::new("uninstall")
        .about("Remove all app shortcuts, files, and registry entries.")
        .long_flag_alias("uninstall")
    )
    .arg(arg!(--verbose "Print debug messages to console / log"))
    .arg(arg!(--nocolor "Disable colored output").hide(true))
    .arg(arg!(-s --silent "Don't show any prompts / dialogs"))
    .arg(arg!(-l --log <PATH> "Override the default log file location").value_parser(value_parser!(PathBuf)))
    .disable_help_subcommand(true)
    .flatten_help(true)
}

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
    let matches = parse_command_line_matches(env::args().collect());

    let default_log_file = {
        let mut my_dir = env::current_exe().unwrap();
        my_dir.pop();
        my_dir.join("Clowd.Squirrel.log")
    };

    let verbose = matches.get_flag("verbose");
    let silent = matches.get_flag("silent");
    let nocolor = matches.get_flag("nocolor");
    let log_file = matches.get_one("log").unwrap_or(&default_log_file);

    platform::set_silent(silent);
    util::setup_logging(Some(&log_file), true, verbose, nocolor)?;

    info!("Starting Clowd.Squirrel Updater ({})", env!("NGBV_VERSION"));
    info!("    Location: {}", env::current_exe()?.to_string_lossy());
    info!("    Verbose: {}", verbose);
    info!("    Silent: {}", silent);
    info!("    Log File: {}", log_file.to_string_lossy());

    let (subcommand, subcommand_matches) = matches.subcommand().ok_or_else(|| anyhow!("No subcommand was used. Try `--help` for more information."))?;
    let result = match subcommand {
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

fn get_my_root_dir() -> Result<PathBuf> {
    // Ok(Path::new(r"C:\Source\rust setup testing\install").to_path_buf())
    let mut my_dir = env::current_exe()?;
    my_dir.pop();
    Ok(my_dir)
}

fn start(matches: &ArgMatches) -> Result<()> {
    // handle legacy arg syntax
    let legacy_args = matches.get_one::<String>("args");
    let wait_for_parent = matches.get_flag("wait");
    let exe_name = matches.get_one::<String>("EXE_NAME");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Command: Start");
    info!("    Wait: {:?}", wait_for_parent);
    info!("    Exe Name: {:?}", exe_name);
    info!("    Exe Args: {:?}", exe_args);

    _start(wait_for_parent, exe_name, exe_args, legacy_args)?;
    Ok(())
}

fn _start(wait_for_parent: bool, exe_name: Option<&String>, exe_args: Option<Vec<&str>>, legacy_args: Option<&String>) -> Result<()> {
    if legacy_args.is_some() {
        info!("    Legacy Args: {:?}", legacy_args);
        warn!("Legacy args format is deprecated and will be removed in a future release. Please update your application to use the new format.");
    }

    if legacy_args.is_some() && exe_args.is_some() {
        bail!("Cannot use both legacy args and new args format.");
    }

    if wait_for_parent {
        platform::wait_for_parent_to_exit(60_000)?; // 1 minute
    }

    let (root_path, app) = init_root()?;

    let current = app.get_current_path(&root_path);
    let exe_to_execute = if let Some(exe) = exe_name {
        Path::new(&current).join(exe)
    } else {
        let exe = app.get_main_exe_path(&root_path);
        Path::new(&exe).to_path_buf()
    };

    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{}'", exe_to_execute.to_string_lossy());
    }

    platform::assert_can_run_binary_authenticode(&exe_to_execute)?;

    info!("About to launch: '{}' in dir '{}'", exe_to_execute.to_string_lossy(), current);

    if let Some(args) = exe_args {
        platform::run_process(exe_to_execute, args, current)?;
    } else if let Some(args) = legacy_args {
        platform::run_process_raw_args(exe_to_execute, args, current)?;
    } else {
        platform::run_process(exe_to_execute, vec![], current)?;
    };

    Ok(())
}

fn apply<'a>(matches: &ArgMatches) -> Result<()> {
    let restart = matches.get_flag("restart");
    let wait_for_parent = matches.get_flag("wait");
    let package = matches.get_one::<PathBuf>("package");
    let exe_name = matches.get_one::<String>("EXE_NAME");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Command: Apply");
    info!("    Restart: {:?}", restart);
    info!("    Wait: {:?}", wait_for_parent);
    info!("    Package: {:?}", package);
    info!("    Exe Name: {:?}", exe_name);
    info!("    Exe Args: {:?}", exe_args);

    if wait_for_parent {
        let _ = platform::wait_for_parent_to_exit(60_000); // 1 minute
    }

    if let Err(e) = apply_package(package) {
        error!("Error applying package: {}", e);
        if !restart {
            return Err(e);
        }
    }

    if restart {
        _start(false, exe_name, exe_args, None)?;
    }

    Ok(())
}

fn apply_package<'a>(package: Option<&PathBuf>) -> Result<()> {
    let mut package_manifest: Option<Manifest> = None;
    let mut package_bundle: Option<BundleInfo<'a>> = None;

    let (root_path, app) = init_root()?;

    if let Some(pkg) = package {
        info!("Loading package from argument '{}'.", pkg.to_string_lossy());
        let bun = bundle::load_bundle_from_file(&pkg)?;
        package_manifest = Some(bun.read_manifest()?);
        package_bundle = Some(bun);
    } else {
        info!("No package specified, searching for latest.");
        let packages_dir = app.get_packages_path(&root_path);
        if let Ok(paths) = glob(format!("{}/*.nupkg", packages_dir).as_str()) {
            for path in paths {
                if let Ok(path) = path {
                    trace!("Checking package: '{}'", path.to_string_lossy());
                    if let Ok(bun) = bundle::load_bundle_from_file(&path) {
                        if let Ok(mani) = bun.read_manifest() {
                            if package_manifest.is_none() || mani.version > package_manifest.clone().unwrap().version {
                                info!("Found {}: '{}'", mani.version, path.to_string_lossy());
                                package_manifest = Some(mani);
                                package_bundle = Some(bun);
                            }
                        }
                    }
                }
            }
        }
    }

    if package_manifest.is_none() || package_bundle.is_none() {
        bail!("Unable to find/load suitable package.");
    }

    let package_manifest = package_manifest.unwrap();

    let found_version = package_manifest.clone().version;
    if found_version <= app.version {
        bail!("Latest package found is {}, which is not newer than current version {}.", found_version, app.version);
    }

    info!("Applying package to current: {}", found_version);

    run_hook(&app, &root_path, "--squirrel-obsoleted", 15);

    let current_dir = app.get_current_path(&root_path);
    platform::replace_dir_with_rollback(current_dir.clone(), || {
        if let Some(bundle) = package_bundle.take() {
            bundle.extract_lib_contents_to_path(&current_dir, |_| {})
        } else {
            bail!("No bundle could be loaded.");
        }
    })?;

    run_hook(&package_manifest, &root_path, "--squirrel-updated", 15);

    info!("Package applied successfully.");
    Ok(())
}

fn init_root() -> Result<(PathBuf, Manifest)> {
    let root_path = get_my_root_dir()?;
    let app = find_manifest_from_root_dir(&root_path)
        .map_err(|m| anyhow!("Unable to read application manifest ({}). Is this a properly installed application?", m))?;
    info!("Loaded manifest for application: {}", app.id);
    info!("Root Directory: {}", root_path.to_string_lossy());
    Ok((root_path, app))
}

fn run_hook(app: &Manifest, root_path: &PathBuf, hook_name: &str, timeout_secs: u64) {
    let sw = simple_stopwatch::Stopwatch::start_new();
    let current_path = app.get_current_path(&root_path);
    let main_exe_path = app.get_main_exe_path(&root_path);
    info!("Running {} hook...", hook_name);
    let ver_string = app.version.to_string();
    let args = vec![hook_name, &ver_string];
    if let Err(e) = platform::run_process_no_console_and_wait(&main_exe_path, args, &current_path, Some(Duration::from_secs(timeout_secs))) {
        warn!("Error running hook {}: {} (took {}ms)", hook_name, e, sw.ms());
    }
    info!("Hook executed successfully (took {}ms)", sw.ms());
    // in case the uninstall hook left running processes
    let _ = platform::kill_processes_in_directory(&root_path);
}

fn uninstall(_matches: &ArgMatches, log_file: &PathBuf) -> Result<()> {
    info!("Command: Uninstall");
    let (root_path, app) = init_root()?;

    fn _uninstall_impl(app: &Manifest, root_path: &PathBuf) -> bool {
        // the real app could be running at the moment
        let _ = platform::kill_processes_in_directory(&root_path);

        let mut finished_with_errors = false;

        // run uninstall hook
        run_hook(&app, root_path, "--squirrel-uninstall", 60);

        if let Err(e) = platform::remove_all_shortcuts_for_root_dir(&root_path) {
            error!("Unable to remove shortcuts ({}).", e);
            // finished_with_errors = true;
        }

        info!("Removing directory '{}'", root_path.to_string_lossy());
        if let Err(e) = util::retry_io(|| remove_dir_all::remove_dir_containing_current_executable()) {
            error!("Unable to remove directory, some files may be in use ({}).", e);
            finished_with_errors = true;
        }

        if let Err(e) = app.remove_uninstall_entry() {
            error!("Unable to remove uninstall registry entry ({}).", e);
            // finished_with_errors = true;
        }

        !finished_with_errors
    }

    // if it returns true, it was a success.
    // if it returns false, it was completed with errors which the user should be notified of.
    let result = _uninstall_impl(&app, &root_path);

    if result {
        info!("Finished successfully.");
        platform::show_info("The application was successfully uninstalled.", format!("{} Uninstall", app.title));
    } else {
        error!("Finished with errors.");
        platform::show_uninstall_complete_with_errors_dialog(&app, &log_file);
    }

    let dead_path = root_path.join(".dead");
    let _ = File::create(dead_path);
    if let Err(e) = platform::register_intent_to_delete_self(5, &root_path) {
        warn!("Unable to schedule self delete ({}).", e);
    }

    Ok(())
}

fn find_manifest_from_root_dir(root_path: &PathBuf) -> Result<Manifest> {
    // default to checking current/sq.version
    let cm = find_current_manifest(root_path);
    if cm.is_ok() {
        return cm;
    }

    // if that fails, check for latest full package
    let latest = find_latest_full_package(root_path);
    if let Some(latest) = latest {
        let mani = latest.load_manifest()?;
        return Ok(mani);
    }

    bail!("Unable to locate manifest or package.");
}

fn find_current_manifest(root_path: &PathBuf) -> Result<Manifest> {
    let m = bundle::Manifest::default();
    let nuspec_path = m.get_nuspec_path(root_path);
    if Path::new(&nuspec_path).exists() {
        if let Ok(nuspec) = util::retry_io(|| fs::read_to_string(&nuspec_path)) {
            return Ok(bundle::read_manifest_from_string(&nuspec)?);
        }
    }
    bail!("Unable to read nuspec file in current directory.")
}

fn find_latest_full_package(root_path: &PathBuf) -> Option<bundle::EntryNameInfo> {
    let packages = get_all_packages(root_path);
    let mut latest: Option<bundle::EntryNameInfo> = None;
    for pkg in packages {
        if pkg.is_delta {
            continue;
        }
        if latest.is_none() {
            latest = Some(pkg);
        } else {
            let latest_ver = latest.clone().unwrap().version;
            if pkg.version > latest_ver {
                latest = Some(pkg);
            }
        }
    }
    latest
}

fn get_all_packages(root_path: &PathBuf) -> Vec<bundle::EntryNameInfo> {
    let m = bundle::Manifest::default();
    let packages = m.get_packages_path(root_path);
    let mut vec = Vec::new();
    debug!("Scanning for packages in {:?}", packages);
    if let Ok(entries) = fs::read_dir(packages) {
        for entry in entries {
            if let Ok(entry) = entry {
                if let Some(pkg) = bundle::parse_package_file_path(entry.path()) {
                    debug!("Found package: {}", entry.path().to_string_lossy());
                    vec.push(pkg);
                }
            }
        }
    }
    vec
}

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
