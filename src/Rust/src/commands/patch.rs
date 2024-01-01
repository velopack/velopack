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

#[test]
fn test_patch_apply() {

    fn find_fixtures() -> PathBuf {
        let mut path = std::env::current_exe().unwrap();
        while !path.join("Velopack.sln").exists() {
            path.pop();
        }
        path.push("test");
        path.push("fixtures");
        path
    }

    let path = find_fixtures();

    let old_file = path.join("obs29.1.2.dll");
    let new_file = path.join("obs30.0.2.dll");
    let p1 = path.join("obs-size.patch");
    let p2 = path.join("obs-speed.patch");

    fn get_sha1(file: &PathBuf) -> String {
        let file_bytes = fs::read(file).unwrap();
        let mut sha1 = sha1_smol::Sha1::new();
        sha1.update(&file_bytes);
        sha1.digest().to_string()
    }

    let expected_sha1 = get_sha1(&new_file);
    let tmp_file = std::path::Path::new("temp.patch").to_path_buf();

    patch(&old_file, &p1, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);

    patch(&old_file, &p2, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);
}
