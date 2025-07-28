use crate::Error;
use rand::distr::{Alphanumeric, SampleString};
use sha2::Digest;
use std::fs::File;
use std::io::{BufReader, Read};
use std::path::Path;
use std::thread;
use std::time::Duration;

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
    Alphanumeric.sample_string(&mut rand::rng(), len)
}

pub fn calculate_sha1_sha256<P: AsRef<Path>>(file: P) -> Result<(String, String), Error> {
    let file = File::open(file)?;
    let mut reader = BufReader::new(file);

    let mut sha256 = sha2::Sha256::new();
    let mut sha1 = sha1::Sha1::new();

    let mut buffer = [0u8; 1024 * 1024]; // 1MB buffer
    loop {
        let bytes_read = reader.read(&mut buffer)?;
        if bytes_read == 0 {
            break;
        }

        sha256.update(&buffer[..bytes_read]);
        sha1.update(&buffer[..bytes_read]);
    }

    let sha256_hash = format!("{:x}", sha256.finalize());
    let sha1_hash = format!("{:x}", sha1.finalize());

    Ok((sha1_hash, sha256_hash))
}

#[cfg(target_os = "windows")]
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
