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
    let patch = fs::OpenOptions::new().read(true).open(patch_file)?;
    let patch_reader = io::BufReader::new(patch);
    let mut output = fs::OpenOptions::new().write(true).create(true).open(output_file)?;
    let mut decoder = zstd::Decoder::with_dictionary(patch_reader, &dict)?;

    info!("Dictionary Size: {}", dict.len());
    info!("Decoder loaded. Beginning patch...");

    io::copy(&mut decoder, &mut output)?;

    info!("Patch applied successfully.");

    Ok(())
}
