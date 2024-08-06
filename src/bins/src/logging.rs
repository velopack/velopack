use anyhow::Result;
use simplelog::*;
use std::path::PathBuf;
use time::format_description::{modifier, Component, FormatItem};

pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, get_config(None), TerminalMode::Mixed, ColorChoice::Never).unwrap();
}

pub fn default_log_location() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        let mut my_dir = std::env::current_exe().unwrap();
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

pub fn setup_logging(process_name: &str, file: Option<&PathBuf>, console: bool, verbose: bool, nocolor: bool) -> Result<()> {
    let mut loggers: Vec<Box<dyn SharedLogger>> = Vec::new();
    let color_choice = if nocolor { ColorChoice::Never } else { ColorChoice::Auto };
    if console {
        let console_level = if verbose { LevelFilter::Debug } else { LevelFilter::Info };
        loggers.push(TermLogger::new(console_level, get_config(None), TerminalMode::Mixed, color_choice));
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
        loggers.push(WriteLogger::new(file_level, get_config(Some(process_name)), writer));
    }

    CombinedLogger::init(loggers)?;
    Ok(())
}

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
