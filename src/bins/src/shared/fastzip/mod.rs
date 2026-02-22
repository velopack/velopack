#![allow(dead_code)]

mod cloneable_seekable_reader;
mod progress_updater;
mod ripunzip;

use anyhow::Result;
use ripunzip::{UnzipEngine, UnzipOptions};
use std::{
    fs::File,
    io::{Read, Write},
    path::{Path, PathBuf},
};
use walkdir::WalkDir;
use zip::{write::SimpleFileOptions, CompressionMethod, ZipWriter};

/// A trait of types which wish to hear progress updates on the unzip.
pub trait UnzipProgressReporter: Sync {
    /// Extraction has begun on a file.
    fn extraction_starting(&self, _display_name: &str) {}
    /// Extraction has finished on a file.
    fn extraction_finished(&self, _display_name: &str) {}
    /// The total number of compressed bytes we expect to extract.
    fn total_bytes_expected(&self, _expected: u64) {}
    /// Some bytes of a file have been decompressed.
    fn bytes_extracted(&self, _count: u64) {}
}

/// A progress reporter which does nothing.
struct NullProgressReporter;

impl UnzipProgressReporter for NullProgressReporter {}

pub fn extract_to_directory<'b, P1: AsRef<Path>, P2: AsRef<Path>>(
    archive_file: P1,
    target_dir: P2,
    progress_reporter: Option<Box<dyn UnzipProgressReporter + Sync + 'b>>,
) -> Result<()> {
    let target_dir = target_dir.as_ref().to_path_buf();
    let file = File::open(archive_file)?;
    let engine = UnzipEngine::for_file(file)?;
    let null_progress = Box::new(NullProgressReporter {});
    let options = UnzipOptions {
        filename_filter: None,
        progress_reporter: progress_reporter.unwrap_or(null_progress),
        output_directory: Some(target_dir),
        password: None,
        single_threaded: false,
    };
    engine.unzip(options)?;
    Ok(())
}

pub fn compress_directory<P1: AsRef<Path>, P2: AsRef<Path>>(target_dir: P1, output_file: P2) -> Result<()> {
    let target_dir = target_dir.as_ref();
    let file = File::create(&output_file)?;
    let mut zip = ZipWriter::new(file);
    let options = SimpleFileOptions::default().compression_method(CompressionMethod::Deflated);

    let relative_paths = enumerate_files_relative(target_dir);
    for relative_path in &relative_paths {
        let full_path = target_dir.join(relative_path);
        let name = relative_path.to_string_lossy();
        // Use forward slashes in zip entries
        let name = name.replace('\\', "/");
        zip.start_file(name, options)?;
        let mut f = File::open(&full_path)?;
        let mut buffer = Vec::new();
        f.read_to_end(&mut buffer)?;
        zip.write_all(&buffer)?;
    }

    zip.finish()?;
    Ok(())
}

pub fn enumerate_files_relative<P: AsRef<Path>>(dir: P) -> Vec<PathBuf> {
    WalkDir::new(&dir)
        .follow_links(false)
        .into_iter()
        .filter_map(|entry| entry.ok())
        .filter(|entry| entry.file_type().is_file())
        .map(|entry| entry.path().strip_prefix(&dir).map(|p| p.to_path_buf()))
        .filter_map(|entry| entry.ok())
        .collect()
}
