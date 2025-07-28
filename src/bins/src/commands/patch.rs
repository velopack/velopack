use crate::shared::fastzip;
use anyhow::{anyhow, bail, Result};
use std::{
    collections::HashSet,
    fs, io,
    path::{Path, PathBuf},
};

pub fn zstd_patch_single<P1: AsRef<Path>, P2: AsRef<Path>, P3: AsRef<Path>>(old_file: P1, patch_file: P2, output_file: P3) -> Result<()> {
    let old_file = old_file.as_ref();
    let patch_file = patch_file.as_ref();
    let output_file = output_file.as_ref();

    if !old_file.exists() {
        bail!("Old file does not exist: {:?}", old_file);
    }

    if !patch_file.exists() {
        bail!("Patch file does not exist: {:?}", patch_file);
    }

    let dict = fs::read(old_file)?;

    // info!("Loading Dictionary (Size: {})", dict.len());
    let patch = fs::OpenOptions::new().read(true).open(patch_file)?;
    let patch_reader = io::BufReader::new(patch);
    let mut decoder = zstd::Decoder::with_dictionary(patch_reader, &dict)?;

    let window_log = fio_highbit64(dict.len() as u64) + 1;
    if window_log >= 27 {
        info!("Large File detected. Overriding windowLog to {}", window_log);
        decoder.window_log_max(window_log)?;
    }

    // info!("Decoder loaded. Beginning patch...");
    let mut output = fs::OpenOptions::new().write(true).create(true).truncate(true).open(output_file)?;
    io::copy(&mut decoder, &mut output)?;

    // info!("Patch applied successfully.");
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

pub fn delta<P1: AsRef<Path>, P2: AsRef<Path>, P3: AsRef<Path>>(
    old_file: P1,
    delta_files: Vec<&PathBuf>,
    temp_dir: P2,
    output_file: P3,
) -> Result<()> {
    let old_file = old_file.as_ref().to_path_buf();
    let temp_dir = temp_dir.as_ref().to_path_buf();
    let output_file = output_file.as_ref().to_path_buf();

    if !old_file.exists() {
        bail!("Old file does not exist: {:?}", old_file);
    }

    if delta_files.is_empty() {
        bail!("No delta files provided.");
    }

    for delta_file in &delta_files {
        if !delta_file.exists() {
            bail!("Delta file does not exist: {:?}", delta_file);
        }
    }

    let time = simple_stopwatch::Stopwatch::start_new();

    info!("Extracting base package for delta patching: {:?}", temp_dir);
    let work_dir = temp_dir.join("_work");
    fs::create_dir_all(&work_dir)?;
    fastzip::extract_to_directory(&old_file, &work_dir, None)?;

    info!("Base package extracted. {} delta packages to apply.", delta_files.len());

    for (i, delta_file) in delta_files.iter().enumerate() {
        info!("{}: extracting apply delta patch: {:?}", i, delta_file);
        let delta_dir = temp_dir.join(format!("delta_{}", i));
        fs::create_dir_all(&delta_dir)?;
        fastzip::extract_to_directory(&delta_file, &delta_dir, None)?;

        let delta_relative_paths = fastzip::enumerate_files_relative(&delta_dir);
        let mut visited_paths = HashSet::new();

        // apply all the zsdiff patches for files which exist in both the delta and the base package
        for relative_path in &delta_relative_paths {
            if relative_path.starts_with("lib") {
                let file_name = relative_path.file_name().ok_or(anyhow!("Failed to get file name"))?;
                let file_name_str = file_name.to_string_lossy();
                if file_name_str.ends_with(".zsdiff") || file_name_str.ends_with(".diff") || file_name_str.ends_with(".bsdiff") {
                    // this is a zsdiff patch, we need to apply it to the old file
                    let file_without_extension = relative_path.with_extension("");
                    // let shasum_path = delta_dir.join(relative_path).with_extension("shasum");
                    let old_file_path = work_dir.join(&file_without_extension);
                    let patch_file_path = delta_dir.join(&relative_path);
                    let output_file_path = delta_dir.join(&file_without_extension);

                    visited_paths.insert(file_without_extension);

                    if fs::metadata(&patch_file_path)?.len() == 0 {
                        // file has not changed, so we can continue.
                        continue;
                    }

                    if file_name_str.ends_with(".zsdiff") {
                        info!("{}: applying zsdiff patch: {:?}", i, relative_path);
                        zstd_patch_single(&old_file_path, &patch_file_path, &output_file_path)?;
                    } else {
                        bail!("Unsupported patch format: {:?}", relative_path);
                    }

                    fs::rename(&output_file_path, &old_file_path)?;
                } else if file_name_str.ends_with(".shasum") {
                    // skip shasum files
                } else {
                    // if this file is inside the lib folder without a known extension, it is a new file
                    let file_path = delta_dir.join(relative_path);
                    let dest_path = work_dir.join(relative_path);
                    info!("{}: new file: {:?}", i, relative_path);
                    fs::copy(&file_path, &dest_path)?;
                    visited_paths.insert(relative_path.clone());
                }
            } else {
                // if this file is not inside the lib folder, we always copy it over
                let file_path = delta_dir.join(relative_path);
                let dest_path = work_dir.join(relative_path);
                info!("{}: copying metadata file: {:?}", i, relative_path);
                fs::copy(&file_path, &dest_path)?;
                visited_paths.insert(relative_path.clone());
            }
        }

        // anything in the work dir which was not visited is an old / deleted file and should be removed
        let workdir_relative_paths = fastzip::enumerate_files_relative(&work_dir);
        for relative_path in &workdir_relative_paths {
            if !visited_paths.contains(relative_path) {
                let file_to_delete = work_dir.join(relative_path);
                info!("{}: deleting old/removed file: {:?}", i, relative_path);
                let _ = fs::remove_file(file_to_delete); // soft error
            }
        }
    }

    info!("All delta patches applied. Asembling output package at: {:?}", output_file);

    fastzip::compress_directory(&work_dir, &output_file, fastzip::CompressionLevel::fast())?;

    info!("Successfully applied {} delta patches in {}s.", delta_files.len(), time.s());
    Ok(())
}

// NOTE: this is some code to do checksum verification, but it is not being used
// by the current implementation because zstd patching already has checksum verification
//
// let actual_checksum = get_sha1(&output_file_path);
// let expected_checksum = load_release_entry_shasum(&shasum_path)?;
//
// if !actual_checksum.eq_ignore_ascii_case(&expected_checksum) {
//     bail!("Checksum mismatch for: {:?}. Expected: {}, Actual: {}", relative_path, expected_checksum, actual_checksum);
// }
// fn load_release_entry_shasum(file: &PathBuf) -> Result<String> {
//     let raw_text = fs::read_to_string(file)?.trim().to_string();
//     let first_word = raw_text.splitn(2, ' ').next().unwrap();
//     let cleaned = first_word.trim().trim_matches(|c: char| !c.is_ascii_hexdigit());
//     Ok(cleaned.to_string())
// }
//
// fn get_sha1(file: &PathBuf) -> String {
//     let file_bytes = fs::read(file).unwrap();
//     let mut sha1 = sha1_smol::Sha1::new();
//     sha1.update(&file_bytes);
//     sha1.digest().to_string()
// }
