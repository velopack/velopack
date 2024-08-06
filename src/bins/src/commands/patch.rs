use anyhow::{bail, Result};
use std::{fs, io, path::PathBuf};

pub fn patch(old_file: &PathBuf, patch_file: &PathBuf, output_file: &PathBuf) -> Result<()> {
    if !old_file.exists() {
        bail!("Old file does not exist: {}", old_file.to_string_lossy());
    }

    if !patch_file.exists() {
        bail!("Patch file does not exist: {}", patch_file.to_string_lossy());
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
    let mut output = fs::OpenOptions::new().write(true).create(true).open(output_file)?;
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
