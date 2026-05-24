#![allow(missing_docs)]

use crate::errors::Error;
use flate2::read::DeflateDecoder;
use semver::Version;
use std::fs::File;
use std::io::{Cursor, Read, Seek, SeekFrom, Write};

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

/// Parse manifest object from an XML string.
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
            Ok(xml::reader::XmlEvent::Characters(text))
            | Ok(xml::reader::XmlEvent::CData(text)) => {
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
                        // legacy typo / backwards compatibility
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
        return Err(Error::InvalidPackage(
            "Missing required manifest property: id".into(),
        ));
    }

    if obj.version == Version::new(0, 0, 0) {
        return Err(Error::InvalidPackage(
            "Missing required manifest property: version".into(),
        ));
    }

    if obj.title.is_empty() {
        obj.title = obj.id.clone();
    }

    Ok(obj)
}

// ---------------------------------------------------------------------------
// Minimal ZIP reader (no `zip` crate -- it does not compile for wasm32-wasip2)
// ---------------------------------------------------------------------------

const EOCD_SIGNATURE: u32 = 0x06054b50;
const CD_SIGNATURE: u32 = 0x02014b50;
const LOCAL_HEADER_SIGNATURE: u32 = 0x04034b50;

const COMPRESSION_STORED: u16 = 0;
const COMPRESSION_DEFLATE: u16 = 8;

/// A single entry from the ZIP central directory.
struct ZipCentralEntry {
    compression_method: u16,
    compressed_size: u32,
    uncompressed_size: u32,
    file_name: String,
    /// Offset of the corresponding local file header within the ZIP file.
    local_header_offset: u32,
}

/// Locate the End of Central Directory record.
/// Scans backwards from the end of the reader (up to 64 KiB + 22 bytes for the
/// comment field).
fn find_eocd<R: Read + Seek>(reader: &mut R) -> Result<(u32, u32), Error> {
    let file_len = reader.seek(SeekFrom::End(0))?;
    // EOCD is at least 22 bytes. Maximum comment length is 65535, so we need
    // to search at most 22 + 65535 bytes from the end.
    let search_len = std::cmp::min(file_len, 22 + 65535) as usize;
    let search_start = file_len - search_len as u64;
    reader.seek(SeekFrom::Start(search_start))?;
    let mut buf = vec![0u8; search_len];
    reader.read_exact(&mut buf)?;

    // Scan backwards for the EOCD signature (4 bytes, little-endian).
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

    let pos = pos.ok_or_else(|| {
        Error::InvalidPackage("ZIP end-of-central-directory record not found".into())
    })?;

    // EOCD layout (after 4-byte signature):
    //  [4..6]   disk number
    //  [6..8]   disk with CD start
    //  [8..10]  number of CD entries on this disk
    // [10..12]  total number of CD entries
    // [12..16]  size of central directory (bytes)
    // [16..20]  offset of central directory start
    if pos + 22 > buf.len() {
        return Err(Error::InvalidPackage(
            "ZIP EOCD record is truncated".into(),
        ));
    }

    let cd_size = u32::from_le_bytes([
        buf[pos + 12],
        buf[pos + 13],
        buf[pos + 14],
        buf[pos + 15],
    ]);
    let cd_offset = u32::from_le_bytes([
        buf[pos + 16],
        buf[pos + 17],
        buf[pos + 18],
        buf[pos + 19],
    ]);

    Ok((cd_offset, cd_size))
}

/// Read all central directory entries from the ZIP file.
fn read_central_directory<R: Read + Seek>(
    reader: &mut R,
) -> Result<Vec<ZipCentralEntry>, Error> {
    let (cd_offset, cd_size) = find_eocd(reader)?;

    reader.seek(SeekFrom::Start(cd_offset as u64))?;
    let mut cd_buf = vec![0u8; cd_size as usize];
    reader.read_exact(&mut cd_buf)?;

    let mut entries = Vec::new();
    let mut cursor = 0usize;

    while cursor + 46 <= cd_buf.len() {
        let sig = u32::from_le_bytes([
            cd_buf[cursor],
            cd_buf[cursor + 1],
            cd_buf[cursor + 2],
            cd_buf[cursor + 3],
        ]);
        if sig != CD_SIGNATURE {
            break;
        }

        let compression_method = u16::from_le_bytes([cd_buf[cursor + 10], cd_buf[cursor + 11]]);
        let compressed_size = u32::from_le_bytes([
            cd_buf[cursor + 20],
            cd_buf[cursor + 21],
            cd_buf[cursor + 22],
            cd_buf[cursor + 23],
        ]);
        let uncompressed_size = u32::from_le_bytes([
            cd_buf[cursor + 24],
            cd_buf[cursor + 25],
            cd_buf[cursor + 26],
            cd_buf[cursor + 27],
        ]);
        let name_len =
            u16::from_le_bytes([cd_buf[cursor + 28], cd_buf[cursor + 29]]) as usize;
        let extra_len =
            u16::from_le_bytes([cd_buf[cursor + 30], cd_buf[cursor + 31]]) as usize;
        let comment_len =
            u16::from_le_bytes([cd_buf[cursor + 32], cd_buf[cursor + 33]]) as usize;
        let local_header_offset = u32::from_le_bytes([
            cd_buf[cursor + 42],
            cd_buf[cursor + 43],
            cd_buf[cursor + 44],
            cd_buf[cursor + 45],
        ]);

        let name_start = cursor + 46;
        let name_end = name_start + name_len;
        if name_end > cd_buf.len() {
            return Err(Error::InvalidPackage(
                "ZIP central directory entry name extends past buffer".into(),
            ));
        }
        let file_name = String::from_utf8_lossy(&cd_buf[name_start..name_end]).to_string();

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

/// Read the raw compressed (or stored) data for a single entry.
/// Seeks to the local file header, skips over the header + variable-length
/// fields, and then reads `compressed_size` bytes.
fn read_entry_data<R: Read + Seek>(
    reader: &mut R,
    entry: &ZipCentralEntry,
) -> Result<Vec<u8>, Error> {
    reader.seek(SeekFrom::Start(entry.local_header_offset as u64))?;

    let mut header = [0u8; 30];
    reader.read_exact(&mut header)?;

    let sig = u32::from_le_bytes([header[0], header[1], header[2], header[3]]);
    if sig != LOCAL_HEADER_SIGNATURE {
        return Err(Error::InvalidPackage(
            "ZIP local file header signature mismatch".into(),
        ));
    }

    let name_len = u16::from_le_bytes([header[26], header[27]]) as u64;
    let extra_len = u16::from_le_bytes([header[28], header[29]]) as u64;

    // Skip past the file name and extra field to reach the data.
    reader.seek(SeekFrom::Current((name_len + extra_len) as i64))?;

    let mut data = vec![0u8; entry.compressed_size as usize];
    reader.read_exact(&mut data)?;

    Ok(data)
}

/// Decompress (or copy for stored) entry data into the final bytes.
fn decompress_entry(entry: &ZipCentralEntry, data: &[u8]) -> Result<Vec<u8>, Error> {
    match entry.compression_method {
        COMPRESSION_STORED => Ok(data.to_vec()),
        COMPRESSION_DEFLATE => {
            let mut decoder = DeflateDecoder::new(data);
            let mut output = Vec::with_capacity(entry.uncompressed_size as usize);
            decoder.read_to_end(&mut output).map_err(|e| {
                Error::InvalidPackage(format!("Failed to decompress entry '{}': {}", entry.file_name, e))
            })?;
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

/// Load a `.nupkg` file and read its `.nuspec` manifest.
pub fn load_manifest_from_file(path: &str) -> Result<Manifest, Error> {
    let mut file = File::open(path)?;
    let entries = read_central_directory(&mut file)?;

    let nuspec_entry = entries
        .iter()
        .find(|e| e.file_name.ends_with(".nuspec"))
        .ok_or_else(|| Error::InvalidPackage("No .nuspec manifest found in package".into()))?;

    let data = read_entry_data(&mut file, nuspec_entry)?;
    let decompressed = decompress_entry(nuspec_entry, &data)?;
    let xml = String::from_utf8(decompressed).map_err(|e| {
        Error::InvalidPackage(format!("Nuspec is not valid UTF-8: {}", e))
    })?;

    read_manifest_from_string(&xml)
}

/// Extract a file from a ZIP archive whose name satisfies `predicate`,
/// writing the decompressed contents to `output_path`.
pub fn extract_zip_file<P: FnMut(&str) -> bool>(
    zip_path: &str,
    mut predicate: P,
    output_path: &str,
) -> Result<(), Error> {
    let mut file = File::open(zip_path)?;
    let entries = read_central_directory(&mut file)?;

    let entry = entries
        .iter()
        .find(|e| predicate(&e.file_name))
        .ok_or_else(|| {
            Error::InvalidPackage(
                "No matching entry found in ZIP archive".into(),
            )
        })?;

    let data = read_entry_data(&mut file, entry)?;
    let decompressed = decompress_entry(entry, &data)?;

    // Ensure the parent directory exists.
    let out = std::path::Path::new(output_path);
    if let Some(parent) = out.parent() {
        if !parent.exists() {
            std::fs::create_dir_all(parent)?;
        }
    }

    let mut out_file = File::create(output_path)?;
    out_file.write_all(&decompressed)?;

    Ok(())
}
