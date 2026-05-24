use crate::errors::Error;
use sha2::Digest;
use std::fs::File;
use std::io::Read;
use std::path::Path;

/// Retries an I/O operation up to 3 additional times on failure.
/// In WASM there is no thread::sleep, so retries happen immediately
/// without delay.
pub fn retry_io<F, T, E>(op: F) -> Result<T, E>
where
    F: Fn() -> Result<T, E>,
    E: std::fmt::Debug,
{
    let res = op();
    if res.is_ok() {
        return res;
    }

    log::warn!("Retrying operation (attempt 2)... (error was: {:?})", res.as_ref().err());

    let res = op();
    if res.is_ok() {
        return res;
    }

    log::warn!("Retrying operation (attempt 3)... (error was: {:?})", res.as_ref().err());

    let res = op();
    if res.is_ok() {
        return res;
    }

    log::warn!("Retrying operation (attempt 4, final)... (error was: {:?})", res.as_ref().err());

    op()
}

/// Generates a random alphanumeric string of the given length using WASI random.
pub fn random_string(len: usize) -> String {
    const CHARSET: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    let mut buf = vec![0u8; len];
    wstd::rand::get_random_bytes(&mut buf);
    buf.iter().map(|b| CHARSET[(*b as usize) % CHARSET.len()] as char).collect()
}

/// Generates a UUID v4 string using WASI random.
pub fn generate_uuid_v4() -> String {
    let mut bytes = [0u8; 16];
    wstd::rand::get_random_bytes(&mut bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
    bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 1
    format!(
        "{:02x}{:02x}{:02x}{:02x}-{:02x}{:02x}-{:02x}{:02x}-{:02x}{:02x}-{:02x}{:02x}{:02x}{:02x}{:02x}{:02x}",
        bytes[0], bytes[1], bytes[2], bytes[3],
        bytes[4], bytes[5], bytes[6], bytes[7],
        bytes[8], bytes[9], bytes[10], bytes[11],
        bytes[12], bytes[13], bytes[14], bytes[15]
    )
}

fn to_hex(bytes: &[u8]) -> String {
    bytes.iter().fold(String::with_capacity(bytes.len() * 2), |mut s, b| {
        use std::fmt::Write;
        write!(s, "{:02X}", b).unwrap();
        s
    })
}

/// Calculates both SHA1 and SHA256 hashes for the given file, returning
/// them as uppercase hex strings in the order (SHA1, SHA256).
// TODO: make truly async once wstd supports yield_now or async file streams
pub fn calculate_sha1_sha256<P: AsRef<Path>>(file: P) -> Result<(String, String), Error> {
    let file = File::open(file)?;
    let mut reader = std::io::BufReader::new(file);

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
