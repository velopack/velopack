#![allow(missing_docs)]

use crate::errors::Error;
use crate::host_fs;
use flate2::read::DeflateDecoder;
use semver::Version;
use std::io::{Cursor, Read, Seek, SeekFrom};

// ---------------------------------------------------------------------------
// Manifest
// ---------------------------------------------------------------------------

#[derive(Debug, Clone)]
pub struct Manifest {
    pub id: String,
    pub version: Version,
    pub title: String,
    pub authors: String,
    pub description: String,
    pub machine_architecture: String,
    pub runtime_dependencies: String,
    pub main_exe: String,
    pub os: String,
    pub os_min_version: String,
    pub channel: String,
    pub shortcut_locations: String,
    pub shortcut_aumid: String,
    pub release_notes: String,
    pub release_notes_html: String,
}

impl Default for Manifest {
    fn default() -> Self {
        Manifest {
            id: String::new(),
            version: Version::new(0, 0, 0),
            title: String::new(),
            authors: String::new(),
            description: String::new(),
            machine_architecture: String::new(),
            runtime_dependencies: String::new(),
            main_exe: String::new(),
            os: String::new(),
            os_min_version: String::new(),
            channel: String::new(),
            shortcut_locations: String::new(),
            shortcut_aumid: String::new(),
            release_notes: String::new(),
            release_notes_html: String::new(),
        }
    }
}

// ---------------------------------------------------------------------------
// XML manifest parsing
// ---------------------------------------------------------------------------

pub fn read_manifest_from_string(xml: &str) -> Result<Manifest, Error> {
    let mut obj = Manifest::default();
    let cursor = Cursor::new(xml);
    let parser = xml::EventReader::new(cursor);
    let mut vec: Vec<String> = Vec::new();
    for e in parser {
        match e {
            Ok(xml::reader::XmlEvent::StartElement { name, .. }) => {
                vec.push(name.local_name);
            }
            Ok(xml::reader::XmlEvent::Characters(text)) | Ok(xml::reader::XmlEvent::CData(text)) => {
                if vec.is_empty() {
                    continue;
                }
                let el_name = vec.last().unwrap();
                match el_name.as_str() {
                    "id" => obj.id = text,
                    "version" => obj.version = Version::parse(&text)?,
                    "title" => obj.title = text,
                    "authors" => obj.authors = text,
                    "description" => obj.description = text,
                    "machineArchitecture" => obj.machine_architecture = text,
                    "runtimeDependencies" => obj.runtime_dependencies = text,
                    "mainExe" => obj.main_exe = text,
                    "os" => obj.os = text,
                    "osMinVersion" => obj.os_min_version = text,
                    "channel" => obj.channel = text,
                    "shortcutLocations" => obj.shortcut_locations = text,
                    "shortcutAumid" => obj.shortcut_aumid = text,
                    "shortcutAmuid" => {
                        obj.shortcut_aumid = text;
                    }
                    "releaseNotes" => obj.release_notes = text,
                    "releaseNotesHtml" => obj.release_notes_html = text,
                    _ => {}
                }
            }
            Ok(xml::reader::XmlEvent::EndElement { .. }) => {
                vec.pop();
            }
            Err(_e) => {
                break;
            }
            _ => {}
        }
    }

    if obj.id.is_empty() {
        return Err(Error::InvalidPackage("Missing required manifest property: id".into()));
    }

    if obj.version == Version::new(0, 0, 0) {
        return Err(Error::InvalidPackage("Missing required manifest property: version".into()));
    }

    if obj.title.is_empty() {
        obj.title = obj.id.clone();
    }

    Ok(obj)
}

// ---------------------------------------------------------------------------
// Host-filesystem backed reader implementing Read + Seek
// ---------------------------------------------------------------------------

struct HostFileReader {
    handle: u32,
    position: u64,
    size: u64,
}

impl HostFileReader {
    fn open(path: &str) -> Result<Self, Error> {
        let size = host_fs::get_file_size(path)?.ok_or_else(|| Error::FileNotFound(path.to_string()))?;
        let handle = host_fs::open(path, false, false)?;
        Ok(HostFileReader { handle, position: 0, size })
    }
}

impl Read for HostFileReader {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        let data = host_fs::read(self.handle, buf.len() as u32).map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e.to_string()))?;
        let n = data.len();
        buf[..n].copy_from_slice(&data);
        self.position += n as u64;
        Ok(n)
    }
}

impl Seek for HostFileReader {
    fn seek(&mut self, pos: SeekFrom) -> std::io::Result<u64> {
        let new_pos = match pos {
            SeekFrom::Start(p) => p,
            SeekFrom::End(offset) => {
                if offset >= 0 {
                    self.size + offset as u64
                } else {
                    self.size
                        .checked_sub((-offset) as u64)
                        .ok_or_else(|| std::io::Error::new(std::io::ErrorKind::InvalidInput, "seek before start"))?
                }
            }
            SeekFrom::Current(offset) => {
                if offset >= 0 {
                    self.position + offset as u64
                } else {
                    self.position
                        .checked_sub((-offset) as u64)
                        .ok_or_else(|| std::io::Error::new(std::io::ErrorKind::InvalidInput, "seek before start"))?
                }
            }
        };
        host_fs::seek(self.handle, new_pos).map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e.to_string()))?;
        self.position = new_pos;
        Ok(new_pos)
    }
}

impl Drop for HostFileReader {
    fn drop(&mut self) {
        let _ = host_fs::close(self.handle);
    }
}

// ---------------------------------------------------------------------------
// Minimal ZIP reader
// ---------------------------------------------------------------------------

const EOCD_SIGNATURE: u32 = 0x06054b50;
const CD_SIGNATURE: u32 = 0x02014b50;
const LOCAL_HEADER_SIGNATURE: u32 = 0x04034b50;
const ZIP64_EOCD_LOCATOR_SIGNATURE: u32 = 0x07064b50;
const ZIP64_EOCD_SIGNATURE: u32 = 0x06064b50;
const ZIP64_EXTRA_FIELD_TAG: u16 = 0x0001;

const COMPRESSION_STORED: u16 = 0;
const COMPRESSION_DEFLATE: u16 = 8;

struct ZipCentralEntry {
    compression_method: u16,
    compressed_size: u64,
    uncompressed_size: u64,
    file_name: String,
    local_header_offset: u64,
}

fn find_eocd<R: Read + Seek>(reader: &mut R) -> Result<(u64, u64), Error> {
    let file_len = reader.seek(SeekFrom::End(0))?;
    let search_len = std::cmp::min(file_len, 22 + 65535) as usize;
    let search_start = file_len - search_len as u64;
    reader.seek(SeekFrom::Start(search_start))?;
    let mut buf = vec![0u8; search_len];
    reader.read_exact(&mut buf)?;

    let sig = EOCD_SIGNATURE.to_le_bytes();
    let mut pos = None;
    if buf.len() >= 4 {
        for i in (0..=(buf.len() - 4)).rev() {
            if buf[i..i + 4] == sig {
                pos = Some(i);
                break;
            }
        }
    }

    let pos = pos.ok_or_else(|| Error::InvalidPackage("ZIP end-of-central-directory record not found".into()))?;

    if pos + 22 > buf.len() {
        return Err(Error::InvalidPackage("ZIP EOCD record is truncated".into()));
    }

    let cd_size = u32::from_le_bytes([buf[pos + 12], buf[pos + 13], buf[pos + 14], buf[pos + 15]]);
    let cd_offset = u32::from_le_bytes([buf[pos + 16], buf[pos + 17], buf[pos + 18], buf[pos + 19]]);

    // Check for ZIP64
    if cd_offset == 0xFFFFFFFF || cd_size == 0xFFFFFFFF {
        if pos >= 20 {
            let loc_pos = pos - 20;
            let loc_sig = ZIP64_EOCD_LOCATOR_SIGNATURE.to_le_bytes();
            if buf[loc_pos..loc_pos + 4] == loc_sig {
                let z64_eocd_offset = u64::from_le_bytes([
                    buf[loc_pos + 8],
                    buf[loc_pos + 9],
                    buf[loc_pos + 10],
                    buf[loc_pos + 11],
                    buf[loc_pos + 12],
                    buf[loc_pos + 13],
                    buf[loc_pos + 14],
                    buf[loc_pos + 15],
                ]);

                reader.seek(SeekFrom::Start(z64_eocd_offset))?;
                let mut z64_buf = [0u8; 56];
                reader.read_exact(&mut z64_buf)?;

                let z64_sig = u32::from_le_bytes([z64_buf[0], z64_buf[1], z64_buf[2], z64_buf[3]]);
                if z64_sig == ZIP64_EOCD_SIGNATURE {
                    let real_cd_size = u64::from_le_bytes([
                        z64_buf[40],
                        z64_buf[41],
                        z64_buf[42],
                        z64_buf[43],
                        z64_buf[44],
                        z64_buf[45],
                        z64_buf[46],
                        z64_buf[47],
                    ]);
                    let real_cd_offset = u64::from_le_bytes([
                        z64_buf[48],
                        z64_buf[49],
                        z64_buf[50],
                        z64_buf[51],
                        z64_buf[52],
                        z64_buf[53],
                        z64_buf[54],
                        z64_buf[55],
                    ]);
                    return Ok((real_cd_offset, real_cd_size));
                }
            }
        }
    }

    Ok((cd_offset as u64, cd_size as u64))
}

fn read_central_directory<R: Read + Seek>(reader: &mut R) -> Result<Vec<ZipCentralEntry>, Error> {
    let (cd_offset, cd_size) = find_eocd(reader)?;

    reader.seek(SeekFrom::Start(cd_offset))?;
    let mut cd_buf = vec![0u8; cd_size as usize];
    reader.read_exact(&mut cd_buf)?;

    let mut entries = Vec::new();
    let mut cursor = 0usize;

    while cursor + 46 <= cd_buf.len() {
        let sig = u32::from_le_bytes([cd_buf[cursor], cd_buf[cursor + 1], cd_buf[cursor + 2], cd_buf[cursor + 3]]);
        if sig != CD_SIGNATURE {
            break;
        }

        let compression_method = u16::from_le_bytes([cd_buf[cursor + 10], cd_buf[cursor + 11]]);
        let mut compressed_size = u32::from_le_bytes([cd_buf[cursor + 20], cd_buf[cursor + 21], cd_buf[cursor + 22], cd_buf[cursor + 23]]) as u64;
        let mut uncompressed_size = u32::from_le_bytes([cd_buf[cursor + 24], cd_buf[cursor + 25], cd_buf[cursor + 26], cd_buf[cursor + 27]]) as u64;
        let name_len = u16::from_le_bytes([cd_buf[cursor + 28], cd_buf[cursor + 29]]) as usize;
        let extra_len = u16::from_le_bytes([cd_buf[cursor + 30], cd_buf[cursor + 31]]) as usize;
        let comment_len = u16::from_le_bytes([cd_buf[cursor + 32], cd_buf[cursor + 33]]) as usize;
        let mut local_header_offset = u32::from_le_bytes([cd_buf[cursor + 42], cd_buf[cursor + 43], cd_buf[cursor + 44], cd_buf[cursor + 45]]) as u64;

        let name_start = cursor + 46;
        let name_end = name_start + name_len;
        if name_end > cd_buf.len() {
            return Err(Error::InvalidPackage("ZIP central directory entry name extends past buffer".into()));
        }
        let file_name = String::from_utf8_lossy(&cd_buf[name_start..name_end]).to_string();

        // Parse ZIP64 extended information extra field if needed
        let extra_start = name_end;
        let extra_end = extra_start + extra_len;
        if extra_end <= cd_buf.len() {
            parse_zip64_extra(
                &cd_buf[extra_start..extra_end],
                &mut uncompressed_size,
                &mut compressed_size,
                &mut local_header_offset,
            );
        }

        entries.push(ZipCentralEntry {
            compression_method,
            compressed_size,
            uncompressed_size,
            file_name,
            local_header_offset,
        });

        cursor = name_end + extra_len + comment_len;
    }

    Ok(entries)
}

fn parse_zip64_extra(extra: &[u8], uncompressed_size: &mut u64, compressed_size: &mut u64, local_header_offset: &mut u64) {
    let mut i = 0;
    while i + 4 <= extra.len() {
        let tag = u16::from_le_bytes([extra[i], extra[i + 1]]);
        let size = u16::from_le_bytes([extra[i + 2], extra[i + 3]]) as usize;
        let data_start = i + 4;
        let data_end = data_start + size;
        if tag == ZIP64_EXTRA_FIELD_TAG && data_end <= extra.len() {
            let mut off = data_start;
            if *uncompressed_size == 0xFFFFFFFF && off + 8 <= data_end {
                *uncompressed_size = u64::from_le_bytes([
                    extra[off],
                    extra[off + 1],
                    extra[off + 2],
                    extra[off + 3],
                    extra[off + 4],
                    extra[off + 5],
                    extra[off + 6],
                    extra[off + 7],
                ]);
                off += 8;
            }
            if *compressed_size == 0xFFFFFFFF && off + 8 <= data_end {
                *compressed_size = u64::from_le_bytes([
                    extra[off],
                    extra[off + 1],
                    extra[off + 2],
                    extra[off + 3],
                    extra[off + 4],
                    extra[off + 5],
                    extra[off + 6],
                    extra[off + 7],
                ]);
                off += 8;
            }
            if *local_header_offset == 0xFFFFFFFF && off + 8 <= data_end {
                *local_header_offset = u64::from_le_bytes([
                    extra[off],
                    extra[off + 1],
                    extra[off + 2],
                    extra[off + 3],
                    extra[off + 4],
                    extra[off + 5],
                    extra[off + 6],
                    extra[off + 7],
                ]);
            }
            return;
        }
        i = data_end;
    }
}

fn read_entry_data<R: Read + Seek>(reader: &mut R, entry: &ZipCentralEntry) -> Result<Vec<u8>, Error> {
    reader.seek(SeekFrom::Start(entry.local_header_offset))?;

    let mut header = [0u8; 30];
    reader.read_exact(&mut header)?;

    let sig = u32::from_le_bytes([header[0], header[1], header[2], header[3]]);
    if sig != LOCAL_HEADER_SIGNATURE {
        return Err(Error::InvalidPackage("ZIP local file header signature mismatch".into()));
    }

    let name_len = u16::from_le_bytes([header[26], header[27]]) as u64;
    let extra_len = u16::from_le_bytes([header[28], header[29]]) as u64;

    reader.seek(SeekFrom::Current((name_len + extra_len) as i64))?;

    let mut data = vec![0u8; entry.compressed_size as usize];
    reader.read_exact(&mut data)?;

    Ok(data)
}

fn decompress_entry(entry: &ZipCentralEntry, data: Vec<u8>) -> Result<Vec<u8>, Error> {
    match entry.compression_method {
        COMPRESSION_STORED => Ok(data),
        COMPRESSION_DEFLATE => {
            let mut decoder = DeflateDecoder::new(data.as_slice());
            let mut output = Vec::with_capacity(entry.uncompressed_size as usize);
            decoder
                .read_to_end(&mut output)
                .map_err(|e| Error::InvalidPackage(format!("Failed to decompress entry '{}': {}", entry.file_name, e)))?;
            Ok(output)
        }
        other => Err(Error::InvalidPackage(format!(
            "Unsupported ZIP compression method {} for entry '{}'",
            other, entry.file_name
        ))),
    }
}

// ---------------------------------------------------------------------------
// Public helpers
// ---------------------------------------------------------------------------

pub fn load_manifest_from_file(path: &str) -> Result<Manifest, Error> {
    let mut reader = HostFileReader::open(path)?;
    let entries = read_central_directory(&mut reader)?;

    let nuspec_entry = entries
        .iter()
        .find(|e| e.file_name.ends_with(".nuspec"))
        .ok_or_else(|| Error::InvalidPackage("No .nuspec manifest found in package".into()))?;

    let data = read_entry_data(&mut reader, nuspec_entry)?;
    let decompressed = decompress_entry(nuspec_entry, data)?;
    let xml = String::from_utf8(decompressed).map_err(|e| Error::InvalidPackage(format!("Nuspec is not valid UTF-8: {}", e)))?;

    read_manifest_from_string(&xml)
}
