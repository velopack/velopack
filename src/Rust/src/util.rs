use anyhow::{anyhow, Result};

use rand::distributions::{Alphanumeric, DistString};
use regex::Regex;
use simplelog::*;
use std::{
    io::{self},
    path::PathBuf,
    thread,
    time::Duration,
};

pub fn retry_io<F, T>(op: F) -> io::Result<T>
where
    F: Fn() -> io::Result<T>,
{
    let res = op();
    if res.is_ok() {
        return Ok(res.unwrap());
    }

    warn!("Retrying operation in 333ms... (error was: {:?})", res.err());
    thread::sleep(Duration::from_millis(333));

    let res = op();
    if res.is_ok() {
        return Ok(res.unwrap());
    }

    warn!("Retrying operation in 666ms... (error was: {:?})", res.err());
    thread::sleep(Duration::from_millis(666));

    let res = op();
    if res.is_ok() {
        return Ok(res.unwrap());
    }

    warn!("Retrying operation in 1000ms... (error was: {:?})", res.err());
    thread::sleep(Duration::from_millis(1000));

    let res = op();
    if res.is_ok() {
        return Ok(res.unwrap());
    }

    warn!("Last retry in 1000ms... (error was: {:?})", res.err());
    thread::sleep(Duration::from_millis(1000));

    op()
}

pub fn random_string(len: usize) -> String {
    Alphanumeric.sample_string(&mut rand::thread_rng(), len)
}

pub fn is_dir_empty(path: &PathBuf) -> bool {
    if !path.exists() {
        return true;
    }
    let is_empty = path.read_dir().map(|mut i| i.next().is_none()).unwrap_or(false);
    let is_dead = path.join(".dead").exists();
    return is_dead || is_empty;
}

pub fn trace_logger() {
    TermLogger::init(LevelFilter::Trace, Config::default(), TerminalMode::Mixed, ColorChoice::Never).unwrap();
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
        );
        loggers.push(WriteLogger::new(file_level, Config::default(), writer));
    }

    CombinedLogger::init(loggers)?;
    Ok(())
}

lazy_static! {
    static ref REGEX_VERSION: Regex = Regex::new(r"^(?P<major>\d+)(\.(?P<minor>\d+))?(\.(?P<build>\d+))?(\.(?P<revision>\d+))?$").unwrap();
}

pub fn parse_version(version: &str) -> Result<(u32, u32, u32, u32)> {
    let caps = REGEX_VERSION.captures(version).ok_or_else(|| anyhow!("Invalid version string: '{}'", version))?;
    let major_str = caps.name("major").ok_or_else(|| anyhow!("Invalid version string: '{}'", version))?.as_str();
    let minor_str = caps.name("minor");
    let build_str = caps.name("build");
    let revision_str = caps.name("revision");
    let major = major_str.parse::<u32>()?;
    let minor = if minor_str.is_some() { minor_str.unwrap().as_str().parse::<u32>()? } else { 0 };
    let build = if build_str.is_some() { build_str.unwrap().as_str().parse::<u32>()? } else { 0 };
    let revision = if revision_str.is_some() { revision_str.unwrap().as_str().parse::<u32>()? } else { 0 };
    Ok((major, minor, build, revision))
}

#[test]
fn test_parse_version_works_with_short_version() {
    let (major, minor, build, _) = parse_version("10").unwrap();
    assert_eq!(major, 10);
    assert_eq!(minor, 0);
    assert_eq!(build, 0);
}

#[test]
fn test_parse_version_works_with_long_version() {
    let (major, minor, build, revision) = parse_version("1033.980.03984.14234").unwrap();
    assert_eq!(major, 1033);
    assert_eq!(minor, 980);
    assert_eq!(build, 3984);
    assert_eq!(revision, 14234);
}

#[test]
fn test_parse_version_throws_with_invalid_version() {
    assert!(parse_version("invalid").is_err());
    assert!(parse_version("1.1.1.1.1").is_err());
    assert!(parse_version("1.1.1.a").is_err());
}

pub fn utf8_safe_substring(s: &str, start_char_idx: usize, length: usize) -> Option<&str> {
    if length <= 0 {
        return None;
    }
    let mut char_iter = s.char_indices();
    let start_byte_idx = char_iter.nth(start_char_idx)?.0;
    let end_byte_idx = char_iter.nth(length)?.0;
    s.get(start_byte_idx..end_byte_idx)
}
