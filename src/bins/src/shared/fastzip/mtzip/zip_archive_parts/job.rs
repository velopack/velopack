use std::{
    borrow::Cow,
    fs::File,
    io::Read,
    panic::{RefUnwindSafe, UnwindSafe},
    path::Path,
};

use flate2::{CrcReader, read::DeflateEncoder};

use super::{extra_field::ExtraFields, file::ZipFile};
use super::super::{
    CompressionType, level::CompressionLevel, platform::attributes_from_fs,
    zip_archive_parts::file::ZipFileHeader,
};

pub enum ZipJobOrigin<'a> {
    Directory,
    Filesystem { path: Cow<'a, Path> },
    RawData(Cow<'a, [u8]>),
    Reader(Box<dyn Read + Send + Sync + UnwindSafe + RefUnwindSafe + 'a>),
}

impl core::fmt::Debug for ZipJobOrigin<'_> {
    #[inline]
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            Self::Directory => f.write_str("Directory"),
            Self::Filesystem { path } => f.debug_struct("Filesystem").field("path", &path).finish(),
            Self::RawData(raw_data) => f.debug_tuple("RawData").field(&raw_data).finish(),
            Self::Reader(_reader) => f.debug_tuple("Reader").finish_non_exhaustive(),
        }
    }
}

#[derive(Debug)]
struct FileDigest {
    data: Vec<u8>,
    uncompressed_size: u32,
    crc: u32,
}

#[derive(Debug)]
pub struct ZipJob<'a> {
    pub data_origin: ZipJobOrigin<'a>,
    pub extra_fields: ExtraFields,
    pub archive_path: String,
    pub file_comment: Option<String>,
    pub external_attributes: u16,
    /// Ignored when [`data_origin`](Self::data_origin) is a [`ZipJobOrigin::Directory`]
    pub compression_level: CompressionLevel,
    /// Ignored when [`data_origin`](Self::data_origin) is a [`ZipJobOrigin::Directory`]
    pub compression_type: CompressionType,
}

impl ZipJob<'_> {
    fn compress_file<R: Read>(
        source: R,
        uncompressed_size_approx: Option<u32>,
        compression_type: CompressionType,
        compression_level: CompressionLevel,
    ) -> std::io::Result<FileDigest> {
        let mut crc_reader = CrcReader::new(source);
        let mut data = Vec::with_capacity(uncompressed_size_approx.unwrap_or(0) as usize);
        let uncompressed_size = match compression_type {
            CompressionType::Deflate => {
                let mut encoder = DeflateEncoder::new(&mut crc_reader, compression_level.into());
                encoder.read_to_end(&mut data)?;
                encoder.total_in() as usize
            }
            CompressionType::Stored => crc_reader.read_to_end(&mut data)?,
        };
        debug_assert!(uncompressed_size <= u32::MAX as usize);
        let uncompressed_size = uncompressed_size as u32;
        data.shrink_to_fit();
        let crc = crc_reader.crc().sum();
        Ok(FileDigest {
            data,
            uncompressed_size,
            crc,
        })
    }

    pub fn into_file(self) -> std::io::Result<ZipFile> {
        match self.data_origin {
            ZipJobOrigin::Directory => Ok(ZipFile::directory(
                self.archive_path,
                self.extra_fields,
                self.external_attributes,
                self.file_comment,
            )),
            ZipJobOrigin::Filesystem { path } => {
                let file = File::open(path).unwrap();
                let file_metadata = file.metadata().unwrap();
                let uncompressed_size_approx = file_metadata.len();
                debug_assert!(uncompressed_size_approx <= u32::MAX.into());
                let uncompressed_size_approx = uncompressed_size_approx as u32;
                let external_file_attributes = attributes_from_fs(&file_metadata);
                let mut extra_fields = ExtraFields::new_from_fs(&file_metadata);
                extra_fields.extend(self.extra_fields);

                let FileDigest {
                    data,
                    uncompressed_size,
                    crc,
                } = Self::compress_file(
                    file,
                    Some(uncompressed_size_approx),
                    self.compression_type,
                    self.compression_level,
                )?;
                Ok(ZipFile {
                    header: ZipFileHeader {
                        compression_type: CompressionType::Deflate,
                        crc,
                        uncompressed_size,
                        filename: self.archive_path,
                        external_file_attributes: (external_file_attributes as u32) << 16,
                        extra_fields,
                        file_comment: self.file_comment,
                    },
                    data,
                })
            }
            ZipJobOrigin::RawData(data) => {
                let uncompressed_size_approx = data.len();
                debug_assert!(uncompressed_size_approx <= u32::MAX as usize);
                let uncompressed_size_approx = uncompressed_size_approx as u32;

                let FileDigest {
                    data,
                    uncompressed_size,
                    crc,
                } = Self::compress_file(
                    data.as_ref(),
                    Some(uncompressed_size_approx),
                    self.compression_type,
                    self.compression_level,
                )?;
                Ok(ZipFile {
                    header: ZipFileHeader {
                        compression_type: CompressionType::Deflate,
                        crc,
                        uncompressed_size,
                        filename: self.archive_path,
                        external_file_attributes: (self.external_attributes as u32) << 16,
                        extra_fields: self.extra_fields,
                        file_comment: self.file_comment,
                    },
                    data,
                })
            }
            ZipJobOrigin::Reader(reader) => {
                let FileDigest {
                    data,
                    uncompressed_size,
                    crc,
                } = Self::compress_file(
                    reader,
                    None,
                    self.compression_type,
                    self.compression_level,
                )?;
                Ok(ZipFile {
                    header: ZipFileHeader {
                        compression_type: CompressionType::Deflate,
                        crc,
                        uncompressed_size,
                        filename: self.archive_path,
                        external_file_attributes: (self.external_attributes as u32) << 16,
                        extra_fields: self.extra_fields,
                        file_comment: self.file_comment,
                    },
                    data,
                })
            }
        }
    }
}
