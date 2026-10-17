//! The append-only chunk blob `chunks.bin` (§6.2).
//!
//! Records are `"VCHK" ‖ u32 length ‖ u8[32] sha256 ‖ payload`. Opening the blob is one sequential
//! verifying read: records whose payload hashes correctly are indexed, a record with a corrupt payload
//! (the crash tail) is skipped, and the first structural failure truncates the file.

use super::{inflate::sha256, BLOB_FLUSH_INTERVAL, BLOB_MAGIC, MAX_CHUNK_LEN};
use std::collections::HashMap;
use std::fs::{File, OpenOptions};
use std::io::{self, BufReader, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

const RECORD_HEADER_LEN: u64 = 4 + 4 + 32;

/// A verified, hash-indexed view of a `chunks.bin` file.
pub struct ChunkBlob {
    path: PathBuf,
    file: File,
    index: HashMap<[u8; 32], (u64, u32)>,
    end: u64,
    unflushed: u64,
}

fn read_exact_at(file: &File, offset: u64, buf: &mut [u8]) -> io::Result<()> {
    #[cfg(windows)]
    {
        use std::os::windows::fs::FileExt;
        let mut done = 0usize;
        while done < buf.len() {
            let n = file.seek_read(&mut buf[done..], offset + done as u64)?;
            if n == 0 {
                return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "blob record is truncated"));
            }
            done += n;
        }
        Ok(())
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::FileExt;
        file.read_exact_at(buf, offset)
    }
}

impl ChunkBlob {
    /// Opens (or creates) the blob and performs the verifying scan.
    pub fn open<P: AsRef<Path>>(path: P) -> io::Result<ChunkBlob> {
        let path = path.as_ref().to_path_buf();
        let mut file = OpenOptions::new().read(true).write(true).create(true).truncate(false).open(&path)?;
        let file_len = file.metadata()?.len();
        let mut index: HashMap<[u8; 32], (u64, u32)> = HashMap::new();
        let mut pos = 0u64;
        let mut skipped = 0usize;
        {
            let mut reader = BufReader::with_capacity(1024 * 1024, &file);
            let mut header = [0u8; RECORD_HEADER_LEN as usize];
            let mut payload: Vec<u8> = Vec::new();
            while pos + RECORD_HEADER_LEN <= file_len {
                reader.read_exact(&mut header)?;
                if header[0..4] != BLOB_MAGIC {
                    break;
                }
                let length = u32::from_le_bytes([header[4], header[5], header[6], header[7]]);
                if length == 0 || length > MAX_CHUNK_LEN || pos + RECORD_HEADER_LEN + length as u64 > file_len {
                    break;
                }
                let mut hash = [0u8; 32];
                hash.copy_from_slice(&header[8..40]);
                payload.resize(length as usize, 0);
                reader.read_exact(&mut payload)?;
                if sha256(&payload) == hash {
                    index.entry(hash).or_insert((pos + RECORD_HEADER_LEN, length));
                } else {
                    skipped += 1;
                }
                pos += RECORD_HEADER_LEN + length as u64;
            }
        }
        if pos < file_len {
            warn!(
                "Truncating chunk blob {:?} from {} to {} bytes (structurally invalid tail)",
                path, file_len, pos
            );
            file.set_len(pos)?;
        }
        if skipped > 0 {
            warn!(
                "Chunk blob {:?} has {} records with corrupt payloads, they will be fetched again",
                path, skipped
            );
        }
        file.seek(SeekFrom::Start(pos))?;
        Ok(ChunkBlob {
            path,
            file,
            index,
            end: pos,
            unflushed: 0,
        })
    }

    /// The path of the blob file.
    pub fn path(&self) -> &Path {
        &self.path
    }

    /// Number of verified chunks in the blob.
    pub fn len(&self) -> usize {
        self.index.len()
    }

    /// True when the blob holds no verified chunks.
    pub fn is_empty(&self) -> bool {
        self.index.is_empty()
    }

    /// Whether a verified record with this hash exists.
    pub fn contains(&self, hash: &[u8; 32]) -> bool {
        self.index.contains_key(hash)
    }

    /// Iterates the hashes of all verified records.
    pub fn hashes(&self) -> impl Iterator<Item = &[u8; 32]> {
        self.index.keys()
    }

    /// Reads a chunk, re-hashing it; a mismatch (or any read error) is reported as "not in blob".
    pub fn read(&self, hash: &[u8; 32]) -> Option<Vec<u8>> {
        let (offset, length) = *self.index.get(hash)?;
        let mut buf = vec![0u8; length as usize];
        match read_exact_at(&self.file, offset, &mut buf) {
            Ok(()) if sha256(&buf) == *hash => Some(buf),
            Ok(()) => {
                warn!("Chunk blob record at {} no longer matches its hash", offset);
                None
            }
            Err(e) => {
                warn!("Failed to read chunk blob record at {}: {}", offset, e);
                None
            }
        }
    }

    /// Appends a verified chunk. The caller has already checked `sha256(data) == hash`.
    /// The record is indexed even if the hash was already present (the newest record wins).
    pub fn append(&mut self, hash: &[u8; 32], data: &[u8]) -> io::Result<()> {
        debug_assert_eq!(&sha256(data), hash);
        let length = data.len() as u32;
        let mut record = Vec::with_capacity(RECORD_HEADER_LEN as usize + data.len());
        record.extend_from_slice(&BLOB_MAGIC);
        record.extend_from_slice(&length.to_le_bytes());
        record.extend_from_slice(hash);
        record.extend_from_slice(data);
        self.file.seek(SeekFrom::Start(self.end))?;
        self.file.write_all(&record)?;
        self.index.insert(*hash, (self.end + RECORD_HEADER_LEN, length));
        self.end += record.len() as u64;
        self.unflushed += record.len() as u64;
        if self.unflushed >= BLOB_FLUSH_INTERVAL {
            self.flush()?;
        }
        Ok(())
    }

    /// Flushes appended data to disk.
    pub fn flush(&mut self) -> io::Result<()> {
        self.file.flush()?;
        self.file.sync_data()?;
        self.unflushed = 0;
        Ok(())
    }
}
