#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[macro_use]
extern crate log;

use anyhow::{bail, Result};
use clap::{arg, value_parser, Command};
use memmap2::Mmap;
use std::fs::File;
use std::path::Path;
use std::{env, path::PathBuf};
use velopack::bundle::BundleZip;
use velopack::sources::{HttpSource, UpdateSource};
use velopack_bins::*;

#[repr(u8)]
enum MultiPartMode {
    None = 0,
    Online = 1,
    LocalFilePart = 2,
    Embedded = 3,
}

enum MultiPartResult {
    None,
    Online(bool, String, String, String),
    LocalFilePart(bool, String),
    AbsolutePath(bool, String),
    Embedded(bool, i64, i64),
}

impl TryFrom<u8> for MultiPartMode {
    type Error = anyhow::Error;
    fn try_from(value: u8) -> Result<Self> {
        match value {
            0 => Ok(MultiPartMode::None),
            1 => Ok(MultiPartMode::Online),
            2 => Ok(MultiPartMode::LocalFilePart),
            3 => Ok(MultiPartMode::Embedded),
            _ => bail!("Invalid value for MultiPartMode"),
        }
    }
}

#[rustfmt::skip]
#[used]
#[no_mangle]
static MULTIPART_PLACEHOLDER: [u8; 2 + 1024 + 32] = [
    // 1 byte for multipart mode flag
    0,
    // requires elevated permissions
    0,
    // 1024 bytes for online multipart URL or local part path
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    // 32 bytes for multipart signature
    0x76, 0x70, 0x6b, 0x20, 0x6d, 0x75, 0x6c, 0x74,
    0x69, 0x70, 0x61, 0x72, 0x74, 0x2e, 0x2e, 0x2e,
    0x2e, 0x2e, 0x2e, 0x2e, 0x2e, 0x2e, 0x2e, 0x2e,
    0x2e, 0x2e, 0x2e, 0x2e, 0x2e, 0x2e, 0x2e, 0x2e,
];

#[inline(never)]
fn multipart_header() -> Result<MultiPartResult> {
    use core::ptr;
    // Perform a volatile read to avoid compiler optimization issues
    let mode: MultiPartMode = unsafe {
        let mode_number: u8 = ptr::read_volatile(&MULTIPART_PLACEHOLDER[0]);
        mode_number.try_into()?
    };

    let elevated: bool = unsafe { ptr::read_volatile(&MULTIPART_PLACEHOLDER[1]) != 0 };

    match mode {
        MultiPartMode::None => Ok(MultiPartResult::None),
        MultiPartMode::Online => {
            let url = String::from_utf8_lossy(&MULTIPART_PLACEHOLDER[1..1025]).trim_end_matches(char::from(0)).to_string();
            let parts: Vec<&str> = url.splitn(3, '\0').collect();
            let channel = parts.get(0).unwrap_or(&"").to_string();
            let filename = parts.get(1).unwrap_or(&"").to_string();
            let url = parts.get(2).unwrap_or(&"").to_string();
            if url.is_empty() || channel.is_empty() {
                bail!("Invalid online multipart URL or channel (missing)");
            }
            info!("MultiPart Online: Channel={}, Filename={}, URL={}", channel, filename, url);
            Ok(MultiPartResult::Online(elevated, channel, filename, url))
        }
        MultiPartMode::LocalFilePart => {
            let path = String::from_utf8_lossy(&MULTIPART_PLACEHOLDER[1..1025]).trim_end_matches(char::from(0)).to_string();
            info!("MultiPart LocalFilePart: Path={}", path);
            Ok(MultiPartResult::LocalFilePart(elevated, path))
        }
        MultiPartMode::Embedded => {
            let offset = i64::from_le_bytes(MULTIPART_PLACEHOLDER[1..9].try_into()?);
            let length = i64::from_le_bytes(MULTIPART_PLACEHOLDER[9..17].try_into()?);
            info!("MultiPart Embedded: Offset={}, Length={}", offset, length);
            Ok(MultiPartResult::Embedded(elevated, offset, length))
        }
    }
}

fn main() -> Result<()> {
    windows::mitigate::pre_main_sideload_mitigation();
    shared::cli_host::clap_run_main("Setup", main_inner)
}

fn main_inner() -> Result<()> {
    #[rustfmt::skip]
    let mut arg_config = Command::new("Setup")
        .about(format!("Velopack Setup ({}) installs applications.\nhttps://github.com/velopack/velopack", env!("NGBV_VERSION")))
        .arg(arg!(-s --silent "Hides all dialogs and answers 'yes' to all prompts"))
        .arg(arg!(-v --verbose "Print debug messages to console"))
        .arg(arg!(-l --log <FILE> "Enable file logging and set location").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(-t --install-to <DIR> "Installation directory to install the application").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(--no-first-run "Skip first application run after installation completed"))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceded by '--'.").required(false).last(true).num_args(0..));

    if cfg!(debug_assertions) {
        arg_config = arg_config
            .arg(arg!(--debug-file <FILE> "Debug mode, install from a nupkg file").required(false).value_parser(value_parser!(PathBuf)))
            .arg(arg!(--debug-online <URL> "Debug mode, install from an online feed").required(false));
    }

    let matches = arg_config.try_get_matches()?;

    let silent = matches.get_flag("silent");
    dialogs::set_silent(silent);

    let verbose = matches.get_flag("verbose");
    let logfile = matches.get_one::<PathBuf>("log");
    logging::setup_logging("setup", logfile, true, verbose)?;

    let no_first_run = matches.get_flag("no-first-run");
    let debug_file = matches.get_one::<PathBuf>("debug-file");
    let debug_online = matches.get_one::<String>("debug-online");
    let install_to = matches.get_one::<PathBuf>("install-to");
    let exe_args: Option<Vec<&str>> = matches.get_many::<String>("EXE_ARGS").map(|v| v.map(|f| f.as_str()).collect());

    info!("Starting Velopack Setup ({})", env!("NGBV_VERSION"));
    info!("    Location: {:?}", env::current_exe()?);
    info!("    Silent: {}", silent);
    info!("    Verbose: {}", verbose);
    info!("    Log: {:?}", logfile);
    info!("    Install To: {:?}", install_to);

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

    let mut installer_mode = multipart_header()?;
    if cfg!(debug_assertions) {
        if let Some(pkg) = debug_file {
            warn!("Loading bundle from DEBUG nupkg file {:?}...", pkg);
            installer_mode = MultiPartResult::AbsolutePath(false, pkg.to_string_lossy().to_string());
        } else if let Some(url) = debug_online {
            warn!("Loading bundle from DEBUG online feed {:?}...", url);
            let parts: Vec<&str> = url.splitn(2, ';').collect();
            installer_mode =
                MultiPartResult::Online(false, parts.get(0).unwrap().to_string(), "".to_string(), parts.get(1).unwrap().to_string());
        }
    }

    let my_exe_path = env::current_exe()?;

    match installer_mode {
        MultiPartResult::None => {
            bail!("No installer mode detected. Please contact the application author.");
        }
        MultiPartResult::Online(elevate, channel, filename, url) => {
            let package_path = if filename.is_empty() {
                let mut local_path = my_exe_path.clone();
                local_path.set_extension("supart");
                local_path
            } else {
                let mut local_path = my_exe_path.clone();
                local_path.pop();
                local_path.push(filename);
                local_path
            };

            main_download_online_package(&package_path, channel, url)?;
            let mut bundle = velopack::bundle::load_bundle_from_file(&package_path)?;
            main_install(&mut bundle, elevate, no_first_run, install_to, exe_args)?;
        }
        MultiPartResult::AbsolutePath(elevate, path) => {
            let mut bundle = velopack::bundle::load_bundle_from_file(&path)?;
            main_install(&mut bundle, elevate, no_first_run, install_to, exe_args)?;
        }
        MultiPartResult::LocalFilePart(elevate, filename) => {
            let package_path = if filename.is_empty() {
                let mut local_path = my_exe_path.clone();
                local_path.set_extension("supart");
                local_path
            } else {
                let mut local_path = my_exe_path.clone();
                local_path.pop();
                local_path.push(filename);
                local_path
            };
            let mut bundle = velopack::bundle::load_bundle_from_file(&package_path)?;
            main_install(&mut bundle, elevate, no_first_run, install_to, exe_args)?;
        }
        MultiPartResult::Embedded(elevate, offset, length) => {
            let file = File::open(env::current_exe()?)?;
            let mmap = unsafe { Mmap::map(&file)? };
            let zip_range: &[u8] = &mmap[offset as usize..(offset + length) as usize];
            let mut bundle = velopack::bundle::load_bundle_from_memory(&zip_range)?;
            main_install(&mut bundle, elevate, no_first_run, install_to, exe_args)?;
        }
    }

    Ok(())
}

fn main_download_online_package<T: AsRef<Path>>(local_path: T, channel: String, url: String) -> Result<()> {
    let local_path = local_path.as_ref();
    if local_path.exists() {
        info!("Local file already exists, skipping download.");
        return Ok(());
    }

    let source = HttpSource::new(&url);
    let feed = source.get_release_feed(&channel, None, None)?;

    let asset = feed.get_latest();
    if asset.is_none() {
        bail!("No assets found in feed remote.");
    }

    let asset = asset.unwrap();

    let tx = if dialogs::get_silent() {
        info!("Will not show progress because silent mode is on.");
        let (tx, _) = std::sync::mpsc::channel::<i16>();
        tx
    } else {
        info!("Showing progress dialog...");
        let exe_path = env::current_exe()?;
        let exe_name = exe_path.file_name().unwrap().to_string_lossy();
        windows::splash::show_progress_dialog(exe_name, "Please wait while the installer is downloading the package...")
    };

    source.download_release_entry(&asset, local_path.to_str().unwrap(), Some(tx.clone()))?;
    let _ = tx.send(windows::splash::MSG_CLOSE);

    Ok(())
}

fn main_install(
    bundle: &mut BundleZip,
    _required_elevated: bool,
    _no_first_run: bool,
    install_to: Option<&PathBuf>,
    start_args: Option<Vec<&str>>,
) -> Result<()> {
    // TODO: implement elevation here if required
    commands::install(bundle, install_to, start_args)?;
    Ok(())
}
