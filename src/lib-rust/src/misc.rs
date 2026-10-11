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

/// Converts a `windows` crate error (an HRESULT, `0x8007xxxx` for Win32 errors) into an `io::Error` whose
/// `raw_os_error()` is the original Win32 error code, so callers can match on `ERROR_*` values.
#[cfg(windows)]
pub fn io_error_from_win32(err: windows::core::Error) -> std::io::Error {
    std::io::Error::from_raw_os_error(win32_error_code(&err) as i32)
}

/// The Win32 error code carried by a `windows` crate error (the low 16 bits of a `FACILITY_WIN32` HRESULT).
#[cfg(windows)]
pub fn win32_error_code(err: &windows::core::Error) -> u32 {
    let code = err.code().0 as u32;
    if code & 0xFFFF_0000 == 0x8007_0000 {
        code & 0xFFFF
    } else {
        code
    }
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

/// Reduces an asset's FileName from a release feed to a name that is safe to store directly in the packages
/// directory. Only the last path segment is kept, so `1.2.0/App-1.2.0-full.nupkg`,
/// `https://cdn.example.com/App-1.2.0-full.nupkg` and `../../App-1.2.0-full.nupkg` all become
/// `App-1.2.0-full.nupkg`, and characters the OS does not allow in file names are replaced with `_`.
/// This mirrors `PathUtil.GetSafeFilename` in the C# library (`Path.GetFileName` followed by
/// `Path.GetInvalidFileNameChars` replacement) so that both libraries pick the same local file for an asset.
pub fn get_safe_filename(file_name: &str) -> String {
    let name = file_name.rsplit(is_path_separator).next().unwrap_or("");
    name.chars()
        .map(|c| {
            if is_invalid_filename_char(c) {
                '_'
            } else {
                c
            }
        })
        .collect()
}

#[cfg(windows)]
fn is_path_separator(c: char) -> bool {
    c == '/' || c == '\\'
}

#[cfg(not(windows))]
fn is_path_separator(c: char) -> bool {
    c == '/'
}

// Same set as .NET's Path.GetInvalidFileNameChars() on each platform.
#[cfg(windows)]
fn is_invalid_filename_char(c: char) -> bool {
    matches!(c, '"' | '<' | '>' | '|' | ':' | '*' | '?' | '\\' | '/') || (c as u32) < 32
}

#[cfg(not(windows))]
fn is_invalid_filename_char(c: char) -> bool {
    matches!(c, '\0' | '/')
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

    #[test]
    fn test_get_safe_filename_keeps_plain_names() {
        assert_eq!(get_safe_filename("App-1.0.0-full.nupkg"), "App-1.0.0-full.nupkg");
        assert_eq!(get_safe_filename(""), "");
    }

    #[test]
    fn test_get_safe_filename_keeps_only_the_last_path_segment() {
        // feed entries may point into a sub-folder or at an absolute URL (velopack/velopack#596)
        assert_eq!(get_safe_filename("1.20.0/App-1.20.0-full.nupkg"), "App-1.20.0-full.nupkg");
        assert_eq!(
            get_safe_filename("https://cdn.example.com/releases/App-1.20.0-full.nupkg"),
            "App-1.20.0-full.nupkg"
        );
        // and must never be able to escape the packages directory
        assert_eq!(get_safe_filename("../../App-1.20.0-full.nupkg"), "App-1.20.0-full.nupkg");
        assert_eq!(get_safe_filename("/etc/App-1.20.0-full.nupkg"), "App-1.20.0-full.nupkg");
    }

    #[cfg(windows)]
    #[test]
    fn test_get_safe_filename_windows() {
        assert_eq!(get_safe_filename("..\\..\\App-1.20.0-full.nupkg"), "App-1.20.0-full.nupkg");
        assert_eq!(get_safe_filename("C:\\Windows\\App-1.20.0-full.nupkg"), "App-1.20.0-full.nupkg");
        assert_eq!(get_safe_filename("releases\\1.20.0/App-1.20.0-full.nupkg"), "App-1.20.0-full.nupkg");
        assert_eq!(get_safe_filename("App:1.0?.nupkg"), "App_1.0_.nupkg");
        assert_eq!(get_safe_filename("<>|\"*\t"), "______");
    }

    #[cfg(not(windows))]
    #[test]
    fn test_get_safe_filename_unix() {
        // like .NET on unix, a backslash is an ordinary file name character, so this is one (odd) file name
        assert_eq!(get_safe_filename("..\\..\\App-1.20.0-full.nupkg"), "..\\..\\App-1.20.0-full.nupkg");
        assert_eq!(get_safe_filename("App:1.0?\0.nupkg"), "App:1.0?_.nupkg");
    }
}
