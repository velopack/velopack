use std::{fs, io, path::Path};
use crate::VelopackError;

/// Applies a zstd patch to a single file by loading the patch as a dictionary.
pub fn zstd_patch_single<P1: AsRef<Path>, P2: AsRef<Path>, P3: AsRef<Path>>(old_file: P1, patch_file: P2, output_file: P3) -> Result<(), VelopackError> {
    let old_file = old_file.as_ref();
    let patch_file = patch_file.as_ref();
    let output_file = output_file.as_ref();

    if !old_file.exists() {
        return Err(VelopackError::FileNotFound(old_file.to_string_lossy().to_string()));
    }

    if !patch_file.exists() {
        return Err(VelopackError::FileNotFound(patch_file.to_string_lossy().to_string()));
    }

    let dict = fs::read(old_file)?;

    info!("Loading Dictionary (Size: {})", dict.len());
    let patch = fs::OpenOptions::new().read(true).open(patch_file)?;
    let patch_reader = io::BufReader::new(patch);
    let mut decoder = zstd::Decoder::with_dictionary(patch_reader, &dict)?;

    let window_log = fio_highbit64(dict.len() as u64) + 1;
    if window_log >= 27 {
        info!("Large File detected. Overriding windowLog to {}", window_log);
        decoder.window_log_max(window_log)?;
    }

    info!("Decoder loaded. Beginning patch...");
    let mut output = fs::OpenOptions::new().write(true).create(true).truncate(true).open(output_file)?;
    io::copy(&mut decoder, &mut output)?;

    info!("Patch applied successfully.");
    Ok(())
}

fn fio_highbit64(v: u64) -> u32 {
    let mut count: u32 = 0;
    let mut v = v;
    v >>= 1;
    while v > 0 {
        v >>= 1;
        count += 1;
    }
    return count;
}