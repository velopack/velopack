use std::{
    cell::RefCell,
    fs::{self, File},
    io::{Read, Seek, Write},
    path::{Path, PathBuf},
    rc::Rc,
};
use zip::ZipArchive;

use crate::{manifest::*, util, VelopackError};

pub trait ReadSeek: Read + Seek {}

impl<T: Read + Seek> ReadSeek for T {}

#[derive(Clone)]
pub struct BundleZip<'a> {
    zip: Rc<RefCell<ZipArchive<Box<dyn ReadSeek + 'a>>>>,
}

#[allow(dead_code)]
pub fn load_bundle_from_file<'a, P: AsRef<Path>>(file_name: P) -> Result<BundleZip<'a>, VelopackError> {
    let file_name = file_name.as_ref();
    debug!("Loading bundle from file '{}'...", file_name.to_string_lossy());
    let file = util::retry_io(|| File::open(&file_name))?;
    let cursor: Box<dyn ReadSeek> = Box::new(file);
    let zip = ZipArchive::new(cursor)?;
    Ok(BundleZip { zip: Rc::new(RefCell::new(zip)) })
}

#[allow(dead_code)]
impl BundleZip<'_> {
    pub fn calculate_size(&self) -> (u64, u64) {
        let mut total_uncompressed_size = 0u64;
        let mut total_compressed_size = 0u64;
        let mut archive = self.zip.borrow_mut();

        for i in 0..archive.len() {
            let file = archive.by_index(i);
            if file.is_ok() {
                let file = file.unwrap();
                total_uncompressed_size += file.size();
                total_compressed_size += file.compressed_size();
            }
        }

        (total_compressed_size, total_uncompressed_size)
    }

    pub fn get_splash_bytes(&self) -> Option<Vec<u8>> {
        let splash_idx = self.find_zip_file(|name| name.contains("splashimage"));
        if splash_idx.is_none() {
            warn!("Could not find splash image in bundle.");
            return None;
        }

        let mut archive = self.zip.borrow_mut();
        let sf = archive.by_index(splash_idx.unwrap());
        if sf.is_err() {
            warn!("Could not find splash image in bundle.");
            return None;
        }

        let res: Result<Vec<u8>, _> = sf.unwrap().bytes().collect();
        if res.is_err() {
            warn!("Could not find splash image in bundle.");
            return None;
        }

        let bytes = res.unwrap();
        if bytes.is_empty() {
            warn!("Could not find splash image in bundle.");
            return None;
        }

        Some(bytes)
    }

    pub fn find_zip_file<F>(&self, predicate: F) -> Option<usize>
        where F: Fn(&str) -> bool,
    {
        let mut archive = self.zip.borrow_mut();
        for i in 0..archive.len() {
            if let Ok(file) = archive.by_index(i) {
                let name = file.name();
                if predicate(name) {
                    return Some(i);
                }
            }
        }
        None
    }

    pub fn extract_zip_idx_to_path<T: AsRef<Path>>(&self, index: usize, path: T) -> Result<(), VelopackError> {
        let path = path.as_ref();
        debug!("Extracting zip file to path: {}", path.to_string_lossy());
        let p = PathBuf::from(path);
        let parent = p.parent().unwrap();

        if !parent.exists() {
            debug!("Creating parent directory: {:?}", parent);
            util::retry_io(|| fs::create_dir_all(parent))?;
        }

        let mut archive = self.zip.borrow_mut();
        let mut file = archive.by_index(index)?;
        let mut outfile = util::retry_io(|| File::create(path))?;
        let mut buffer = [0; 64000]; // Use a 64KB buffer; good balance for large/small files.

        debug!("Writing file to disk with 64k buffer: {:?}", path);
        loop {
            let len = file.read(&mut buffer)?;
            if len == 0 {
                break; // End of file
            }
            outfile.write_all(&buffer[..len])?;
        }

        Ok(())
    }

    pub fn extract_zip_predicate_to_path<F, T: AsRef<Path>>(&self, predicate: F, path: T) -> Result<usize, VelopackError>
        where F: Fn(&str) -> bool,
    {
        let idx = self.find_zip_file(predicate);
        if idx.is_none() {
            return Err(VelopackError::FileNotFound("(zip bundle predicate)".to_owned()));
        }
        let idx = idx.unwrap();
        self.extract_zip_idx_to_path(idx, path)?;
        Ok(idx)
    }

    pub fn read_manifest(&self) -> Result<Manifest, VelopackError> {
        let nuspec_idx = self.find_zip_file(|name| name.ends_with(".nuspec"))
            .ok_or(VelopackError::MissingNuspec)?;
        
        let mut contents = String::new();
        let mut archive = self.zip.borrow_mut();
        archive.by_index(nuspec_idx)?.read_to_string(&mut contents)?;
        let app = read_manifest_from_string(&contents)?;
        Ok(app)
    }

    pub fn len(&self) -> usize {
        let archive = self.zip.borrow();
        archive.len()
    }

    pub fn get_file_names(&self) -> Result<Vec<String>, VelopackError> {
        let mut files: Vec<String> = Vec::new();
        let mut archive = self.zip.borrow_mut();
        for i in 0..archive.len() {
            let file = archive.by_index(i)?;
            let key = file.name();
            files.push(key.to_string());
        }
        Ok(files)
    }
}
