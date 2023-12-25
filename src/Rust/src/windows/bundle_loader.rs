use anyhow::{anyhow, bail, Result};
use memmap2::Mmap;
use winsafe::{self as w, co, prelude::*};

static BUNDLE_PLACEHOLDER: [u8; 48] = [
    0, 0, 0, 0, 0, 0, 0, 0, // 8 bytes for package offset
    0, 0, 0, 0, 0, 0, 0, 0, // 8 bytes for package length
    0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29, // 32 bytes for bundle signature
    0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4, //
    0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5, //
    0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d, //
];

pub fn header_offset_and_length() -> (i64, i64) {
    let offset = i64::from_ne_bytes(BUNDLE_PLACEHOLDER[0..8].try_into().unwrap());
    let length = i64::from_ne_bytes(BUNDLE_PLACEHOLDER[8..16].try_into().unwrap());
    (offset, length)
}

pub fn load_bundle_from_mmap<'a>(mmap: &'a Mmap, debug_pkg: &Option<&PathBuf>) -> Result<BundleInfo<'a>> {
    info!("Reading bundle header...");
    let (offset, length) = header_offset_and_length();
    info!("Bundle offset = {}, length = {}", offset, length);

    let zip_range: &'a [u8] = &mmap[offset as usize..(offset + length) as usize];

    // try to load the bundle from embedded zip
    if offset > 0 && length > 0 {
        info!("Loading bundle from embedded zip...");
        let cursor: Box<dyn ReadSeek> = Box::new(Cursor::new(zip_range));
        let zip = ZipArchive::new(cursor).map_err(|e| anyhow::Error::new(e))?;
        return Ok(BundleInfo { zip: Rc::new(RefCell::new(zip)), zip_from_file: false, zip_range: Some(zip_range), file_path: None });
    }

    // in debug mode only, allow a nupkg to be passed in as the first argument
    if cfg!(debug_assertions) {
        if let Some(pkg) = debug_pkg {
            info!("Loading bundle from debug nupkg file...");
            return load_bundle_from_file(pkg.to_owned());
        }
    }

    bail!("Could not find embedded zip file. Please contact the application author.");
}