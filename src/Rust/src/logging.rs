use anyhow::Result;
use simplelog::*;
use std::path::PathBuf;

pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, get_config(), TerminalMode::Mixed, ColorChoice::Never).unwrap();
}

pub fn setup_logging(file: Option<&PathBuf>, console: bool, verbose: bool, nocolor: bool) -> Result<()> {
    let mut loggers: Vec<Box<dyn SharedLogger>> = Vec::new();
    let color_choice = if nocolor { ColorChoice::Never } else { ColorChoice::Auto };
    if console {
        let console_level = if verbose { LevelFilter::Debug } else { LevelFilter::Info };
        loggers.push(TermLogger::new(console_level, get_config(), TerminalMode::Mixed, color_choice));
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
        loggers.push(WriteLogger::new(file_level, get_config(), writer));
    }

    CombinedLogger::init(loggers)?;
    Ok(())
}


fn get_config() -> Config {
    let mut c = ConfigBuilder::default();
    let _ = c.set_time_offset_to_local(); // might fail if local tz can't be determined
    c.build()
}