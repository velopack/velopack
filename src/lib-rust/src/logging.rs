use crate::locator::VelopackLocator;
use std::path::PathBuf;

#[cfg(feature = "file-logging")]
use simplelog::*;
#[cfg(feature = "file-logging")]
use time::format_description::{modifier, Component, FormatItem};

static LOGGING_FILE_NAME: &str = "velopack.log";

#[cfg(target_os = "linux")]
fn default_file_linux<L: TryInto<VelopackLocator>>(locator: L) -> PathBuf {
    let file_name = match locator.try_into() {
        Ok(locator) => format!("velopack_{}.log", locator.get_manifest_id()),
        Err(_) => LOGGING_FILE_NAME.to_string(),
    };
    std::env::temp_dir().join(file_name)
}

#[cfg(target_os = "macos")]
fn default_file_macos<L: TryInto<VelopackLocator>>(locator: L) -> PathBuf {
    let file_name = match locator.try_into() {
        Ok(locator) => format!("velopack_{}.log", locator.get_manifest_id()),
        Err(_) => LOGGING_FILE_NAME.to_string(),
    };

    #[allow(deprecated)]
    let user_home = std::env::home_dir();

    if let Some(home) = user_home {
        let mut lib_logs = home.clone();
        lib_logs.push("Library");
        lib_logs.push("Logs");

        if lib_logs.exists() {
            lib_logs.push(file_name);
            return lib_logs;
        }
    }

    std::env::temp_dir().join(file_name)
}

#[cfg(target_os = "windows")]
fn default_file_windows<L: TryInto<VelopackLocator>>(locator: L) -> PathBuf {
    match locator.try_into() {
        Ok(locator) => {
            let mut log_dir = locator.get_root_dir();
            log_dir.push(LOGGING_FILE_NAME);
            match std::fs::OpenOptions::new().write(true).create(true).open(&log_dir) {
                Ok(_) => log_dir, // the desired location is writable
                Err(_) => std::env::temp_dir().join(format!("velopack_{}.log", locator.get_manifest_id())), // fallback to temp dir with app name
            }
        }
        Err(_) => std::env::temp_dir().join(LOGGING_FILE_NAME), // fallback to temp dir shared filename
    }
}

/// Default log location for Velopack on the current OS.
pub fn default_logfile_path<L: TryInto<VelopackLocator>>(locator: L) -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        return default_file_windows(locator);
    }
    #[cfg(target_os = "linux")]
    {
        return default_file_linux(locator);
    }
    #[cfg(target_os = "macos")]
    {
        return default_file_macos(locator);
    }
}

/// Initialize logging for the current process. This will log optionally to a file and/or the console.
/// It can only be called once per process, and should be called early in the process lifecycle.
/// Future calls to this function will fail.
#[cfg(feature = "file-logging")]
pub fn init_logging(
    process_name: &str,
    file: Option<&PathBuf>,
    console: bool,
    verbose: bool,
    custom_log_cb: Option<Box<dyn SharedLogger>>,
) {
    let mut loggers: Vec<Box<dyn SharedLogger>> = Vec::new();
    if let Some(cb) = custom_log_cb {
        loggers.push(cb);
    }

    let color_choice = ColorChoice::Never;
    if console {
        let console_level = if verbose { LevelFilter::Debug } else { LevelFilter::Info };
        loggers.push(TermLogger::new(console_level, get_config(None), TerminalMode::Mixed, color_choice));
    }

    if let Some(f) = file {
        let file_level = if verbose { LevelFilter::Trace } else { LevelFilter::Info };
        let writer = super::file_rotate::FileRotate::new(
            f.clone(),
            1 * 1024 * 1024, // 1MB max log file size
        );
        loggers.push(WriteLogger::new(file_level, get_config(Some(process_name)), writer));
    }

    if let Ok(()) = CombinedLogger::init(loggers) {
        log_panics::init();
    }
}

/// Initialize a Trace / Console logger for the current process.
#[cfg(feature = "file-logging")]
pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, get_config(None), TerminalMode::Mixed, ColorChoice::Never).unwrap();
}

#[cfg(feature = "file-logging")]
fn get_config(process_name: Option<&str>) -> Config {
    let mut c = ConfigBuilder::default();
    let mut prefix = "".to_owned();
    if let Some(pn) = process_name {
        prefix = format!("[{}:{}] ", pn, std::process::id());
    }

    let prefix_heaped = Box::leak(prefix.into_boxed_str());

    let time_format: &'static [FormatItem<'static>] = Box::leak(Box::new([
        FormatItem::Literal(prefix_heaped.as_bytes()),
        FormatItem::Literal(b"["),
        FormatItem::Component(Component::Hour(modifier::Hour::default())),
        FormatItem::Literal(b":"),
        FormatItem::Component(Component::Minute(modifier::Minute::default())),
        FormatItem::Literal(b":"),
        FormatItem::Component(Component::Second(modifier::Second::default())),
        FormatItem::Literal(b"]"),
    ]));

    c.set_time_format_custom(time_format);
    let _ = c.set_time_offset_to_local(); // might fail if local tz can't be determined
    c.build()
}
