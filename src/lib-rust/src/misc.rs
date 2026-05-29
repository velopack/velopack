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
    match op() {
        Ok(val) => return Ok(val),
        Err(e) => warn!("Retrying operation in 333ms... (error was: {:?})", e),
    }
    thread::sleep(Duration::from_millis(333));

    match op() {
        Ok(val) => return Ok(val),
        Err(e) => warn!("Retrying operation in 666ms... (error was: {:?})", e),
    }
    thread::sleep(Duration::from_millis(666));

    match op() {
        Ok(val) => return Ok(val),
        Err(e) => warn!("Retrying operation in 1000ms... (error was: {:?})", e),
    }
    thread::sleep(Duration::from_millis(1000));

    op()
}

pub fn random_string(len: usize) -> String {
    Alphanumeric.sample_string(&mut rand::rng(), len)
}

fn to_hex(bytes: &[u8]) -> String {
    bytes.iter().fold(String::with_capacity(bytes.len() * 2), |mut s, b| {
        use std::fmt::Write;
        write!(s, "{:02X}", b).unwrap();
        s
    })
}

pub fn calculate_sha1_sha256<P: AsRef<Path>>(file: P) -> Result<(String, String), Error> {
    let file = File::open(file)?;
    let mut reader = BufReader::new(file);

    let mut sha256 = sha2::Sha256::new();
    let mut sha1 = sha1::Sha1::new();

    let mut buffer = [0u8; 64 * 1024];
    loop {
        let bytes_read = reader.read(&mut buffer)?;
        if bytes_read == 0 {
            break;
        }

        sha256.update(&buffer[..bytes_read]);
        sha1.update(&buffer[..bytes_read]);
    }

    let sha256_hash = to_hex(&sha256.finalize());
    let sha1_hash = to_hex(&sha1.finalize());

    Ok((sha1_hash, sha256_hash))
}

#[cfg(target_os = "windows")]
pub fn is_directory_writable<P1: AsRef<Path>>(path: P1) -> bool {
    use std::os::windows::fs::OpenOptionsExt;
    let path = path.as_ref();
    let path = path.join(".velopack_dir_test");
    let result = std::fs::File::options()
        .create(true)
        .truncate(true)
        .write(true)
        .custom_flags(0x04000000) // FILE_FLAG_DELETE_ON_CLOSE
        .open(&path);

    if let Err(e) = result {
        warn!("Failed to open directory for writing {:?}: {}", path, e);
        return false;
    }

    result.is_ok()
}

/// Check if a path is a subdirectory of a parent directory
pub fn is_sub_path<P1: AsRef<Path>, P2: AsRef<Path>>(path: P1, parent: P2) -> bool {
    let path = path.as_ref().to_string_lossy().to_lowercase();
    let parent = parent.as_ref().to_string_lossy().to_lowercase();

    // Normalize separator
    #[cfg(windows)]
    let separator = "\\";
    #[cfg(not(windows))]
    let separator = "/";

    let parent = parent.trim_end_matches('\\').trim_end_matches('/').to_owned() + separator;

    if path.is_empty() || parent.is_empty() {
        return false;
    }

    if path.len() < parent.len() {
        return false;
    }

    path.starts_with(&parent)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    #[test]
    fn test_to_hex() {
        assert_eq!(to_hex(&[]), "");
        assert_eq!(to_hex(&[0x00]), "00");
        assert_eq!(to_hex(&[0xff]), "FF");
        assert_eq!(to_hex(&[0xde, 0xad, 0xbe, 0xef]), "DEADBEEF");
    }

    #[test]
    fn test_calculate_sha1_sha256() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("test.txt");
        let mut f = File::create(&path).unwrap();
        f.write_all(b"hello world").unwrap();
        drop(f);

        let (sha1, sha256) = calculate_sha1_sha256(&path).unwrap();

        // known hashes for "hello world"
        assert_eq!(sha1, "2AAE6C35C94FCFB415DBE95F408B9CE91EE846ED");
        assert_eq!(sha256, "B94D27B9934D3E08A52E52D7DA7DABFAC484EFE37A5380EE9088F7ACE2EFCDE9");

        // verify they are uppercase hex (consistent with C# BitConverter.ToString)
        assert!(sha1.chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_lowercase()));
        assert!(sha256.chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_lowercase()));
    }
}
