#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

mod logging;
mod shared;
mod windows;

#[macro_use]
extern crate log;
extern crate simplelog;
#[macro_use]
extern crate lazy_static;

use shared::{bundle, dialogs};

use anyhow::{anyhow, bail, Result};
use clap::{arg, value_parser, Command};
use memmap2::Mmap;
use pretty_bytes_rust::pretty_bytes;
use std::{
    env,
    fs::{self, File},
    path::{Path, PathBuf},
    time::Duration,
};
use winsafe::{self as w, co};

fn main() -> Result<()> {
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
    logging::setup_logging(logfile, true, verbose, nocolor)?;
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED | co::COINIT::DISABLE_OLE1DDE)?;

    info!("Starting Velopack Setup ({})", env!("NGBV_VERSION"));
    info!("    Location: {:?}", std::env::current_exe()?);
    info!("    Silent: {}", silent);
    info!("    Verbose: {}", verbose);
    info!("    Log: {:?}", logfile);
    info!("    Install To: {:?}", installto);
    if cfg!(debug_assertions) {
        info!("    Debug: {:?}", debug);
    }

    let res = run(&debug, &installto);
    if let Err(e) = &res {
        error!("An error has occurred: {}", e);
        dialogs::show_error("Setup Error", None, format!("An error has occurred: {}", e).as_str());
    }

    res?;
    Ok(())
}

fn run(debug_pkg: &Option<&PathBuf>, install_to: &Option<&PathBuf>) -> Result<()> {
    let osinfo = os_info::get();
    info!("OS: {}, Arch={}", osinfo, osinfo.architecture().unwrap_or("unknown"));

    if !w::IsWindowsVersionOrGreater(6, 1, 1)? {
        bail!("This installer requires Windows 7 SP1 or later and cannot run.");
    }

    let file = File::open(env::current_exe()?)?;
    let mmap = unsafe { Mmap::map(&file)? };
    let pkg = bundle::load_bundle_from_mmap(&mmap, debug_pkg)?;
    info!("Bundle loaded successfully.");

    // find and parse nuspec
    info!("Reading package manifest...");
    let app = pkg.read_manifest()?;

    info!("Package manifest loaded successfully.");
    info!("    Package ID: {}", &app.id);
    info!("    Package Version: {}", &app.version);
    info!("    Package Title: {}", &app.title);
    info!("    Package Authors: {}", &app.authors);
    info!("    Package Description: {}", &app.description);
    info!("    Package Machine Architecture: {}", &app.machine_architecture);
    info!("    Package Runtime Dependencies: {}", &app.runtime_dependencies);

    let _mutex = windows::create_global_mutex(&app)?;

    if !windows::prerequisite::prompt_and_install_all_missing(&app, None)? {
        info!("Cancelling setup. Pre-requisites not installed.");
        return Ok(());
    }

    info!("Determining install directory...");
    let (root_path, root_is_default) = if install_to.is_some() {
        (install_to.unwrap().clone(), false)
    } else {
        let appdata = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::LocalAppData, co::KF::DONT_UNEXPAND, None)?;
        (Path::new(&appdata).join(&app.id), true)
    };

    // path needs to exist for future operations (disk space etc)
    if !root_path.exists() {
        shared::retry_io(|| fs::create_dir_all(&root_path))?;
    }

    let root_path_str = root_path.to_str().unwrap();
    info!("Installation Directory: {:?}", root_path_str);

    // do we have enough disk space?
    let (compressed_size, extracted_size) = pkg.calculate_size();
    let required_space = compressed_size + extracted_size + (50 * 1000 * 1000); // archive + velopack overhead
    let mut free_space: u64 = 0;
    w::GetDiskFreeSpaceEx(Some(&root_path_str), None, None, Some(&mut free_space))?;
    if free_space < required_space {
        bail!(
            "{} requires at least {} disk space to be installed. There is only {} available.",
            &app.title,
            pretty_bytes(required_space, None),
            pretty_bytes(free_space, None)
        );
    }

    info!("There is {} free space available at destination, this package requires {}.", pretty_bytes(free_space, None), pretty_bytes(required_space, None));

    // does this app support this OS / architecture?
    if !app.os_min_version.is_empty() && !windows::is_os_version_or_greater(&app.os_min_version)? {
        bail!("This application requires Windows {} or later.", &app.os_min_version);
    }

    if !app.machine_architecture.is_empty() && !windows::is_cpu_architecture_supported(&app.machine_architecture)? {
        bail!("This application ({}) does not support your CPU architecture.", &app.machine_architecture);
    }

    let mut root_path_renamed = String::new();
    // does the target directory exist and have files? (eg. already installed)
    if !shared::is_dir_empty(&root_path) {
        // the target directory is not empty, and not dead
        if !dialogs::show_overwrite_repair_dialog(&app, &root_path, root_is_default) {
            // user cancelled overwrite prompt
            error!("Directory exists, and user cancelled overwrite.");
            return Ok(());
        }

        shared::force_stop_package(&root_path)
            .map_err(|z| anyhow!("Failed to stop application ({}), please close the application and try running the installer again.", z))?;

        root_path_renamed = format!("{}_{}", root_path_str, shared::random_string(8));
        info!("Renaming existing directory to '{}' to allow rollback...", root_path_renamed);

        shared::retry_io(|| fs::rename(&root_path, &root_path_renamed)).map_err(|_| {
            anyhow!(
                "Failed to remove existing application directory, please close the application and try running the installer again. \
                If the issue persists, try uninstalling first via Programs & Features, or restarting your computer."
            )
        })?;
    }

    info!("Preparing and cleaning installation directory...");
    remove_dir_all::ensure_empty_dir(&root_path)?;

    info!("Reading splash image...");
    let splash_bytes = pkg.get_splash_bytes();
    let tx = windows::splash::show_splash_dialog(app.title.to_owned(), splash_bytes, true);
    let install_result = install_app(&pkg, &root_path, &tx);
    let _ = tx.send(windows::splash::MSG_CLOSE);

    if install_result.is_ok() {
        info!("Installation completed successfully!");
        if !root_path_renamed.is_empty() {
            info!("Removing rollback directory...");
            let _ = shared::retry_io(|| fs::remove_dir_all(&root_path_renamed));
        }
    } else {
        error!("Installation failed!");
        if !root_path_renamed.is_empty() {
            info!("Rolling back installation...");
            let _ = shared::force_stop_package(&root_path);
            let _ = shared::retry_io(|| fs::remove_dir_all(&root_path));
            let _ = shared::retry_io(|| fs::rename(&root_path_renamed, &root_path));
        }
        install_result?;
    }

    Ok(())
}

fn install_app(pkg: &bundle::BundleInfo, root_path: &PathBuf, tx: &std::sync::mpsc::Sender<i16>) -> Result<()> {
    info!("Starting installation!");

    let app = pkg.read_manifest()?;

    // all application paths
    let updater_path = app.get_update_path(root_path);
    let packages_path = app.get_packages_path(root_path);
    let current_path = app.get_current_path(root_path);
    let nupkg_path = app.get_target_nupkg_path(root_path);
    let main_exe_path = app.get_main_exe_path(root_path);

    info!("Extracting Update.exe...");
    let _ = pkg
        .extract_zip_predicate_to_path(|name| name.ends_with("Squirrel.exe"), updater_path)
        .map_err(|_| anyhow!("This installer is missing a critical binary (Update.exe). Please contact the application author."))?;
    let _ = tx.send(5);

    info!("Copying nupkg to packages directory...");
    shared::retry_io(|| fs::create_dir_all(&packages_path))?;
    pkg.copy_bundle_to_file(&nupkg_path)?;
    let _ = tx.send(10);

    pkg.extract_lib_contents_to_path(&current_path, |p| {
        let _ = tx.send(((p as f32) / 100.0 * 80.0 + 10.0) as i16);
    })?;

    if !Path::new(&main_exe_path).exists() {
        bail!("The main executable could not be found in the package. Please contact the application author.");
    }

    info!("Creating start menu shortcut...");
    let startmenu = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None)?;
    let lnk_path = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", &app.title));
    if let Err(e) = windows::create_lnk(&lnk_path.to_string_lossy(), &main_exe_path, &current_path) {
        warn!("Failed to create start menu shortcut: {}", e);
    }

    let ver_string = app.version.to_string();
    info!("Starting process install hook: \"{}\" --squirrel-install {}", &main_exe_path, &ver_string);
    let args = vec!["--squirrel-install", &ver_string];
    if let Err(e) = windows::run_process_no_console_and_wait(&main_exe_path, args, &current_path, Some(Duration::from_secs(30))) {
        let setup_name = format!("{} Setup {}", app.title, app.version);
        error!("Process install hook failed: {}", e);
        let _ = tx.send(windows::splash::MSG_CLOSE);
        dialogs::show_warn(
            &setup_name,
            None,
            format!("Installation has completed, but the application install hook failed ({}). It may not have installed correctly.", e).as_str(),
        );
    }

    let _ = tx.send(100);

    app.write_uninstall_entry(root_path)?;

    if !dialogs::get_silent() {
        info!("Starting app...");
        shared::start_package(&app, &root_path, None, Some("VELOPACK_FIRSTRUN"))?;
    }

    Ok(())
}
