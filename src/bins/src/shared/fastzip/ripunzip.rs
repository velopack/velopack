// Copyright 2022 Google LLC

// Licensed under the Apache License, Version 2.0 <LICENSE-APACHE or
// https://www.apache.org/licenses/LICENSE-2.0> or the MIT license
// <LICENSE-MIT or https://opensource.org/licenses/MIT>, at your
// option. This file may not be copied, modified, or distributed
// except according to those terms.

use std::{
    borrow::Cow,
    fs::File,
    io::{ErrorKind, Read, Seek, SeekFrom},
    path::{Path, PathBuf},
    sync::Mutex,
};

use anyhow::{Context, Result};
use rayon::prelude::*;
use zip::{read::ZipFile, ZipArchive};

use super::{cloneable_seekable_reader::CloneableSeekableReader, progress_updater::ProgressUpdater, UnzipProgressReporter};

pub(crate) fn determine_stream_len<R: Seek>(stream: &mut R) -> std::io::Result<u64> {
    let old_pos = stream.stream_position()?;
    let len = stream.seek(SeekFrom::End(0))?;
    if old_pos != len {
        stream.seek(SeekFrom::Start(old_pos))?;
    }
    Ok(len)
}

/// Options for unzipping.
pub struct UnzipOptions<'a, 'b> {
    /// The destination directory.
    pub output_directory: Option<PathBuf>,
    /// Password if encrypted.
    pub password: Option<String>,
    /// Whether to run in single-threaded mode.
    pub single_threaded: bool,
    /// A filename filter, optionally
    pub filename_filter: Option<Box<dyn FilenameFilter + Sync + 'a>>,
    /// An object to receive notifications of unzip progress.
    pub progress_reporter: Box<dyn UnzipProgressReporter + Sync + 'b>,
}

/// An object which can unzip a zip file, in its entirety, from a local
/// file or from a network stream. It tries to do this in parallel wherever
/// possible.
pub struct UnzipEngine {
    zipfile: Box<dyn UnzipEngineImpl>,
    compressed_length: u64,
    directory_creator: DirectoryCreator,
}

/// Code which can determine whether to unzip a given filename.
pub trait FilenameFilter {
    /// Returns true if the given filename should be unzipped.
    fn should_unzip(&self, filename: &str) -> bool;
}

/// The underlying engine used by the unzipper. This is different
/// for files and URIs.
trait UnzipEngineImpl {
    fn unzip(&mut self, options: UnzipOptions, directory_creator: &DirectoryCreator) -> Vec<anyhow::Error>;

    // Due to lack of RPITIT we'll return a Vec<String> here
    fn list(&self) -> Result<Vec<String>, anyhow::Error>;
}

/// Engine which knows how to unzip a file.
#[derive(Clone)]
struct UnzipFileEngine(ZipArchive<CloneableSeekableReader<File>>);

impl UnzipEngineImpl for UnzipFileEngine {
    fn unzip(&mut self, options: UnzipOptions, directory_creator: &DirectoryCreator) -> Vec<anyhow::Error> {
        unzip_serial_or_parallel(self.0.len(), options, directory_creator, || self.0.clone(), || {})
    }

    fn list(&self) -> Result<Vec<String>, anyhow::Error> {
        list(&self.0)
    }
}

impl UnzipEngine {
    /// Create an unzip engine which knows how to unzip a file.
    pub fn for_file(mut zipfile: File) -> Result<Self> {
        // The following line doesn't actually seem to make any significant
        // performance difference.
        // let zipfile = BufReader::new(zipfile);
        let compressed_length = determine_stream_len(&mut zipfile)?;
        let zipfile = CloneableSeekableReader::new(zipfile);
        Ok(Self {
            zipfile: Box::new(UnzipFileEngine(ZipArchive::new(zipfile)?)),
            compressed_length,
            directory_creator: DirectoryCreator::default(),
        })
    }

    /// The total compressed length that we expect to retrieve over
    /// the network or from the compressed file.
    pub fn zip_length(&self) -> u64 {
        self.compressed_length
    }

    // Perform the unzip.
    pub fn unzip(mut self, options: UnzipOptions) -> Result<()> {
        log::debug!("Starting extract");
        options.progress_reporter.total_bytes_expected(self.compressed_length);
        let errors = self.zipfile.unzip(options, &self.directory_creator);
        // Return the first error code, if any.
        errors.into_iter().next().map(Result::Err).unwrap_or(Ok(()))
    }

    /// List the filenames in the archive
    pub fn list(self) -> Result<impl Iterator<Item = String>> {
        // In future this might be a more dynamic iterator type.
        self.zipfile.list().map(|mut v| {
            // Names are returned in a HashMap iteration order so let's
            // sort thme to be more reasonable
            v.sort();
            v.into_iter()
        })
    }
}

/// Return a list of filenames from the zip. For now this is infallible
/// but provide the option of an error code in case we do something
/// smarter in future.
fn list<'a, T: Read + Seek + 'a>(zip_archive: &ZipArchive<T>) -> Result<Vec<String>> {
    Ok(zip_archive.file_names().map(|s| s.to_string()).collect())
}

fn unzip_serial_or_parallel<'a, T: Read + Seek + 'a>(
    len: usize,
    options: UnzipOptions,
    directory_creator: &DirectoryCreator,
    get_ziparchive_clone: impl Fn() -> ZipArchive<T> + Sync,
    // Call when a file is going to be skipped
    file_skip_callback: impl Fn() + Sync + Send + Clone,
) -> Vec<anyhow::Error> {
    let progress_reporter: &dyn UnzipProgressReporter = options.progress_reporter.as_ref();
    match (options.filename_filter, options.single_threaded) {
        (None, true) => (0..len)
            .map(|i| {
                extract_file_by_index(
                    &get_ziparchive_clone,
                    i,
                    &options.output_directory,
                    &options.password,
                    progress_reporter,
                    directory_creator,
                )
            })
            .filter_map(Result::err)
            .collect(),
        (None, false) => {
            // We use par_bridge here rather than into_par_iter because it turns
            // out to better preserve ordering of the IDs in the input range,
            // i.e. we're more likely to ask our initial threads to act upon
            // file IDs 0, 1, 2, 3, 4, 5 rather than 0, 1000, 2000, 3000 etc.
            // On a device which is CPU-bound or IO-bound (rather than network
            // bound) that's beneficial because we can start to decompress
            // and write data to disk as soon as it arrives from the network.
            (0..len)
                .par_bridge()
                .map(|i| {
                    extract_file_by_index(
                        &get_ziparchive_clone,
                        i,
                        &options.output_directory,
                        &options.password,
                        progress_reporter,
                        directory_creator,
                    )
                })
                .filter_map(Result::err)
                .collect()
        }
        (Some(filename_filter), single_threaded) => {
            // If we have a filename filter, an easy thing would be to
            // iterate through each file index as above, and check to see if its
            // name matches. Unfortunately, that seeks all over the place
            // to get the filename from the local header.
            // Instead, let's get a list of the filenames we need
            // and request them from the zip library directly.
            // As we can't predict their order in the file, this may involve
            // arbitrary rewinds, so let's do it single-threaded.
            if !single_threaded {
                log::warn!("Unzipping specific files - assuming --single-threaded since we currently cannot unzip specific files in a multi-threaded mode. If you need that, consider launching multiple copies of ripunzip in parallel.");
            }
            let mut filenames: Vec<_> = get_ziparchive_clone()
                .file_names()
                .filter(|name| filename_filter.as_ref().should_unzip(name))
                .map(|s| s.to_string())
                .collect();
            // The filenames returned by the file_names() method above are in
            // HashMap iteration order (i.e. random). To avoid creating lots
            // of HTTPS streams for files which are nearby each other in the
            // zip, we'd ideally extract them in order of file position.
            // We have no way of knowing file position (without iterating the
            // whole file) so instead let's sort them and hope that files were
            // zipped in alphabetical order, or close to it. If we're wrong,
            // we'll just end up rewinding, that is, creating extra redundant
            // HTTP(S) streams.
            filenames.sort();
            log::info!("Will unzip {} matching filenames", filenames.len());
            file_skip_callback();

            // let progress_reporter: &dyn UnzipProgressReporter = options.progress_reporter.as_ref();
            filenames
                .into_iter()
                .map(|name| {
                    let myzip: &mut zip::ZipArchive<T> = &mut get_ziparchive_clone();
                    let file: ZipFile<T> = match &options.password {
                        None => myzip.by_name(&name)?,
                        Some(string) => myzip.by_name_decrypt(&name, string.as_bytes())?,
                    };
                    let r = extract_file(file, &options.output_directory, progress_reporter, directory_creator);
                    file_skip_callback();
                    r
                })
                .filter_map(Result::err)
                .collect()
        }
    }
}

fn extract_file_by_index<'a, T: Read + Seek + 'a>(
    get_ziparchive_clone: impl Fn() -> ZipArchive<T> + Sync,
    i: usize,
    output_directory: &Option<PathBuf>,
    password: &Option<String>,
    progress_reporter: &dyn UnzipProgressReporter,
    directory_creator: &DirectoryCreator,
) -> Result<(), anyhow::Error> {
    let myzip: &mut zip::ZipArchive<T> = &mut get_ziparchive_clone();
    let file: ZipFile<T> = match password {
        None => myzip.by_index(i)?,
        Some(string) => myzip.by_index_decrypt(i, string.as_bytes())?,
    };
    extract_file(file, output_directory, progress_reporter, directory_creator)
}

fn extract_file<R: Read>(
    file: ZipFile<R>,
    output_directory: &Option<PathBuf>,
    progress_reporter: &dyn UnzipProgressReporter,
    directory_creator: &DirectoryCreator,
) -> Result<(), anyhow::Error> {
    let name = file.enclosed_name().as_deref().map(Path::to_string_lossy).unwrap_or_else(|| Cow::Borrowed("<unprintable>")).to_string();
    extract_file_inner(file, output_directory, progress_reporter, directory_creator).with_context(|| format!("Failed to extract {name}"))
}

/// Extracts a file from a zip file.
fn extract_file_inner<R: Read>(
    mut file: ZipFile<R>,
    output_directory: &Option<PathBuf>,
    progress_reporter: &dyn UnzipProgressReporter,
    directory_creator: &DirectoryCreator,
) -> Result<()> {
    let name = file.enclosed_name().ok_or_else(|| std::io::Error::new(ErrorKind::Unsupported, "path not safe to extract"))?;
    let display_name = name.display().to_string();
    let out_path = match output_directory {
        Some(output_directory) => output_directory.join(name),
        None => name,
    };
    progress_reporter.extraction_starting(&display_name);
    log::debug!("Start extract of file at {:x}, length {:x}, name {}", file.data_start(), file.compressed_size(), display_name);
    if file.name().ends_with('/') {
        directory_creator.create_dir_all(&out_path)?;
    } else {
        if let Some(parent) = out_path.parent() {
            directory_creator.create_dir_all(parent)?;
        }
        let out_file = File::create(&out_path).with_context(|| "Failed to create file")?;
        // Progress bar strategy. The overall progress across the entire zip file must be
        // denoted in terms of *compressed* bytes, since at the outset we don't know the uncompressed
        // size of each file. Yet, within a given file, we update progress based on the bytes
        // of uncompressed data written, once per 1MB, because that's the information that we happen
        // to have available. So, calculate how many compressed bytes relate to 1MB of uncompressed
        // data, and the remainder.
        let uncompressed_size = file.size();
        let compressed_size = file.compressed_size();
        let mut progress_updater = ProgressUpdater::new(
            |external_progress| {
                progress_reporter.bytes_extracted(external_progress);
            },
            compressed_size,
            uncompressed_size,
            1024 * 1024,
        );
        let mut out_file = progress_streams::ProgressWriter::new(out_file, |bytes_written| progress_updater.progress(bytes_written as u64));
        // Using a BufWriter here doesn't improve performance even on a VM with
        // spinny disks.
        std::io::copy(&mut file, &mut out_file).with_context(|| "Failed to write directory")?;
        progress_updater.finish();
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if let Some(mode) = file.unix_mode() {
            std::fs::set_permissions(&out_path, std::fs::Permissions::from_mode(mode)).with_context(|| "Failed to set permissions")?;
        }
    }
    log::debug!("Finished extract of file at {:x}, length {:x}, name {}", file.data_start(), file.compressed_size(), display_name);
    progress_reporter.extraction_finished(&display_name);
    Ok(())
}

/// An engine used to ensure we don't conflict in creating directories
/// between threads
#[derive(Default)]
struct DirectoryCreator(Mutex<()>);

impl DirectoryCreator {
    fn create_dir_all(&self, path: &Path) -> Result<()> {
        // Fast path - avoid locking if the directory exists
        if path.exists() {
            return Ok(());
        }
        let _exclusivity = self.0.lock().unwrap();
        if path.exists() {
            return Ok(());
        }
        std::fs::create_dir_all(path).with_context(|| "Failed to create directory")
    }
}
