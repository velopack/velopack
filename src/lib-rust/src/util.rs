use crate::Error;
use rand::distributions::{Alphanumeric, DistString};
use sha2::Digest;
use std::fs::File;
use std::path::Path;
use std::thread;
use std::time::Duration;

#[cfg(target_os = "windows")]
use windows::{
    core::{GUID, PWSTR},
    Win32::UI::Shell::{
        FOLDERID_LocalAppData,
        SHGetKnownFolderPath,
    },
};

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

    op()
}

pub fn random_string(len: usize) -> String {
    Alphanumeric.sample_string(&mut rand::thread_rng(), len)
}

pub fn calculate_file_sha256<P: AsRef<Path>>(file: P) -> Result<String, Error> {
    let mut file = File::open(file)?;
    let mut sha256 = sha2::Sha256::new();
    std::io::copy(&mut file, &mut sha256)?;
    let hash = sha256.finalize();
    Ok(format!("{:x}", hash))
}

pub fn calculate_file_sha1<P: AsRef<Path>>(file: P) -> Result<String, Error> {
    let mut file = File::open(file)?;
    let mut sha1o = sha1::Sha1::new();
    std::io::copy(&mut file, &mut sha1o)?;
    let hash = sha1o.finalize();
    Ok(format!("{:x}", hash))
}

pub fn is_directory_writable<P1: AsRef<Path>>(path: P1) -> bool {
    use std::os::windows::fs::OpenOptionsExt;
    let path = path.as_ref();
    let path = path.join(".velopack_dir_test");
    let result = std::fs::File::options()
        .create(true)
        .write(true)
        .custom_flags(0x04000000) // FILE_FLAG_DELETE_ON_CLOSE
        .open(&path);

    if let Err(e) = result {
        warn!("Failed to open directory for writing {:?}: {}", path, e);
        return false;
    }

    result.is_ok()
}

#[cfg(target_os = "windows")]
fn get_known_folder(rfid: *const GUID) -> Result<String, Error> {
    unsafe {
        let flag = windows::Win32::UI::Shell::KNOWN_FOLDER_FLAG(0);
        let result = SHGetKnownFolderPath(rfid, flag, None)?;
        pwstr_to_string(result)
    }
}

#[cfg(target_os = "windows")]
fn pwstr_to_string(input: PWSTR) -> Result<String, Error> {
    unsafe {
        let hstring = input.to_hstring()?;
        let string = hstring.to_string_lossy();
        Ok(string.trim_end_matches('\0').to_string())
    }
}

#[cfg(target_os = "windows")]
pub fn get_local_app_data() -> Result<String, Error> {
    get_known_folder(&FOLDERID_LocalAppData)
}