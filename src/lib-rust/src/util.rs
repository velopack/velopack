use std::thread;
use std::time::Duration;
use rand::distributions::{Alphanumeric, DistString};

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