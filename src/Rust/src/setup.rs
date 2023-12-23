#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

mod bundle;
mod download;
mod runtimes;
mod splash;
mod util;
mod platform;

#[macro_use]
extern crate lazy_static;

#[macro_use]
extern crate log;
extern crate simplelog;

use anyhow::{anyhow, bail, Result};
use clap::{arg, value_parser, Command};
use memmap2::Mmap;
use pretty_bytes_rust::pretty_bytes;
use regex::Regex;
use std::{
    env,
    fs::{self, File},
    path::{Path, PathBuf},
    time::Duration,
};
use winsafe::{self as w, co};

fn main() -> Result<()> {
    let mut arg_config = Command::new("Setup")
        .about(format!("Clowd.Squirrel Setup ({}) installs Squirrel applications.\nhttps://github.com/clowd/Clowd.Squirrel", env!("CARGO_PKG_VERSION")))
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

    platform::set_silent(silent);
    util::setup_logging(logfile, true, verbose, nocolor)?;

    info!("Starting Clowd.Squirrel Setup ({})", env!("CARGO_PKG_VERSION"));
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
        platform::show_error(format!("An error has occurred: {}", e), "Setup Error".to_string());
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

    let mutex_name = format!("clowdsquirrel-{}", &app.id);
    info!("Attempting to open global system mutex: '{}'", &mutex_name);
    let _mutex = platform::create_global_mutex(&mutex_name)?;

    info!("Checking application pre-requisites...");
    let dependencies = runtimes::parse_dependency_list(&app.runtime_dependencies);
    let mut missing: Vec<&Box<dyn runtimes::RuntimeInfo>> = Vec::new();
    let mut missing_str = String::new();

    for i in 0..dependencies.len() {
        let dep = &dependencies[i];
        if dep.is_installed() {
            info!("    {} is already installed.", dep.display_name());
            continue;
        }
        info!("    {} is missing.", dep.display_name());
        if !missing.is_empty() {
            missing_str += ", ";
        }
        missing.push(dep);
        missing_str += dep.display_name();
    }

    let splash_bytes = pkg.get_splash_bytes();

    if !missing.is_empty() {
        if !platform::show_missing_dependencies_dialog(&app, &missing_str) {
            error!("User cancelled pre-requisite installation.");
            return Ok(());
        }

        let downloads = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Downloads, co::KF::DONT_UNEXPAND, None)?;
        let downloads = Path::new(downloads.as_str());

        info!("Downloading {} missing pre-requisites...", missing.len());
        let quiet = platform::get_silent();

        for i in 0..missing.len() {
            let dep = &missing[i];
            let url = dep.get_download_url()?;
            let exe_name = downloads.join(dep.get_exe_name());

            if !exe_name.exists() {
                let tx = splash::show_splash_in_new_thread(app.title.to_owned(), splash_bytes.clone(), true);
                info!("    Downloading {}...", dep.display_name());
                let result = download::download_url_to_file(&url, &exe_name.to_str().unwrap(), |p| {
                    let _ = tx.send(p);
                });
                let _ = tx.send(splash::MSG_CLOSE);
                result?;
            }

            info!("    Installing {}...", dep.display_name());
            let result = dep.install(exe_name.to_str().unwrap(), quiet)?;
            if result == runtimes::RuntimeInstallResult::RestartRequired {
                warn!("A restart is required to complete the installation of {}.", dep.display_name());
                platform::show_restart_required(&app);
                return Ok(());
            }
        }
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
        util::retry_io(|| fs::create_dir_all(&root_path))?;
    }

    let root_path_str = root_path.to_str().unwrap();
    info!("Installation Directory: {:?}", root_path_str);

    // do we have enough disk space?
    let (compressed_size, extracted_size) = pkg.calculate_size();
    let required_space = compressed_size + extracted_size + (50 * 1000 * 1000); // archive + squirrel overhead
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
    if !app.os_min_version.is_empty() && !platform::is_os_version_or_greater(&app.os_min_version)? {
        bail!("This application requires Windows {} or later.", &app.os_min_version);
    }

    if !app.machine_architecture.is_empty() && !platform::is_cpu_architecture_supported(&app.machine_architecture)? {
        bail!("This application ({}) does not support your CPU architecture.", &app.machine_architecture);
    }

    let mut root_path_renamed = String::new();
    // does the target directory exist and have files? (eg. already installed)
    if !util::is_dir_empty(&root_path) {
        // the target directory is not empty, and not dead
        if !platform::show_overwrite_repair_dialog(&app, &root_path, root_is_default) {
            // user cancelled overwrite prompt
            error!("Directory exists, and user cancelled overwrite.");
            return Ok(());
        }

        platform::kill_processes_in_directory(&root_path)
            .map_err(|z| anyhow!("Failed to stop application ({}), please close the application and try running the installer again.", z))?;

        root_path_renamed = format!("{}_{}", root_path_str, util::random_string(8));
        info!("Renaming existing directory to '{}' to allow rollback...", root_path_renamed);

        util::retry_io(|| fs::rename(&root_path, &root_path_renamed)).map_err(|_| {
            anyhow!(
                "Failed to remove existing application directory, please close the application and try running the installer again. \
                If the issue persists, try uninstalling first via Programs & Features, or restarting your computer."
            )
        })?;
    }

    info!("Preparing and cleaning installation directory...");
    remove_dir_all::ensure_empty_dir(&root_path)?;

    let tx = splash::show_splash_in_new_thread(app.title.to_owned(), splash_bytes.clone(), true);
    let install_result = install_app(&pkg, &root_path, &tx);
    let _ = tx.send(splash::MSG_CLOSE);

    if install_result.is_ok() {
        info!("Installation completed successfully!");
        if !root_path_renamed.is_empty() {
            info!("Removing rollback directory...");
            let _ = util::retry_io(|| fs::remove_dir_all(&root_path_renamed));
        }
    } else {
        error!("Installation failed!");
        if !root_path_renamed.is_empty() {
            info!("Rolling back installation...");
            let _ = platform::kill_processes_in_directory(&root_path);
            let _ = util::retry_io(|| fs::remove_dir_all(&root_path));
            let _ = util::retry_io(|| fs::rename(&root_path_renamed, &root_path));
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
    let nuspec_path = app.get_nuspec_path(root_path);
    let nupkg_path = app.get_target_nupkg_path(root_path);
    let main_exe_path = app.get_main_exe_path(root_path);

    info!("Extracting Update.exe...");
    let updater_idx = pkg
        .extract_zip_predicate_to_path(|name| name.ends_with("Squirrel.exe"), updater_path)
        .map_err(|_| anyhow!("This installer is missing a critical binary (Update.exe). Please contact the application author."))?;
    let _ = tx.send(5);

    info!("Extracting bundle manifest...");
    let _ = pkg
        .extract_zip_predicate_to_path(|name| name.ends_with(".nuspec"), nuspec_path)
        .map_err(|_| anyhow!("This installer is missing a nuspec. Please contact the application author."))?;
    let _ = tx.send(7);

    info!("Copying nupkg to packages directory...");
    util::retry_io(|| fs::create_dir_all(&packages_path))?;
    pkg.copy_bundle_to_file(&nupkg_path)?;
    let _ = tx.send(10);

    info!("Extracting {} app files to current directory...", pkg.len());
    let re = Regex::new(r"lib[\\\/][^\\\/]*[\\\/]").unwrap();
    let stub_regex = Regex::new("_ExecutionStub.exe$").unwrap();

    let files = pkg.get_file_names()?;
    let num_files = files.len();

    for (i, key) in files.iter().enumerate() {
        if i == updater_idx || !re.is_match(key) || key.ends_with("/") || key.ends_with("\\") {
            info!("    {} Skipped '{}'", i, key);
            continue;
        }

        let file_path_in_zip = re.replace(key, "").to_string();
        let file_path_on_disk = Path::new(&current_path).join(&file_path_in_zip);

        if stub_regex.is_match(&file_path_in_zip) {
            // let stub_key = stub_regex.replace(&file_path_in_zip, ".exe").to_string();
            // file_path_on_disk = root_path.join(&stub_key);
            info!("    {} Skipped Stub (obsolete) '{}'", i, key);
            continue;
        }

        let final_path = file_path_on_disk.to_str().unwrap().replace("/", "\\");
        info!("    {} Extracting '{}' to '{}'", i, key, final_path);

        pkg.extract_zip_idx_to_path(i, &final_path)?;

        let progress = ((i as f32 / num_files as f32) * 80.0) as i16 + 10;
        let _ = tx.send(progress);
    }

    let _ = tx.send(90);

    // let folder_size = fs_extra::dir::get_size(&root_path).unwrap();
    // info!("{} extracted to {}", pretty_bytes(folder_size, None), root_path_str);

    if !Path::new(&main_exe_path).exists() {
        bail!("The main executable could not be found in the package. Please contact the application author.");
    }

    info!("Creating start menu shortcut...");
    let startmenu = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None)?;
    let lnk_path = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", &app.title));
    if let Err(e) = platform::create_lnk(&lnk_path.to_string_lossy(), &main_exe_path, &current_path) {
        warn!("Failed to create start menu shortcut: {}", e);
    }

    let ver_string = app.version.to_string();
    info!("Starting process install hook: \"{}\" --squirrel-install {}", &main_exe_path, &ver_string);
    let args = vec!["--squirrel-install", &ver_string];
    if let Err(e) = platform::run_process_no_console_and_wait(&main_exe_path, args, &current_path, Some(Duration::from_secs(30))) {
        let setup_name = format!("{} Setup {}", app.title, app.version);
        error!("Process install hook failed: {}", e);
        let _ = tx.send(splash::MSG_CLOSE);
        platform::show_warning(
            format!("Installation has completed, but the application install hook failed ({}). It may not have installed correctly.", e),
            setup_name,
        );
    }

    let _ = tx.send(100);

    app.write_uninstall_entry(root_path)?;

    if !platform::get_silent() {
        info!("Starting app: \"{}\" --squirrel-firstrun", main_exe_path);
        let args = vec!["--squirrel-firstrun"];
        let _ = platform::run_process(&main_exe_path, args, &current_path);
    }

    Ok(())
}
