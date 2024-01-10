use anyhow::{anyhow, Result};
use rand::distributions::{Alphanumeric, DistString};
use regex::Regex;
use std::{path::Path, thread, time::Duration};

#[cfg(not(target_os = "linux"))]
pub fn replace_dir_with_rollback<F, T, P: AsRef<Path>>(path: P, op: F) -> Result<()>
where
    F: FnOnce() -> Result<T>,
{
    use std::fs;
    let path = path.as_ref();
    let is_dir = path.is_dir();
    let path = path.to_string_lossy().to_string();
    let mut path_renamed = String::new();

    if !is_dir_empty(&path) {
        path_renamed = format!("{}_{}", path, random_string(8));
        info!("Renaming directory '{}' to '{}' to allow rollback...", path, path_renamed);

        super::force_stop_package(&path)
            .map_err(|z| anyhow!("Failed to stop application ({}), please close the application and try running the installer again.", z))?;

        retry_io(|| fs::rename(&path, &path_renamed)).map_err(|z| anyhow!("Failed to rename directory '{}' to '{}' ({}).", path, path_renamed, z))?;
    }

    if is_dir {
        remove_dir_all::ensure_empty_dir(&path).map_err(|z| anyhow!("Failed to create clean directory '{}' ({}).", path, z))?;
    }

    info!("Running rollback protected operation...");
    if let Err(e) = op() {
        // install failed, rollback if possible
        warn!("Rolling back installation... (error was: {:?})", e);

        if let Err(ex) = super::force_stop_package(&path) {
            warn!("Failed to stop application ({}).", ex);
        }

        if !path_renamed.is_empty() {
            if let Err(ex) = super::retry_io(|| fs::remove_dir_all(&path)) {
                error!("Failed to remove directory '{}' ({}).", path, ex);
            }
            if let Err(ex) = super::retry_io(|| fs::rename(&path_renamed, &path)) {
                error!("Failed to rename directory '{}' to '{}' ({}).", path_renamed, path, ex);
            }
        }
        return Err(e);
    } else {
        // install successful, remove rollback directory if exists
        if !path_renamed.is_empty() {
            debug!("Removing rollback path '{}'.", path_renamed);
            if is_dir {
                if let Err(ex) = super::retry_io(|| fs::remove_dir_all(&path_renamed)) {
                    warn!("Failed to remove rollback directory '{}' ({}).", path_renamed, ex);
                }
            } else {
                if let Err(ex) = super::retry_io(|| fs::remove_file(&path_renamed)) {
                    warn!("Failed to remove rollback file '{}' ({}).", path_renamed, ex);
                }
            }
        }
        return Ok(());
    }
}

pub fn retry_io<F, T, E>(op: F) -> Result<T, E>
where
    F: Fn() -> Result<T, E>,
    E: std::fmt::Debug,
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

pub fn is_dir_empty<P: AsRef<Path>>(path: P) -> bool {
    let path = path.as_ref();
    if !path.exists() {
        return true;
    }
    let is_empty = path.read_dir().map(|mut i| i.next().is_none()).unwrap_or(false);
    let is_dead = path.join(".dead").exists();
    return is_dead || is_empty;
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

pub fn utf8_safe_substring_len(s: &str, start_char_idx: usize, length: usize) -> Option<&str> {
    if length <= 0 {
        return None;
    }
    let mut char_iter = s.char_indices();
    let start_byte_idx = char_iter.nth(start_char_idx)?.0;
    let end_byte_idx = char_iter.nth(length)?.0;
    s.get(start_byte_idx..end_byte_idx)
}

pub fn utf8_safe_substring(s: &str, start_char_idx: usize) -> Option<&str> {
    let mut char_iter = s.char_indices();
    let start_byte_idx = char_iter.nth(start_char_idx)?.0;
    s.get(start_byte_idx..)
}
