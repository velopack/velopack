#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::{bail, Result};
use clap::{arg, value_parser, Command};
use memmap2::Mmap;
use std::fs::File;
use std::path::Path;
use std::{env, path::PathBuf};
use velopack::bundle::BundleZip;
use velopack_bins::*;

#[used]
#[no_mangle]
static BUNDLE_PLACEHOLDER: [u8; 48] = [
    0, 0, 0, 0, 0, 0, 0, 0, // 8 bytes for package offset
    0, 0, 0, 0, 0, 0, 0, 0, // 8 bytes for package length
    0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29, // 32 bytes for bundle signature
    0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4, //
    0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5, //
    0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d, //
];

#[inline(never)]
pub fn header_offset_and_length() -> (i64, i64) {
    use core::ptr;
    // Perform volatile reads to avoid optimization issues
    // TODO: refactor to use little-endian, also need to update the writer in dotnet
    unsafe {
        let offset = i64::from_ne_bytes([
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[0]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[1]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[2]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[3]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[4]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[5]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[6]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[7]),
        ]);
        let length = i64::from_ne_bytes([
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[8]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[9]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[10]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[11]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[12]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[13]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[14]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[15]),
        ]);
        (offset, length)
    }
}

fn main() -> Result<()> {
    windows::mitigate::pre_main_sideload_mitigation();
    shared::cli_host::clap_run_main("Setup", main_inner)
}

fn main_inner() -> Result<()> {
    #[rustfmt::skip]
    let mut arg_config = Command::new("Setup")
        .about(format!("Velopack Setup ({}) installs applications.\nhttps://velopack.io", env!("NGBV_VERSION")))
        .arg(arg!(-s --silent "Hides all dialogs and answers 'yes' to all prompts"))
        .arg(arg!(-v --verbose "Print debug messages to console"))
        .arg(arg!(-l --log <FILE> "Enable file logging and set location").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(-t --installto <DIR> "Installation directory to install the application").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(-b --bootstrap "Just apply install files, do not write uninstall registry keys"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceded by '--'.").required(false).last(true).num_args(0..));

    if cfg!(debug_assertions) {
        arg_config = arg_config
            .arg(arg!(-d --debug <FILE> "Debug mode, install from a nupkg file").required(false).value_parser(value_parser!(PathBuf)));
    }

    if let Err(e) = run_inner(arg_config) {
        let error_string = format!("An error has occurred: {:?}", e);
        if let Ok(downcast) = e.downcast::<clap::Error>() {
            let output_string = downcast.to_string();
            match downcast.kind() {
                clap::error::ErrorKind::DisplayHelp => {
                    println!("{output_string}");
                    return Ok(());
                }
                clap::error::ErrorKind::DisplayHelpOnMissingArgumentOrSubcommand => {
                    println!("{output_string}");
                    return Ok(());
                }
                clap::error::ErrorKind::DisplayVersion => {
                    println!("{output_string}");
                    return Ok(());
                }
                _ => {}
            }
        }
        error!("{}", error_string);
        dialogs::show_error("Setup Error", None, &error_string);
    }

    Ok(())
}

fn run_inner(arg_config: Command) -> Result<()> {
    let matches = arg_config.try_get_matches()?;

    let silent = matches.get_flag("silent");
    dialogs::set_silent(silent);

    let verbose = matches.get_flag("verbose");
    let logfile = matches.get_one::<PathBuf>("log");
    velopack::logging::init_logging("setup", logfile, true, verbose, None);

    let debug = matches.get_one::<PathBuf>("debug");
    let install_to = matches.get_one::<PathBuf>("installto");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Starting Velopack Setup ({})", env!("NGBV_VERSION"));
    info!("    Location: {:?}", env::current_exe()?);
    info!("    Silent: {}", silent);
    info!("    Verbose: {}", verbose);
    info!("    Log: {:?}", logfile);
    info!("    Install To: {:?}", install_to);
    if cfg!(debug_assertions) {
        info!("    Debug: {:?}", debug);
    }

    // change working directory to the containing directory of the exe
    let mut containing_dir = env::current_exe()?;
    containing_dir.pop();
    env::set_current_dir(containing_dir)?;

    // load the bundle which is embedded or if missing from the debug nupkg path
    let osinfo = os_info::get();
    let osarch = shared::runtime_arch::RuntimeArch::from_current_system();
    info!("OS: {osinfo}, Arch={osarch:#?}");

    if !windows::is_windows_7_sp1_or_greater() {
        bail!("This installer requires Windows 7 SPA1 or later and cannot run.");
    }

    let file = File::open(env::current_exe()?)?;
    let mmap = unsafe { Mmap::map(&file)? };
    let mut bundle: Option<BundleZip> = None;

    // in debug mode only, allow a nupkg to be passed in as the first argument
    if cfg!(debug_assertions) {
        if let Some(pkg) = debug {
            info!("Loading bundle from DEBUG nupkg file {:?}...", pkg);
            bundle = Some(velopack::bundle::load_bundle_from_file(pkg)?);
        }
    }

    info!("Reading bundle header...");
    let (offset, length) = header_offset_and_length();
    info!("Bundle offset = {}, length = {}", offset, length);

    // try to load the bundle from embedded zip
    if offset > 0 && length > 0 {
        bundle = Some(velopack::bundle::load_bundle_from_memory(&mmap[offset as usize..(offset + length) as usize])?);
    }

    if bundle.is_none() {
        bail!("Could not find embedded zip file. Please contact the application author.");
    }

    let mut bundle = bundle.unwrap();

    info!("Reading package manifest...");
    let app = bundle.read_manifest()?;

    info!("Determining install directory...");
    let (root_path, root_is_default) = if install_to.is_some() {
        (install_to.unwrap().clone(), false)
    } else {
        let appdata = windows::known_path::get_local_app_data()?;
        (Path::new(&appdata).join(&app.id), true)
    };

    let bar = root_path.parent();
    info!("Parent dir: {:?}", bar);

    if let Some(parent_dir) = root_path.parent() {
        info!("Checking if directory is writable: {:?}", parent_dir);
        if !windows::is_directory_writable(parent_dir) {
            if windows::process::is_process_elevated() {
                bail!("The installation directory is not writable & process is already admin. Please select a different directory.");
            }

            // re-launch as admin
            info!("Re-launching as administrator to install to {:?}", root_path);

            let mut args: Vec<String> = Vec::new();
            if silent {
                args.push("--silent".to_string());
            }

            if verbose {
                args.push("--verbose".to_string());
            }

            if let Some(debug) = debug {
                args.push("--debug".to_string());
                args.push(debug.to_string_lossy().to_string());
            }

            if let Some(logfile) = logfile {
                args.push("--log".to_string());
                args.push(logfile.to_string_lossy().to_string());
            }

            if let Some(install_to) = install_to {
                args.push("--installto".to_string());
                args.push(install_to.to_string_lossy().to_string());
            }

            if let Some(exe_args) = exe_args {
                args.push("--".to_string());
                for arg in exe_args {
                    args.push(arg.to_string());
                }
            }

            windows::process::relaunch_self_as_admin(args)?;
            info!("Successfully re-launched as administrator.");
            return Ok(());
        }
    }

    commands::install(&mut bundle, (root_path, root_is_default), exe_args)?;
    Ok(())
}
