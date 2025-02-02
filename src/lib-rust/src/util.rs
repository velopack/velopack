use crate::Error;
use rand::distr::{Alphanumeric, SampleString};
use sha2::Digest;
use std::fs::File;
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