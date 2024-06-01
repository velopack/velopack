use std::fs::File;
use std::io::{self, Read};
use std::path::Path;

#[derive(Debug)]
#[allow(non_camel_case_types)]
enum MagicMachO {
    MH_MAGIC = 0xfeedface,
    MH_CIGAM = 0xcefaedfe,
    MH_MAGIC_64 = 0xfeedfacf,
    MH_CIGAM_64 = 0xcffaedfe,
    // https://developer.apple.com/documentation/kernel/fat_header/1558632-magic/
    // https://opensource.apple.com/source/file/file-80.40.2/file/magic/Magdir/cafebabe.auto.html
    FAT_MAGIC = 0xcafebabe,
    FAT_CIGAM = 0xbebafeca,
}

impl MagicMachO {
    fn from_u32(value: u32) -> Option<Self> {
        match value {
            0xfeedface => Some(MagicMachO::MH_MAGIC),
            0xcefaedfe => Some(MagicMachO::MH_CIGAM),
            0xfeedfacf => Some(MagicMachO::MH_MAGIC_64),
            0xcffaedfe => Some(MagicMachO::MH_CIGAM_64),
            0xcafebabe => Some(MagicMachO::FAT_MAGIC),
            0xbebafeca => Some(MagicMachO::FAT_CIGAM),
            _ => None,
        }
    }
}

pub fn is_macho_image<P: AsRef<Path>>(file_path: P) -> io::Result<bool> {
    let file_path = file_path.as_ref();
    let mut file = File::open(file_path)?;
    let mut buffer = [0; 4];

    if file.metadata()?.len() < 256 {
        return Ok(false);
    }

    file.read_exact(&mut buffer)?;
    let magic = u32::from_be_bytes(buffer);
    Ok(MagicMachO::from_u32(magic).is_some())
}
