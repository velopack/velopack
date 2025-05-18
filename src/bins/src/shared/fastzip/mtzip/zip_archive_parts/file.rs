use std::io::{Seek, Write};

use super::extra_field::ExtraFields;
use super::super::{CompressionType, platform::VERSION_MADE_BY};

const LOCAL_FILE_HEADER_SIGNATURE: u32 = 0x04034B50;
const CENTRAL_FILE_HEADER_SIGNATURE: u32 = 0x02014B50;

const VERSION_NEEDED_TO_EXTRACT: u16 = 20;

/// Set bit 11 to indicate that the file names are in UTF-8, because all strings in rust are valid
/// UTF-8
const GENERAL_PURPOSE_BIT_FLAG: u16 = 1 << 11;

#[derive(Debug)]
pub struct ZipFile {
    pub header: ZipFileHeader,
    pub data: Vec<u8>,
}

#[derive(Debug)]
pub struct ZipFileHeader {
    pub compression_type: CompressionType,
    pub crc: u32,
    pub uncompressed_size: u32,
    pub filename: String,
    pub file_comment: Option<String>,
    pub external_file_attributes: u32,
    pub extra_fields: ExtraFields,
}

#[derive(Debug)]
pub struct ZipFileNoData {
    pub header: ZipFileHeader,
    pub local_header_offset: u32,
    pub compressed_size: u32,
}

impl ZipFile {
    pub fn write_local_file_header_with_data_consuming<W: Write + Seek>(
        self,
        buf: &mut W,
    ) -> std::io::Result<ZipFileNoData> {
        let local_header_offset = super::stream_position_u32(buf)?;
        self.write_local_file_header_and_data(buf)?;
        let Self { header, data } = self;
        Ok(ZipFileNoData {
            header,
            local_header_offset,
            compressed_size: data.len() as u32,
        })
    }

    const LOCAL_FILE_HEADER_LEN: usize = 30;

    pub fn write_local_file_header_and_data<W: Write>(&self, buf: &mut W) -> std::io::Result<()> {
        // Writing to a temporary in-memory statically sized array first
        let mut header = [0; Self::LOCAL_FILE_HEADER_LEN];
        {
            let mut header_buf: &mut [u8] = &mut header;

            // signature
            header_buf.write_all(&LOCAL_FILE_HEADER_SIGNATURE.to_le_bytes())?;
            // version needed to extract
            header_buf.write_all(&VERSION_NEEDED_TO_EXTRACT.to_le_bytes())?;
            // general purpose bit flag
            header_buf.write_all(&GENERAL_PURPOSE_BIT_FLAG.to_le_bytes())?;
            // compression type
            header_buf.write_all(&(self.header.compression_type as u16).to_le_bytes())?;
            // Last modification time // moved to extra fields
            header_buf.write_all(&0_u16.to_le_bytes())?;
            // Last modification date // moved to extra fields
            header_buf.write_all(&0_u16.to_le_bytes())?;
            // crc
            header_buf.write_all(&self.header.crc.to_le_bytes())?;
            // Compressed size
            debug_assert!(self.data.len() <= u32::MAX as usize);
            header_buf.write_all(&(self.data.len() as u32).to_le_bytes())?;
            // Uncompressed size
            header_buf.write_all(&self.header.uncompressed_size.to_le_bytes())?;
            // Filename size
            debug_assert!(self.header.filename.len() <= u16::MAX as usize);
            header_buf.write_all(&(self.header.filename.len() as u16).to_le_bytes())?;
            // extra field size
            header_buf.write_all(
                &self
                    .header
                    .extra_fields
                    .data_length::<false>()
                    .to_le_bytes(),
            )?;
        }

        buf.write_all(&header)?;

        // Filename
        buf.write_all(self.header.filename.as_bytes())?;
        // Extra field
        self.header.extra_fields.write::<_, false>(buf)?;

        // Data
        buf.write_all(&self.data)?;

        Ok(())
    }

    #[inline]
    pub fn directory(
        mut name: String,
        extra_fields: ExtraFields,
        external_attributes: u16,
        file_comment: Option<String>,
    ) -> Self {
        if !(name.ends_with('/') || name.ends_with('\\')) {
            name += "/"
        };
        Self {
            header: ZipFileHeader {
                compression_type: CompressionType::Stored,
                crc: 0,
                uncompressed_size: 0,
                filename: name,
                external_file_attributes: (external_attributes as u32) << 16,
                extra_fields,
                file_comment,
            },
            data: vec![],
        }
    }
}

impl ZipFileNoData {
    const CENTRAL_DIR_ENTRY_LEN: usize = 46;

    pub fn write_central_directory_entry<W: Write>(&self, buf: &mut W) -> std::io::Result<()> {
        // Writing to a temporary in-memory statically sized array first
        let mut central_dir_entry_header = [0; Self::CENTRAL_DIR_ENTRY_LEN];
        {
            let mut central_dir_entry_buf: &mut [u8] = &mut central_dir_entry_header;

            // signature
            central_dir_entry_buf.write_all(&CENTRAL_FILE_HEADER_SIGNATURE.to_le_bytes())?;
            // version made by
            central_dir_entry_buf.write_all(&VERSION_MADE_BY.to_le_bytes())?;
            // version needed to extract
            central_dir_entry_buf.write_all(&VERSION_NEEDED_TO_EXTRACT.to_le_bytes())?;
            // general purpose bit flag
            central_dir_entry_buf.write_all(&GENERAL_PURPOSE_BIT_FLAG.to_le_bytes())?;
            // compression type
            central_dir_entry_buf
                .write_all(&(self.header.compression_type as u16).to_le_bytes())?;
            // Last modification time // moved to extra fields
            central_dir_entry_buf.write_all(&0_u16.to_le_bytes())?;
            // Last modification date // moved to extra fields
            central_dir_entry_buf.write_all(&0_u16.to_le_bytes())?;
            // crc
            central_dir_entry_buf.write_all(&self.header.crc.to_le_bytes())?;
            // Compressed size
            central_dir_entry_buf.write_all(&self.compressed_size.to_le_bytes())?;
            // Uncompressed size
            central_dir_entry_buf.write_all(&self.header.uncompressed_size.to_le_bytes())?;
            // Filename size
            debug_assert!(self.header.filename.len() <= u16::MAX as usize);
            central_dir_entry_buf.write_all(&(self.header.filename.len() as u16).to_le_bytes())?;
            // extra field size
            central_dir_entry_buf
                .write_all(&self.header.extra_fields.data_length::<true>().to_le_bytes())?;
            // comment size
            central_dir_entry_buf.write_all(
                &(self
                    .header
                    .file_comment
                    .as_ref()
                    .map(|fc| fc.len())
                    .unwrap_or(0) as u16)
                    .to_le_bytes(),
            )?;
            // disk number start
            central_dir_entry_buf.write_all(&0_u16.to_le_bytes())?;
            // internal file attributes
            central_dir_entry_buf.write_all(&0_u16.to_le_bytes())?;
            // external file attributes
            central_dir_entry_buf.write_all(&self.header.external_file_attributes.to_le_bytes())?;
            // relative offset of local header
            central_dir_entry_buf.write_all(&self.local_header_offset.to_le_bytes())?;
        }

        buf.write_all(&central_dir_entry_header)?;

        // Filename
        buf.write_all(self.header.filename.as_bytes())?;
        // Extra field
        self.header.extra_fields.write::<_, true>(buf)?;
        // File comment
        if let Some(file_comment) = &self.header.file_comment {
            buf.write_all(file_comment.as_bytes())?;
        }

        Ok(())
    }
}
