#![allow(dead_code)]

mod cloneable_seekable_reader;
mod mtzip;
mod progress_updater;
mod ripunzip;

use anyhow::Result;
pub use mtzip::level::CompressionLevel;
use ripunzip::{UnzipEngine, UnzipOptions};
use std::{
    fs::File,
    path::{Path, PathBuf},
};
use walkdir::WalkDir;

/// A trait of types which wish to hear progress updates on the unzip.
pub trait UnzipProgressReporter: Sync {
    /// Extraction has begun on a file.
    fn extraction_starting(&self, _display_name: &str) {}
    /// Extraction has finished on a file.
    fn extraction_finished(&self, _display_name: &str) {}
    /// The total number of compressed bytes we expect to extract.
    fn total_bytes_expected(&self, _expected: u64) {}
    /// Some bytes of a file have been decompressed. This is probably
    /// the best way to display an overall progress bar. This should eventually
    /// add up to the number you're given using `total_bytes_expected`.
    /// The 'count' parameter is _not_ a running total - you must add up
    /// each call to this function into the running total.
    /// It's a bit unfortunate that we give compressed bytes rather than
    /// uncompressed bytes, but currently we can't calculate uncompressed
    /// bytes without downloading the whole zip file first, which rather
    /// defeats the point.
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

pub fn compress_directory<'b, P1: AsRef<Path>, P2: AsRef<Path>>(target_dir: P1, output_file: P2, level: CompressionLevel) -> Result<()> {
    let target_dir = target_dir.as_ref().to_path_buf();
    let mut zipper = mtzip::ZipArchive::new();
    let workdir_relative_paths = enumerate_files_relative(&target_dir);
    for relative_path in &workdir_relative_paths {
        zipper
            .add_file_from_fs(target_dir.join(&relative_path), relative_path.to_string_lossy().to_string())
            .compression_level(level)
            .done();
    }
    let mut file = File::create(&output_file)?;
    zipper.write_with_rayon(&mut file)?;
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
