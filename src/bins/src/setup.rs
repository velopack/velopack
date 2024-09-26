#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::{bail, Result};
use clap::{arg, value_parser, Command};
use memmap2::Mmap;
use std::cell::RefCell;
use std::fs::File;
use std::io::Cursor;
use std::rc::Rc;
use std::{env, path::PathBuf};
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

#[used]
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

    #[rustfmt::skip]
    let mut arg_config = Command::new("Setup")
        .about(format!("Velopack Setup ({}) installs applications.\nhttps://github.com/velopack/velopack", env!("NGBV_VERSION")))
        .arg(arg!(-s --silent "Hides all dialogs and answers 'yes' to all prompts"))
        .arg(arg!(-v --verbose "Print debug messages to console"))
        .arg(arg!(-l --log <FILE> "Enable file logging and set location").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(-t --installto <DIR> "Installation directory to install the application").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceded by '--'.").required(false).last(true).num_args(0..));

    if cfg!(debug_assertions) {
        arg_config = arg_config
            .arg(arg!(-d --debug <FILE> "Debug mode, install from a nupkg file").required(false).value_parser(value_parser!(PathBuf)));
    }

    let res = run_inner(arg_config);
    if let Err(e) = &res {
        error!("An error has occurred: {}", e);
        dialogs::show_error("Setup Error", None, format!("An error has occurred: {}", e).as_str());
    }
    
    Ok(())
}

fn run_inner(arg_config: Command) -> Result<()>
{
    let matches = arg_config.try_get_matches()?;
    
    let silent = matches.get_flag("silent");
    let verbose = matches.get_flag("verbose");
    let debug = matches.get_one::<PathBuf>("debug");
    let logfile = matches.get_one::<PathBuf>("log");
    let install_to = matches.get_one::<PathBuf>("installto");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    dialogs::set_silent(silent);
    logging::setup_logging("setup", logfile, true, verbose)?;

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

    // in debug mode only, allow a nupkg to be passed in as the first argument
    if cfg!(debug_assertions) {
        if let Some(pkg) = debug {
            info!("Loading bundle from DEBUG nupkg file {:?}...", pkg);
            let mut bundle = velopack::bundle::load_bundle_from_file(pkg)?;
            commands::install(&mut bundle, install_to, exe_args)?;
            return Ok(())
        }
    }

    info!("Reading bundle header...");
    let (offset, length) = header_offset_and_length();
    info!("Bundle offset = {}, length = {}", offset, length);

    // try to load the bundle from embedded zip
    if offset > 0 && length > 0 {
        info!("Loading bundle from embedded zip...");
        let file = File::open(env::current_exe()?)?;
        let mmap = unsafe { Mmap::map(&file)? };
        let zip_range: &[u8] = &mmap[offset as usize..(offset + length) as usize];
        let mut bundle = velopack::bundle::load_bundle_from_memory(&zip_range)?;
        commands::install(&mut bundle, install_to, exe_args)?;
        return Ok(())
    }

    bail!("Could not find embedded zip file. Please contact the application author.");
}

