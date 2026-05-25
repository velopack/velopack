use crate::errors::Error;
use crate::host_fs;
use sha2::Digest;

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

pub fn random_string(len: usize) -> String {
    const CHARSET: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    let mut buf = vec![0u8; len];
    wstd::rand::get_random_bytes(&mut buf);
    buf.iter().map(|b| CHARSET[(*b as usize) % CHARSET.len()] as char).collect()
}

pub fn generate_uuid_v4() -> String {
    let mut bytes = [0u8; 16];
    wstd::rand::get_random_bytes(&mut bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    format!(
        "{:02x}{:02x}{:02x}{:02x}-{:02x}{:02x}-{:02x}{:02x}-{:02x}{:02x}-{:02x}{:02x}{:02x}{:02x}{:02x}{:02x}",
        bytes[0],
        bytes[1],
        bytes[2],
        bytes[3],
        bytes[4],
        bytes[5],
        bytes[6],
        bytes[7],
        bytes[8],
        bytes[9],
        bytes[10],
        bytes[11],
        bytes[12],
        bytes[13],
        bytes[14],
        bytes[15]
    )
}

fn to_hex(bytes: &[u8]) -> String {
    bytes.iter().fold(String::with_capacity(bytes.len() * 2), |mut s, b| {
        use std::fmt::Write;
        write!(s, "{:02X}", b).unwrap();
        s
    })
}

pub struct ChecksumBuilder {
    sha1: sha1::Sha1,
    sha256: sha2::Sha256,
    size: u64,
}

impl ChecksumBuilder {
    pub fn new() -> Self {
        ChecksumBuilder {
            sha1: sha1::Sha1::new(),
            sha256: sha2::Sha256::new(),
            size: 0,
        }
    }

    pub fn update(&mut self, data: &[u8]) {
        self.sha1.update(data);
        self.sha256.update(data);
        self.size += data.len() as u64;
    }

    pub fn size(&self) -> u64 {
        self.size
    }

    pub fn finish(self) -> (String, String) {
        (to_hex(&self.sha1.finalize()), to_hex(&self.sha256.finalize()))
    }
}

pub fn copy_file_with_checksums(from: &str, to: &str) -> Result<(u64, String, String), Error> {
    use crate::host_fs::HandleGuard;
    let rh = host_fs::open(from, false, false)?;
    let rg = HandleGuard(rh);
    let wh = host_fs::open(to, true, true)?;
    let wg = HandleGuard(wh);
    let mut csb = ChecksumBuilder::new();
    loop {
        let chunk = host_fs::read(rh, 64 * 1024)?;
        if chunk.is_empty() {
            break;
        }
        csb.update(&chunk);
        host_fs::write(wh, &chunk)?;
    }
    host_fs::close(rh)?;
    std::mem::forget(rg);
    host_fs::close(wh)?;
    std::mem::forget(wg);
    let size = csb.size();
    let (sha1, sha256) = csb.finish();
    Ok((size, sha1, sha256))
}
