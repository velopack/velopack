//! # mtzip
//!
//! MTZIP (Stands for Multi-Threaded ZIP) is a library for making zip archives while utilising all
//! available performance available with multithreading. The amount of threads can be limited by
//! the user or detected automatically.
//!
//! Example usage:
//!
//! ```ignore
//! # use std::path::Path;
//! # use std::fs::File;
//! use mtzip::ZipArchive;
//!
//! // Creating the zipper that holds data and handles compression
//! let mut zipper = ZipArchive::new();
//!
//! // Adding a file from filesystem
//! zipper.add_file_from_fs(
//!     Path::new("input/test_text_file.txt"),
//!     "test_text_file.txt".to_owned(),
//! );
//!
//! // Adding a file with data from a memory location
//! zipper.add_file_from_memory(b"Hello, world!", "hello_world.txt".to_owned());
//!
//! // Adding a directory and a file to it
//! zipper.add_directory("test_dir".to_owned());
//! zipper.add_file_from_fs(
//!     Path::new("input/file_that_goes_to_a_dir.txt"),
//!     "test_dir/file_that_goes_to_a_dir.txt".to_owned(),
//! );
//!
//! // Writing to a file
//! // First, open the file
//! let mut file = File::create("output.zip").unwrap();
//! // Then, write to it
//! zipper.write(&mut file); // Amount of threads is chosen automatically
//! ```

use std::{
    borrow::Cow,
    io::{Read, Seek, Write},
    num::NonZeroUsize,
    panic::{RefUnwindSafe, UnwindSafe},
    path::Path,
    sync::{mpsc, Mutex},
};

use level::CompressionLevel;
use rayon::prelude::*;
use zip_archive_parts::{
    data::ZipData,
    extra_field::{ExtraField, ExtraFields},
    file::ZipFile,
    job::{ZipJob, ZipJobOrigin},
};

pub mod level;
mod platform;
mod zip_archive_parts;

// TODO: tests, maybe examples

/// Compression type for the file. Directories always use [`Stored`](CompressionType::Stored).
/// Default is [`Deflate`](CompressionType::Deflate).
#[repr(u16)]
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
pub enum CompressionType {
    /// No compression at all, the data is stored as-is.
    ///
    /// This is used for directories because they have no data (no payload)
    Stored = 0,
    #[default]
    /// Deflate compression, the most common in ZIP files.
    Deflate = 8,
}

/// Builder used to optionally add additional attributes to a file or directory.
/// The default compression type is [`CompressionType::Deflate`] and default compression level is
/// [`CompressionLevel::best`]
#[must_use]
#[derive(Debug)]
pub struct ZipFileBuilder<'a, 'b> {
    archive_handle: &'a mut ZipArchive<'b>,
    job: ZipJob<'b>,
}

impl<'a, 'b> ZipFileBuilder<'a, 'b> {
    /// Call this when you're done configuring the file entry and it will be added to the job list,
    /// or directly into the resulting dataset if it's a directory. Always needs to be called.
    pub fn done(self) {
        let Self { archive_handle, job } = self;
        match &job.data_origin {
            ZipJobOrigin::Directory => {
                let file = job.into_file().expect("No failing code path");
                archive_handle.push_file(file);
            }
            _ => archive_handle.push_job(job),
        }
    }

    /// Read filesystem metadata from filesystem and add the properties to this file. It sets
    /// external attributes (as with [`Self::external_attributes`]) and adds extra fields generated
    /// with [`ExtraFields::new_from_fs`]
    pub fn metadata_from_fs(self, fs_path: &Path) -> std::io::Result<Self> {
        let metadata = std::fs::metadata(fs_path)?;
        let external_attributes = platform::attributes_from_fs(&metadata);
        let extra_fields = ExtraFields::new_from_fs(&metadata);
        Ok(self.external_attributes(external_attributes).extra_fields(extra_fields))
    }

    /// Add a file comment.
    pub fn file_comment(mut self, comment: String) -> Self {
        self.job.file_comment = Some(comment);
        self
    }

    /// Add additional [`ExtraField`].
    pub fn extra_field(mut self, extra_field: ExtraField) -> Self {
        self.job.extra_fields.values.push(extra_field);
        self
    }

    /// Add additional [`ExtraField`]s.
    pub fn extra_fields(mut self, extra_fields: impl IntoIterator<Item = ExtraField>) -> Self {
        self.job.extra_fields.extend(extra_fields);
        self
    }

    /// Set compression type. Ignored for directories, as they use no compression.
    ///
    /// Default is [`CompressionType::Deflate`].
    pub fn compression_type(mut self, compression_type: CompressionType) -> Self {
        self.job.compression_type = compression_type;
        self
    }

    /// Set compression level. Ignored for directories, as they use no compression.
    ///
    /// Default is [`CompressionLevel::best`]
    pub fn compression_level(mut self, compression_level: CompressionLevel) -> Self {
        self.job.compression_level = compression_level;
        self
    }

    /// Set external attributes. The format depends on a filesystem and is mostly a legacy
    /// mechanism, usually a default value is used if this is not a filesystem source. When a file
    /// is added from the filesystem, these attributes will be read and used and the ones set wit
    /// hthis method are ignored.
    pub fn external_attributes(mut self, external_attributes: u16) -> Self {
        self.job.external_attributes = external_attributes;
        self
    }

    /// Set external file attributes from a filesystem item. Use of this method is discouraged in
    /// favor of [`Self::metadata_from_fs`], which also sets extra fields which contain modern
    /// filesystem attributes instead of using old 16-bit system-dependent format.
    pub fn external_attributes_from_fs(mut self, fs_path: &Path) -> std::io::Result<Self> {
        let metadata = std::fs::metadata(fs_path)?;
        self.job.external_attributes = platform::attributes_from_fs(&metadata);
        Ok(self)
    }

    #[inline]
    fn new(archive: &'a mut ZipArchive<'b>, filename: String, origin: ZipJobOrigin<'b>) -> Self {
        Self {
            archive_handle: archive,
            job: ZipJob {
                data_origin: origin,
                archive_path: filename,
                extra_fields: ExtraFields::default(),
                file_comment: None,
                external_attributes: platform::default_file_attrs(),
                compression_type: CompressionType::Deflate,
                compression_level: CompressionLevel::best(),
            },
        }
    }

    #[inline]
    fn new_dir(archive: &'a mut ZipArchive<'b>, filename: String) -> Self {
        Self {
            archive_handle: archive,
            job: ZipJob {
                data_origin: ZipJobOrigin::Directory,
                archive_path: filename,
                extra_fields: ExtraFields::default(),
                file_comment: None,
                external_attributes: platform::default_dir_attrs(),
                compression_type: CompressionType::Deflate,
                compression_level: CompressionLevel::best(),
            },
        }
    }
}

/// Structure that holds the current state of ZIP archive creation.
///
/// # Lifetimes
///
/// Because some of the methods allow supplying borrowed data, the lifetimes are used to indicate
/// that [`Self`](ZipArchive) borrows them. If you only provide owned data, such as
/// [`Vec<u8>`](Vec) or [`PathBuf`](std::path::PathBuf), you won't have to worry about lifetimes
/// and can simply use `'static`, if you ever need to specify them in your code.
///
/// The lifetime `'a` is for the borrowed data passed in
/// [`add_file_from_memory`](Self::add_file_from_memory),
/// [`add_file_from_fs`](Self::add_file_from_fs) and
/// [`add_file_from_reader`](Self::add_file_from_reader)
#[derive(Debug, Default)]
pub struct ZipArchive<'a> {
    jobs_queue: Vec<ZipJob<'a>>,
    data: ZipData,
}

impl<'a> ZipArchive<'a> {
    fn push_job(&mut self, job: ZipJob<'a>) {
        self.jobs_queue.push(job);
    }

    fn push_file(&mut self, file: ZipFile) {
        self.data.files.push(file);
    }

    /// Create an empty [`ZipArchive`]
    #[inline]
    pub fn new() -> Self {
        Self::default()
    }

    /// Add file from filesystem.
    ///
    /// Opens the file and reads data from it when [`compress`](Self::compress) is called.
    ///
    /// ```
    /// # use mtzip::ZipArchive;
    /// # use std::path::Path;
    /// let mut zipper = ZipArchive::new();
    /// zipper
    ///     .add_file_from_fs(Path::new("input.txt"), "input.txt".to_owned())
    ///     .done();
    /// ```
    #[inline]
    pub fn add_file_from_fs(&mut self, fs_path: impl Into<Cow<'a, Path>>, archived_path: String) -> ZipFileBuilder<'_, 'a> {
        ZipFileBuilder::new(self, archived_path, ZipJobOrigin::Filesystem { path: fs_path.into() })
    }

    /// Add file with data from memory.
    ///
    /// The data can be either borrowed or owned by the [`ZipArchive`] struct to avoid lifetime
    /// hell.
    ///
    /// ```
    /// # use mtzip::ZipArchive;
    /// # use std::path::Path;
    /// let mut zipper = ZipArchive::new();
    /// let data: &[u8] = "Hello, world!".as_ref();
    /// zipper
    ///     .add_file_from_memory(data, "hello_world.txt".to_owned())
    ///     .done();
    /// ```
    #[inline]
    pub fn add_file_from_memory(&mut self, data: impl Into<Cow<'a, [u8]>>, archived_path: String) -> ZipFileBuilder<'_, 'a> {
        ZipFileBuilder::new(self, archived_path, ZipJobOrigin::RawData(data.into()))
    }

    /// Add a file with data from a reader.
    ///
    /// This method takes any type implementing [`Read`] and allows it to have borrowed data (`'r`)
    ///
    /// ```
    /// # use mtzip::ZipArchive;
    /// # use std::path::Path;
    /// let mut zipper = ZipArchive::new();
    /// let data_input = std::io::stdin();
    /// zipper
    ///     .add_file_from_reader(data_input, "stdin_file.txt".to_owned())
    ///     .done();
    /// ```
    #[inline]
    pub fn add_file_from_reader<R: Read + Send + Sync + UnwindSafe + RefUnwindSafe + 'a>(
        &mut self,
        reader: R,
        archived_path: String,
    ) -> ZipFileBuilder<'_, 'a> {
        ZipFileBuilder::new(self, archived_path, ZipJobOrigin::Reader(Box::new(reader)))
    }

    /// Add a directory entry.
    ///
    /// All directories in the tree should be added. This method does not asssociate any filesystem
    /// properties to the entry.
    ///
    /// ```
    /// # use mtzip::ZipArchive;
    /// # use std::path::Path;
    /// let mut zipper = ZipArchive::new();
    /// zipper.add_directory("test_dir/".to_owned()).done();
    /// ```
    #[inline]
    pub fn add_directory(&mut self, archived_path: String) -> ZipFileBuilder<'_, 'a> {
        ZipFileBuilder::new_dir(self, archived_path)
    }

    /// Compress contents. Will be done automatically on [`write`](Self::write) call if files were
    /// added between last compression and [`write`](Self::write) call. Automatically chooses
    /// amount of threads to use based on how much are available.
    #[inline]
    pub fn compress(&mut self) {
        self.compress_with_threads(Self::get_threads());
    }

    /// Compress contents. Will be done automatically on
    /// [`write_with_threads`](Self::write_with_threads) call if files were added between last
    /// compression and [`write`](Self::write). Allows specifying amount of threads that will be
    /// used.
    ///
    /// Example of getting amount of threads that this library uses in
    /// [`compress`](Self::compress):
    ///
    /// ```
    /// # use std::num::NonZeroUsize;
    /// # use mtzip::ZipArchive;
    /// # let mut zipper = ZipArchive::new();
    /// let threads = std::thread::available_parallelism()
    ///     .map(NonZeroUsize::get)
    ///     .unwrap_or(1);
    ///
    /// zipper.compress_with_threads(threads);
    /// ```
    #[inline]
    pub fn compress_with_threads(&mut self, threads: usize) {
        if !self.jobs_queue.is_empty() {
            self.compress_with_consumer(threads, |zip_data, rx| zip_data.files.extend(rx))
        }
    }

    /// Write compressed data to a writer (usually a file). Executes [`compress`](Self::compress)
    /// if files were added between last [`compress`](Self::compress) call and this call.
    /// Automatically chooses the amount of threads cpu has.
    #[inline]
    pub fn write<W: Write + Seek>(&mut self, writer: &mut W) -> std::io::Result<()> {
        self.write_with_threads(writer, Self::get_threads())
    }

    /// Write compressed data to a writer (usually a file). Executes
    /// [`compress_with_threads`](Self::compress_with_threads) if files were added between last
    /// [`compress`](Self::compress) call and this call. Allows specifying amount of threads that
    /// will be used.
    ///
    /// Example of getting amount of threads that this library uses in [`write`](Self::write):
    ///
    /// ```
    /// # use std::num::NonZeroUsize;
    /// # use mtzip::ZipArchive;
    /// # let mut zipper = ZipArchive::new();
    /// let threads = std::thread::available_parallelism()
    ///     .map(NonZeroUsize::get)
    ///     .unwrap_or(1);
    ///
    /// zipper.compress_with_threads(threads);
    /// ```
    #[inline]
    pub fn write_with_threads<W: Write + Seek>(&mut self, writer: &mut W, threads: usize) -> std::io::Result<()> {
        if !self.jobs_queue.is_empty() {
            self.compress_with_consumer(threads, |zip_data, rx| zip_data.write(writer, rx))
        } else {
            self.data.write(writer, std::iter::empty())
        }
    }

    /// Starts the compression jobs and passes teh mpsc receiver to teh consumer function, which
    /// might either store the data in [`ZipData`] - [`Self::compress_with_threads`]; or write the
    /// zip data as soon as it's available - [`Self::write_with_threads`]
    fn compress_with_consumer<F, T>(&mut self, threads: usize, consumer: F) -> T
    where
        F: FnOnce(&mut ZipData, mpsc::Receiver<ZipFile>) -> T,
    {
        let jobs_drain = Mutex::new(self.jobs_queue.drain(..));
        let jobs_drain_ref = &jobs_drain;
        std::thread::scope(|s| {
            let rx = {
                let (tx, rx) = mpsc::channel();
                for _ in 0..threads {
                    let thread_tx = tx.clone();
                    s.spawn(move || loop {
                        let next_job = jobs_drain_ref.lock().unwrap().next_back();
                        if let Some(job) = next_job {
                            thread_tx.send(job.into_file().unwrap()).unwrap();
                        } else {
                            break;
                        }
                    });
                }
                rx
            };
            consumer(&mut self.data, rx)
        })
    }

    fn get_threads() -> usize {
        std::thread::available_parallelism().map(NonZeroUsize::get).unwrap_or(1)
    }
}

impl ZipArchive<'_> {
    /// Compress contents and use rayon for parallelism.
    ///
    /// Uses whatever thread pool this function is executed in.
    ///
    /// If you want to limit the amount of threads to be used, use
    /// [`rayon::ThreadPoolBuilder::num_threads`] and either set it as a global pool, or
    /// [`rayon::ThreadPool::install`] the call to this method in it.
    pub fn compress_with_rayon(&mut self) {
        if !self.jobs_queue.is_empty() {
            let files_par_iter = self.jobs_queue.par_drain(..).map(|job| job.into_file().unwrap());
            self.data.files.par_extend(files_par_iter)
        }
    }

    /// Write the contents to a writer.
    ///
    /// This method uses teh same thread logic as [`Self::compress_with_rayon`], refer to  its
    /// documentation for details on how to control the parallelism and thread allocation.
    pub fn write_with_rayon<W: Write + Seek + Send>(&mut self, writer: &mut W) -> std::io::Result<()> {
        if !self.jobs_queue.is_empty() {
            let files_par_iter = self.jobs_queue.par_drain(..).map(|job| job.into_file().unwrap());
            self.data.write_rayon(writer, files_par_iter)
        } else {
            self.data.write_rayon(writer, rayon::iter::empty())
        }
    }
}
