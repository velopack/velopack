use anyhow::Result;
use simplelog::*;
use std::path::PathBuf;

pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, get_config(), TerminalMode::Mixed, ColorChoice::Never).unwrap();
}

pub fn default_log_location() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        let mut my_dir = std::env::current_exe().unwrap();
        my_dir.pop();
        my_dir.pop();
        return my_dir.join("Velopack.log");
    }
    #[cfg(target_os = "linux")]
    {
        return std::path::Path::new("/tmp/velopack.log").to_path_buf();
    }
    #[cfg(target_os = "macos")]
    {
        #[allow(deprecated)]
        let mut user_home = std::env::home_dir().expect("Could not locate user home directory via $HOME or /etc/passwd");
        user_home.push("Library");
        user_home.push("Logs");
        user_home.push("velopack.log");
        return user_home;
    }
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