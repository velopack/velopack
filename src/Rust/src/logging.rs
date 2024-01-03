use anyhow::Result;
use simplelog::*;
use std::{env, path::PathBuf};

pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, Config::default(), TerminalMode::Mixed, ColorChoice::Never).unwrap();
}
pub fn default_logging(verbose: bool, nocolor: bool) -> Result<()> {
    #[cfg(target_os = "windows")]
    let default_log_file = {
        let mut my_dir = env::current_exe().unwrap();
        my_dir.pop();
        my_dir.join("Velopack.log")
    };

    #[cfg(target_os = "macos")]
    let default_log_file = {
        let (_root, manifest) = shared::detect_current_manifest().expect("Unable to load app manfiest.");
        std::path::Path::new(format!("/tmp/velopack/{}.log", manifest.id).as_str()).to_path_buf()
    };

    setup_logging(Some(&default_log_file), true, verbose, nocolor)
}

pub fn setup_logging(file: Option<&PathBuf>, console: bool, verbose: bool, nocolor: bool) -> Result<()> {
    let mut loggers: Vec<Box<dyn SharedLogger>> = Vec::new();
    let color_choice = if nocolor { ColorChoice::Never } else { ColorChoice::Auto };
    if console {
        let console_level = if verbose { LevelFilter::Debug } else { LevelFilter::Info };
        loggers.push(TermLogger::new(console_level, Config::default(), TerminalMode::Mixed, color_choice));
    }

    if let Some(f) = file {
        let file_level = if verbose { LevelFilter::Trace } else { LevelFilter::Info };
        let writer = file_rotate::FileRotate::new(
            f.clone(),
            file_rotate::suffix::AppendCount::new(1),          // keep 1 old log file
            file_rotate::ContentLimit::Bytes(1 * 1024 * 1024), // 1MB max log file size
            file_rotate::compression::Compression::None,
            #[cfg(unix)]
            None,
        );
        loggers.push(WriteLogger::new(file_level, Config::default(), writer));
    }

    CombinedLogger::init(loggers)?;
    Ok(())
}
