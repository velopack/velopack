use anyhow::{anyhow, bail, Result};
use rand::distributions::{Alphanumeric, DistString};
use regex::Regex;
use std::{
    ffi::OsStr,
    fs,
    io::{self},
    path::{Path, PathBuf},
    process::Command as Process,
    thread,
    time::Duration,
};

use crate::shared::bundle;

use super::bundle::{EntryNameInfo, Manifest};

pub fn replace_dir_with_rollback<F, T, P: AsRef<Path>>(path: P, op: F) -> Result<()>
where
    F: FnOnce() -> Result<T>,
{
    let path = path.as_ref().to_string_lossy().to_string();
    let mut path_renamed = String::new();

    if !is_dir_empty(&path) {
        path_renamed = format!("{}_{}", path, random_string(8));
        info!("Renaming directory '{}' to '{}' to allow rollback...", path, path_renamed);

        super::force_stop_package(&path)
            .map_err(|z| anyhow!("Failed to stop application ({}), please close the application and try running the installer again.", z))?;

        retry_io(|| fs::rename(&path, &path_renamed)).map_err(|z| anyhow!("Failed to rename directory '{}' to '{}' ({}).", path, path_renamed, z))?;
    }

    remove_dir_all::ensure_empty_dir(&path).map_err(|z| anyhow!("Failed to create clean directory '{}' ({}).", path, z))?;

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
            debug!("Removing rollback directory '{}'.", path_renamed);
            if let Err(ex) = super::retry_io(|| fs::remove_dir_all(&path_renamed)) {
                warn!("Failed to remove directory '{}' ({}).", path_renamed, ex);
            }
        }
        return Ok(());
    }
}

pub fn run_process<S, P>(exe: S, args: Vec<&str>, work_dir: P) -> Result<()>
where
    S: AsRef<OsStr>,
    P: AsRef<Path>,
{
    Process::new(exe).args(args).current_dir(work_dir).spawn()?;
    Ok(())
}

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

pub fn utf8_safe_substring(s: &str, start_char_idx: usize, length: usize) -> Option<&str> {
    if length <= 0 {
        return None;
    }
    let mut char_iter = s.char_indices();
    let start_byte_idx = char_iter.nth(start_char_idx)?.0;
    let end_byte_idx = char_iter.nth(length)?.0;
    s.get(start_byte_idx..end_byte_idx)
}

fn get_my_root_dir() -> Result<PathBuf> {
    let mut my_dir = std::env::current_exe()?;
    my_dir.pop();
    Ok(my_dir)
}

pub fn detect_current_manifest() -> Result<(PathBuf, Manifest)> {
    let root_path = get_my_root_dir()?;
    let app = find_manifest_from_root_dir(&root_path)
        .map_err(|m| anyhow!("Unable to read application manifest ({}). Is this a properly installed application?", m))?;
    info!("Loaded manifest for application: {}", app.id);
    info!("Root Directory: {}", root_path.to_string_lossy());
    Ok((root_path, app))
}

fn find_manifest_from_root_dir(root_path: &PathBuf) -> Result<Manifest> {
    // default to checking current/sq.version
    let cm = find_current_manifest(root_path);
    if cm.is_ok() {
        return cm;
    }

    // if that fails, check for latest full package
    let latest = find_latest_full_package(root_path);
    if let Some(latest) = latest {
        let mani = latest.load_manifest()?;
        return Ok(mani);
    }

    bail!("Unable to locate manifest or package.");
}

fn find_current_manifest(root_path: &PathBuf) -> Result<Manifest> {
    let m = Manifest::default();
    let nuspec_path = m.get_nuspec_path(root_path);
    if Path::new(&nuspec_path).exists() {
        if let Ok(nuspec) = super::retry_io(|| std::fs::read_to_string(&nuspec_path)) {
            return Ok(bundle::read_manifest_from_string(&nuspec)?);
        }
    }
    bail!("Unable to read nuspec file in current directory.")
}

fn find_latest_full_package(root_path: &PathBuf) -> Option<EntryNameInfo> {
    let packages = get_all_packages(root_path);
    let mut latest: Option<EntryNameInfo> = None;
    for pkg in packages {
        if pkg.is_delta {
            continue;
        }
        if latest.is_none() {
            latest = Some(pkg);
        } else {
            let latest_ver = latest.clone().unwrap().version;
            if pkg.version > latest_ver {
                latest = Some(pkg);
            }
        }
    }
    latest
}

fn get_all_packages(root_path: &PathBuf) -> Vec<EntryNameInfo> {
    let m = Manifest::default();
    let packages = m.get_packages_path(root_path);
    let mut vec = Vec::new();
    debug!("Scanning for packages in {:?}", packages);
    if let Ok(entries) = std::fs::read_dir(packages) {
        for entry in entries {
            if let Ok(entry) = entry {
                if let Some(pkg) = super::bundle::parse_package_file_path(entry.path()) {
                    debug!("Found package: {}", entry.path().to_string_lossy());
                    vec.push(pkg);
                }
            }
        }
    }
    vec
}
