#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::Result;
use clap::{arg, value_parser, Command};
use std::{env, path::PathBuf};
use velopack::*;

fn main() -> Result<()> {
    #[cfg(windows)]
    windows::mitigate::pre_main_sideload_mitigation();

    let mut arg_config = Command::new("Setup")
        .about(format!("Velopack Setup ({}) installs applications.\nhttps://github.com/velopack/velopack", env!("NGBV_VERSION")))
        .arg(arg!(-s --silent "Hides all dialogs and answers 'yes' to all prompts"))
        .arg(arg!(-v --verbose "Print debug messages to console"))
        .arg(arg!(-l --log <FILE> "Enable file logging and set location").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(-t --installto <DIR> "Installation directory to install the application").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--nocolor "Disable colored output").hide(true));

    if cfg!(debug_assertions) {
        arg_config = arg_config.arg(arg!(-d --debug <FILE> "Debug mode, install from a nupkg file").required(false).value_parser(value_parser!(PathBuf)));
    }

    let matches = arg_config.get_matches();
    let silent = matches.get_flag("silent");
    let verbose = matches.get_flag("verbose");
    let debug = matches.get_one::<PathBuf>("debug");
    let logfile = matches.get_one::<PathBuf>("log");
    let installto = matches.get_one::<PathBuf>("installto");
    let nocolor = matches.get_flag("nocolor");

    shared::dialogs::set_silent(silent);
    logging::setup_logging("setup", logfile, true, verbose, nocolor)?;

    info!("Starting Velopack Setup ({})", env!("NGBV_VERSION"));
    info!("    Location: {:?}", std::env::current_exe()?);
    info!("    Silent: {}", silent);
    info!("    Verbose: {}", verbose);
    info!("    Log: {:?}", logfile);
    info!("    Install To: {:?}", installto);
    if cfg!(debug_assertions) {
        info!("    Debug: {:?}", debug);
    }

    // change working directory to the containing directory of the exe
    let mut containing_dir = env::current_exe()?;
    containing_dir.pop();
    env::set_current_dir(containing_dir)?;

    let res = commands::install(debug, installto);
    if let Err(e) = &res {
        error!("An error has occurred: {}", e);
        dialogs::show_error("Setup Error", None, format!("An error has occurred: {}", e).as_str());
    }

    res?;
    Ok(())
}
