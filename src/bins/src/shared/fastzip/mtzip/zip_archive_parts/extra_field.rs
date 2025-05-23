//! ZIP file extra field

use std::{fs::Metadata, io::Write};

/// This is a structure containing [`ExtraField`]s associated with a file or directory in a zip
/// file, mostly used for filesystem properties, and this is the only functionality implemented
/// here.
///
/// The [`new_from_fs`](Self::new_from_fs) method will use the metadata the filesystem provides to
/// construct the collection.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ExtraFields {
    pub(crate) values: Vec<ExtraField>,
}

impl Extend<ExtraField> for ExtraFields {
    fn extend<T: IntoIterator<Item = ExtraField>>(&mut self, iter: T) {
        self.values.extend(iter)
    }
}

impl IntoIterator for ExtraFields {
    type Item = <Vec<ExtraField> as IntoIterator>::Item;
    type IntoIter = <Vec<ExtraField> as IntoIterator>::IntoIter;

    fn into_iter(self) -> Self::IntoIter {
        self.values.into_iter()
    }
}

impl ExtraFields {
    /// Create a new set of [`ExtraField`]s. [`Self::new_from_fs`] should be preferred.
    ///
    /// # Safety
    ///
    /// All fields must have valid values depending on the field type.
    pub unsafe fn new<I>(fields: I) -> Self
    where
        I: IntoIterator<Item = ExtraField>,
    {
        Self { values: fields.into_iter().collect() }
    }

    /// This method will use the filesystem metadata to get the properties that can be stored in
    /// ZIP [`ExtraFields`].
    ///
    /// The behavior is dependent on the target platform. Will return an empty set if the target os
    /// is not Windows or Linux and not of UNIX family.
    pub fn new_from_fs(metadata: &Metadata) -> Self {
        #[cfg(target_os = "windows")]
        {
            return Self::new_windows(metadata);
        }

        #[cfg(target_os = "linux")]
        {
            return Self::new_linux(metadata);
        }

        #[cfg(all(unix, not(target_os = "linux")))]
        {
            return Self::new_unix(metadata);
        }
    }

    #[cfg(target_os = "linux")]
    fn new_linux(metadata: &Metadata) -> Self {
        use std::os::linux::fs::MetadataExt;

        let mod_time = Some(metadata.st_mtime() as i32);
        let ac_time = Some(metadata.st_atime() as i32);
        let cr_time = Some(metadata.st_ctime() as i32);

        let uid = metadata.st_uid();
        let gid = metadata.st_gid();

        Self { values: vec![ExtraField::UnixExtendedTimestamp { mod_time, ac_time, cr_time }, ExtraField::UnixAttrs { uid, gid }] }
    }

    #[cfg(all(unix, not(target_os = "linux")))]
    #[allow(dead_code)]
    fn new_unix(metadata: &Metadata) -> Self {
        use std::os::unix::fs::MetadataExt;

        let mod_time = Some(metadata.mtime() as i32);
        let ac_time = Some(metadata.atime() as i32);
        let cr_time = Some(metadata.ctime() as i32);

        let uid = metadata.uid();
        let gid = metadata.gid();

        Self { values: vec![ExtraField::UnixExtendedTimestamp { mod_time, ac_time, cr_time }, ExtraField::UnixAttrs { uid, gid }] }
    }

    #[cfg(target_os = "windows")]
    fn new_windows(metadata: &Metadata) -> Self {
        use std::os::windows::fs::MetadataExt;

        let mtime = metadata.last_write_time();
        let atime = metadata.last_access_time();
        let ctime = metadata.creation_time();

        Self { values: vec![ExtraField::Ntfs { mtime, atime, ctime }] }
    }

    pub(crate) fn data_length<const CENTRAL_HEADER: bool>(&self) -> u16 {
        self.values.iter().map(|f| 4 + f.field_size::<CENTRAL_HEADER>()).sum()
    }

    pub(crate) fn write<W: Write, const CENTRAL_HEADER: bool>(&self, writer: &mut W) -> std::io::Result<()> {
        for field in &self.values {
            field.write::<_, CENTRAL_HEADER>(writer)?;
        }
        Ok(())
    }
}

/// Extra data that can be associated with a file or directory.
///
/// This library only implements the filesystem properties in NTFS and UNIX format.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExtraField {
    /// NTFS file properties.
    Ntfs {
        /// Last modification timestamp
        mtime: u64,
        /// Last access timestamp
        atime: u64,
        /// File/directory creation timestamp
        ctime: u64,
    },
    /// Info-Zip extended unix timestamp. Each part is optional by definition, but will be
    /// populated by [`ExtraFields::new_from_fs`].
    UnixExtendedTimestamp {
        /// Last modification timestamp
        mod_time: Option<i32>,
        /// Last access timestamp
        ac_time: Option<i32>,
        /// Creation timestamp
        cr_time: Option<i32>,
    },
    /// UNIX file/directory attributes defined by Info-Zip.
    UnixAttrs {
        /// UID of the owner
        uid: u32,
        /// GID of the group
        gid: u32,
    },
}

const MOD_TIME_PRESENT: u8 = 1;
const AC_TIME_PRESENT: u8 = 1 << 1;
const CR_TIME_PRESENT: u8 = 1 << 2;

impl ExtraField {
    #[inline]
    fn header_id(&self) -> u16 {
        match self {
            Self::Ntfs { mtime: _, atime: _, ctime: _ } => 0x000a,
            Self::UnixExtendedTimestamp { mod_time: _, ac_time: _, cr_time: _ } => 0x5455,
            Self::UnixAttrs { uid: _, gid: _ } => 0x7875,
        }
    }

    #[inline]
    const fn optional_field_size<T: Sized>(field: &Option<T>) -> u16 {
        match field {
            Some(_) => std::mem::size_of::<T>() as u16,
            None => 0,
        }
    }

    #[inline]
    const fn field_size<const CENTRAL_HEADER: bool>(&self) -> u16 {
        match self {
            Self::Ntfs { mtime: _, atime: _, ctime: _ } => 32,
            Self::UnixExtendedTimestamp { mod_time, ac_time, cr_time } => {
                1 + Self::optional_field_size(mod_time) + {
                    if !CENTRAL_HEADER {
                        Self::optional_field_size(ac_time) + Self::optional_field_size(cr_time)
                    } else {
                        0
                    }
                }
            }
            Self::UnixAttrs { uid: _, gid: _ } => 11,
        }
    }

    #[inline]
    const fn if_present(val: Option<i32>, if_present: u8) -> u8 {
        match val {
            Some(_) => if_present,
            None => 0,
        }
    }

    const NTFS_FIELD_LEN: usize = 32;
    const UNIX_ATTRS_LEN: usize = 11;

    pub(crate) fn write<W: Write, const CENTRAL_HEADER: bool>(self, writer: &mut W) -> std::io::Result<()> {
        // Header ID
        writer.write_all(&self.header_id().to_le_bytes())?;
        // Field data size
        writer.write_all(&self.field_size::<CENTRAL_HEADER>().to_le_bytes())?;

        match self {
            Self::Ntfs { mtime, atime, ctime } => {
                // Writing to a temporary in-memory array
                let mut field = [0; Self::NTFS_FIELD_LEN];
                {
                    let mut field_buf: &mut [u8] = &mut field;

                    // Reserved field
                    field_buf.write_all(&0_u32.to_le_bytes())?;

                    // Tag1 number
                    field_buf.write_all(&1_u16.to_le_bytes())?;
                    // Tag1 size
                    field_buf.write_all(&24_u16.to_le_bytes())?;

                    // Mtime
                    field_buf.write_all(&mtime.to_le_bytes())?;
                    // Atime
                    field_buf.write_all(&atime.to_le_bytes())?;
                    // Ctime
                    field_buf.write_all(&ctime.to_le_bytes())?;
                }

                writer.write_all(&field)?;
            }
            Self::UnixExtendedTimestamp { mod_time, ac_time, cr_time } => {
                let flags = Self::if_present(mod_time, MOD_TIME_PRESENT)
                    | Self::if_present(ac_time, AC_TIME_PRESENT)
                    | Self::if_present(cr_time, CR_TIME_PRESENT);
                writer.write_all(&[flags])?;
                if let Some(mod_time) = mod_time {
                    writer.write_all(&mod_time.to_le_bytes())?;
                }
                if !CENTRAL_HEADER {
                    if let Some(ac_time) = ac_time {
                        writer.write_all(&ac_time.to_le_bytes())?;
                    }
                    if let Some(cr_time) = cr_time {
                        writer.write_all(&cr_time.to_le_bytes())?;
                    }
                }
            }
            Self::UnixAttrs { uid, gid } => {
                // Writing to a temporary in-memory array
                let mut field = [0; Self::UNIX_ATTRS_LEN];
                {
                    let mut field_buf: &mut [u8] = &mut field;

                    // Version of the field
                    field_buf.write_all(&[1])?;
                    // UID size
                    field_buf.write_all(&[4])?;
                    // UID
                    field_buf.write_all(&uid.to_le_bytes())?;
                    // GID size
                    field_buf.write_all(&[4])?;
                    // GID
                    field_buf.write_all(&gid.to_le_bytes())?;
                }

                writer.write_all(&field)?;
            }
        }

        Ok(())
    }
}
