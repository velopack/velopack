use anyhow::Result;
use simplelog::*;
use std::path::PathBuf;
use time::format_description::{modifier, Component, FormatItem};

pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, get_config(None), TerminalMode::Mixed, ColorChoice::Never).unwrap();
}

pub fn setup_logging(process_name: &str, file: Option<&PathBuf>, console: bool, verbose: bool) -> Result<()> {
    let mut loggers: Vec<Box<dyn SharedLogger>> = Vec::new();
    let color_choice = ColorChoice::Never;
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
            None,
        );
        loggers.push(WriteLogger::new(file_level, get_config(Some(process_name)), writer));
    }

    CombinedLogger::init(loggers)?;
    log_panics::init();
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
