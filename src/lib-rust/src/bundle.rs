#![allow(missing_docs)]

use std::io::Cursor;
use std::{
    cell::RefCell,
    fs::{self, File},
    io::{Read, Seek, Write},
    path::{Path, PathBuf},
    rc::Rc,
};

use regex::Regex;
use semver::Version;
use xml::reader::XmlEvent;
use xml::EventReader;
use zip::ZipArchive;

use crate::{misc, Error};

#[cfg(target_os = "macos")]
use std::os::unix::fs::PermissionsExt;

#[cfg(target_os = "windows")]
use normpath::PathExt;

pub trait ReadSeek: Read + Seek {}

impl<T: Read + Seek> ReadSeek for T {}

#[derive(Clone)]
pub struct BundleZip<'a> {
    zip: Rc<RefCell<ZipArchive<Box<dyn ReadSeek + 'a>>>>,
    zip_from_file: bool,
    zip_range: Option<&'a [u8]>,
    file_path: Option<PathBuf>,
    manifest: Option<Manifest>,
}

pub fn load_bundle_from_file<'a, P: AsRef<Path>>(file_name: P) -> Result<BundleZip<'a>, Error> {
    let file_name = file_name.as_ref();
    debug!("Loading bundle from file '{:?}'...", file_name);
    let file = misc::retry_io(|| File::open(file_name))?;
    let cursor: Box<dyn ReadSeek> = Box::new(file);
    let zip = ZipArchive::new(cursor)?;
    Ok(BundleZip {
        zip: Rc::new(RefCell::new(zip)),
        zip_from_file: true,
        file_path: Some(file_name.to_owned()),
        zip_range: None,
        manifest: None,
    })
}

pub fn load_bundle_from_memory<'a>(zip_range: &'a [u8]) -> Result<BundleZip<'a>, Error> {
    info!("Loading bundle from embedded zip...");
    let cursor: Box<dyn ReadSeek> = Box::new(Cursor::new(zip_range));
    let zip = ZipArchive::new(cursor)?;
    Ok(BundleZip {
        zip: Rc::new(RefCell::new(zip)),
        zip_from_file: false,
        zip_range: Some(zip_range),
        file_path: None,
        manifest: None,
    })
}

#[allow(dead_code)]
impl BundleZip<'_> {
    pub fn copy_bundle_to_file<T: AsRef<Path>>(&self, output_file_path: T) -> Result<(), Error> {
        let nupkg_path = output_file_path.as_ref();
        if self.zip_from_file {
            misc::retry_io(|| fs::copy(self.file_path.clone().unwrap(), nupkg_path))?;
        } else {
            misc::retry_io(|| fs::write(nupkg_path, self.zip_range.unwrap()))?;
        }
        Ok(())
    }

    pub fn calculate_size(&self) -> (u64, u64) {
        let mut total_uncompressed_size = 0u64;
        let mut total_compressed_size = 0u64;
        let mut archive = self.zip.borrow_mut();

        for i in 0..archive.len() {
            if let Ok(file) = archive.by_index(i) {
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
        let mut sf = match archive.by_index(splash_idx.unwrap()) {
            Ok(f) => f,
            Err(_) => {
                warn!("Could not find splash image in bundle.");
                return None;
            }
        };

        let mut bytes = Vec::new();
        if sf.read_to_end(&mut bytes).is_err() {
            warn!("Could not find splash image in bundle.");
            return None;
        }

        if bytes.is_empty() {
            warn!("Could not find splash image in bundle.");
            return None;
        }

        Some(bytes)
    }

    pub fn find_zip_file<F>(&self, predicate: F) -> Option<usize>
    where
        F: Fn(&str) -> bool,
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

    pub fn extract_zip_idx_to_path<T: AsRef<Path>>(&self, index: usize, path: T) -> Result<(), Error> {
        self.extract_zip_idx_to_path_with_progress(index, path, |_| {})
    }

    pub fn extract_zip_idx_to_path_with_progress<T: AsRef<Path>, P: Fn(i16)>(&self, index: usize, path: T, progress: P) -> Result<(), Error> {
        let path = path.as_ref();
        debug!("Extracting zip file to path: {:?}", path);
        let p = PathBuf::from(path);
        let parent = p.parent().unwrap();

        if !parent.exists() {
            debug!("Creating parent directory: {:?}", parent);
            misc::retry_io(|| fs::create_dir_all(parent))?;
        }

        let mut archive = self.zip.borrow_mut();
        let mut file = archive.by_index(index)?;
        let total_size = file.size();
        let mut outfile = misc::retry_io(|| File::create(path))?;
        let mut buffer = [0; 64000]; // Use a 64KB buffer; good balance for large/small files.
        let mut bytes_written: u64 = 0;

        debug!("Writing file to disk with 64k buffer: {:?}", path);
        loop {
            let len = file.read(&mut buffer)?;
            if len == 0 {
                break; // End of file
            }
            outfile.write_all(&buffer[..len])?;
            bytes_written += len as u64;
            if total_size > 0 {
                progress(((bytes_written as f64 / total_size as f64) * 100.0) as i16);
            }
        }

        Ok(())
    }

    pub fn extract_zip_predicate_to_path<F, T: AsRef<Path>>(&self, predicate: F, path: T) -> Result<usize, Error>
    where
        F: Fn(&str) -> bool,
    {
        self.extract_zip_predicate_to_path_with_progress(predicate, path, |_| {})
    }

    pub fn extract_zip_predicate_to_path_with_progress<F, T: AsRef<Path>, P: Fn(i16)>(
        &self,
        predicate: F,
        path: T,
        progress: P,
    ) -> Result<usize, Error>
    where
        F: Fn(&str) -> bool,
    {
        let idx = self.find_zip_file(predicate);
        if idx.is_none() {
            return Err(Error::InvalidPackage("(zip bundle predicate not found)".to_owned()));
        }
        let idx = idx.unwrap();
        self.extract_zip_idx_to_path_with_progress(idx, path, progress)?;
        Ok(idx)
    }

    pub fn read_manifest(&mut self) -> Result<Manifest, Error> {
        if let Some(manifest) = &self.manifest {
            return Ok(manifest.clone());
        }

        let nuspec_idx = self
            .find_zip_file(|name| name.ends_with(".nuspec"))
            .ok_or(Error::InvalidPackage("No .nuspec manifest found".into()))?;

        let mut contents = String::new();
        let mut archive = self.zip.borrow_mut();
        archive.by_index(nuspec_idx)?.read_to_string(&mut contents)?;
        let app = read_manifest_from_string(&contents)?;

        self.manifest = Some(app.clone());
        Ok(app)
    }

    pub fn len(&self) -> usize {
        let archive = self.zip.borrow();
        archive.len()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }

    pub fn get_file_names(&self) -> Result<Vec<String>, Error> {
        let mut files: Vec<String> = Vec::new();
        let mut archive = self.zip.borrow_mut();
        for i in 0..archive.len() {
            let file = archive.by_index(i)?;
            let key = file.name();
            files.push(key.to_string());
        }
        Ok(files)
    }

    #[cfg(not(target_os = "linux"))]
    fn create_symlink(link_path: &PathBuf, target_path: &PathBuf) -> Result<(), Error> {
        #[cfg(target_os = "windows")]
        {
            let absolute_path = link_path.parent().unwrap().join(target_path);
            trace!(
                "Creating symlink '{:?}' -> '{:?}', target isfile={}, isdir={}, relative={:?}",
                link_path,
                absolute_path,
                absolute_path.is_file(),
                absolute_path.is_dir(),
                target_path
            );
            if absolute_path.is_file() {
                std::os::windows::fs::symlink_file(target_path, link_path)?;
            } else if absolute_path.is_dir() {
                std::os::windows::fs::symlink_dir(target_path, link_path)?;
            } else {
                return Err(std::io::Error::new(
                    std::io::ErrorKind::NotFound,
                    format!("Target path '{:?}' does not exist or is of an invalid type", absolute_path),
                )
                .into());
            }
        }
        #[cfg(not(target_os = "windows"))]
        {
            std::os::unix::fs::symlink(target_path, link_path)?;
        }
        Ok(())
    }

    /// Extracts all `_ExecutionStub.exe` files from the archive to the given directory,
    /// renaming them by stripping the `_ExecutionStub` suffix (e.g. `Blah_ExecutionStub.exe` -> `Blah.exe`).
    pub fn extract_stubs_to_dir<P: AsRef<Path>>(&self, dir: P) -> Result<(), Error> {
        let dir = dir.as_ref();
        let mut archive = self.zip.borrow_mut();
        let stub_suffix = "_ExecutionStub.exe";
        for i in 0..archive.len() {
            let name = match archive.by_index(i) {
                Ok(f) => f.name().to_string(),
                Err(_) => continue,
            };
            let file_name = match name.rsplit(&['/', '\\']).next() {
                Some(n) if n.ends_with(stub_suffix) => n.to_string(),
                _ => continue,
            };
            let dest_name = file_name.replace(stub_suffix, ".exe");
            let dest_path = dir.join(&dest_name);
            drop(archive);
            info!("Extracting stub '{}' to '{:?}'", file_name, dest_path);
            match self.extract_zip_idx_to_path(i, &dest_path) {
                Ok(_) => info!("Successfully extracted stub executable: {:?}", dest_path),
                Err(e) => warn!("Failed to extract stub executable {:?}: {} (non-fatal)", dest_path, e),
            }
            archive = self.zip.borrow_mut();
        }
        Ok(())
    }

    #[cfg(not(target_os = "linux"))]
    pub fn extract_lib_contents_to_path<P: AsRef<Path>, F: Fn(i16)>(&self, current_path: P, progress: F) -> Result<(), Error> {
        let current_path = current_path.as_ref();
        let files = self.get_file_names()?;
        let num_files = files.len();

        info!("Extracting {} app files to '{:?}'...", num_files, current_path);
        let re = Regex::new(r"lib[\\\/][^\\\/]*[\\\/]").unwrap();
        let stub_regex = Regex::new("_ExecutionStub.exe$").unwrap();
        let symlink_regex = Regex::new(".__symlink$").unwrap();
        let updater_idx = self.find_zip_file(|name| name.ends_with("Squirrel.exe"));

        // for legacy support, we still extract the nuspec file to the current dir.
        // in newer versions, the nuspec is in the current dir in the package itself.
        #[cfg(target_os = "windows")]
        {
            let nuspec_path = current_path.join("sq.version");
            let _ = self
                .extract_zip_predicate_to_path(|name| name.ends_with(".nuspec"), nuspec_path)
                .map_err(|_| Error::InvalidPackage("No .nuspec manifest found".into()))?;
        }

        // we extract the symlinks after, because the target must exist.
        let mut symlinks: Vec<(usize, PathBuf)> = Vec::new();

        for (i, key) in files.iter().enumerate() {
            if Some(i) == updater_idx || !re.is_match(key) || key.ends_with("/") || key.ends_with("\\") {
                debug!("    {} Skipped '{}'", i, key);
                continue;
            }

            let file_path_in_zip = re.replace(key, "").to_string();
            let file_path_on_disk = Path::new(&current_path).join(&file_path_in_zip);

            if symlink_regex.is_match(&file_path_in_zip) {
                let sym_key = symlink_regex.replace(&file_path_in_zip, "").to_string();
                let file_path_on_disk = Path::new(&current_path).join(&sym_key);
                symlinks.push((i, file_path_on_disk));
                continue;
            }

            if stub_regex.is_match(&file_path_in_zip) {
                // let stub_key = stub_regex.replace(&file_path_in_zip, ".exe").to_string();
                // file_path_on_disk = root_path.join(&stub_key);
                debug!("    {} Skipped Stub (obsolete) '{}'", i, key);
                continue;
            }

            // on windows, the zip paths are / and should be \ instead
            #[cfg(target_os = "windows")]
            let file_path_on_disk = file_path_on_disk.normalize_virtually()?.into_path_buf();

            debug!("    {} Extracting '{}' to '{:?}'", i, key, file_path_on_disk);
            self.extract_zip_idx_to_path(i, &file_path_on_disk)?;

            // on macos, we need to chmod +x the executable files
            // for now, we just chmod 755 every file we extract. this is not great, ideally we
            // will preserve the mode as it was when packaging. This will come in a future release.
            #[cfg(target_os = "macos")]
            {
                if let Err(e) = std::fs::set_permissions(&file_path_on_disk, std::fs::Permissions::from_mode(0o755)) {
                    warn!("Failed to set mode 755 on '{:?}': {:?}", file_path_on_disk, e);
                }
            }

            progress(((i as f32 / num_files as f32) * 100.0) as i16);
        }

        // we extract the symlinks after, because the target must exist.
        for (i, link_path) in symlinks {
            let mut archive = self.zip.borrow_mut();
            let mut file = archive.by_index(i)?;
            let mut contents = String::new();
            file.read_to_string(&mut contents)?;
            info!("    {} Creating symlink '{:?}' -> '{}'", i, link_path, contents);

            let contents = contents.trim_end_matches('/');
            #[cfg(target_os = "windows")]
            let contents = contents.replace("/", "\\");
            let contents = PathBuf::from(contents);

            let parent = link_path.parent().unwrap();
            if !parent.exists() {
                debug!("Creating parent directory: {:?}", parent);
                misc::retry_io(|| fs::create_dir_all(parent))?;
            }
            misc::retry_io(|| Self::create_symlink(&link_path, &contents))?;
        }

        Ok(())
    }
}

#[derive(Debug, derivative::Derivative, Clone)]
#[derivative(Default)]
#[allow(missing_docs)]
pub struct Manifest {
    pub id: String,
    #[derivative(Default(value = "Version::new(0, 0, 0)"))]
    pub version: Version,
    pub title: String,
    pub authors: String,
    pub description: String,
    pub machine_architecture: String,
    pub runtime_dependencies: String,
    pub main_exe: String,
    pub os: String,
    pub os_min_version: String,
    pub channel: String,
    pub shortcut_locations: String,
    pub shortcut_aumid: String,
    pub release_notes: String,
    pub release_notes_html: String,
    pub splash_progress_color: String,
}

/// Parse manifest object from an XML string.
pub fn read_manifest_from_string(xml: &str) -> Result<Manifest, Error> {
    let mut obj: Manifest = Default::default();
    let cursor = Cursor::new(xml);
    let parser = EventReader::new(cursor);
    let mut vec: Vec<String> = Vec::new();
    for e in parser {
        match e {
            Ok(XmlEvent::StartElement { name, .. }) => {
                vec.push(name.local_name);
            }
            Ok(XmlEvent::Characters(text)) | Ok(XmlEvent::CData(text)) => {
                if vec.is_empty() {
                    continue;
                }
                let el_name = vec.last().unwrap();
                if el_name == "id" {
                    obj.id = text;
                } else if el_name == "version" {
                    obj.version = Version::parse(&text)?;
                } else if el_name == "title" {
                    obj.title = text;
                } else if el_name == "authors" {
                    obj.authors = text;
                } else if el_name == "description" {
                    obj.description = text;
                } else if el_name == "machineArchitecture" {
                    obj.machine_architecture = text;
                } else if el_name == "runtimeDependencies" {
                    obj.runtime_dependencies = text;
                } else if el_name == "mainExe" {
                    obj.main_exe = text;
                } else if el_name == "os" {
                    obj.os = text;
                } else if el_name == "osMinVersion" {
                    obj.os_min_version = text;
                } else if el_name == "channel" {
                    obj.channel = text;
                } else if el_name == "shortcutLocations" {
                    obj.shortcut_locations = text;
                } else if el_name == "shortcutAumid" {
                    obj.shortcut_aumid = text;
                } else if el_name == "shortcutAmuid" {
                    // legacy typo / backwards compatibility
                    obj.shortcut_aumid = text;
                } else if el_name == "releaseNotes" {
                    obj.release_notes = text;
                } else if el_name == "releaseNotesHtml" {
                    obj.release_notes_html = text;
                } else if el_name == "splashProgressColor" {
                    obj.splash_progress_color = text;
                }
            }
            Ok(XmlEvent::EndElement { .. }) => {
                vec.pop();
            }
            Err(e) => {
                error!("Error: {e}");
                break;
            }
            // There's more: https://docs.rs/xml-rs/latest/xml/reader/enum.XmlEvent.html
            _ => {}
        }
    }

    if obj.id.is_empty() {
        return Err(Error::InvalidPackage("Missing required manifest property: id".into()));
    }

    if obj.version == Version::new(0, 0, 0) {
        return Err(Error::InvalidPackage("Missing required manifest property: version".into()));
    }

    #[cfg(target_os = "windows")]
    if obj.main_exe.is_empty() {
        return Err(Error::InvalidPackage("Missing required manifest property: mainExe".into()));
    }

    if obj.title.is_empty() {
        obj.title = obj.id.clone();
    }

    Ok(obj)
}

#[derive(Debug, Clone, derivative::Derivative)]
#[derivative(Default)]
pub struct EntryNameInfo {
    pub name: String,
    #[derivative(Default(value = "Version::new(0, 0, 0)"))]
    pub version: Version,
    pub is_delta: bool,
    pub file_path: String,
}

impl EntryNameInfo {
    pub fn load_manifest(&self) -> Result<Manifest, Error> {
        let path = Path::new(&self.file_path).to_path_buf();
        let mut bundle = load_bundle_from_file(&path)?;
        bundle.read_manifest()
    }
}

lazy_static::lazy_static! {
    static ref ENTRY_SUFFIX_FULL: Regex = Regex::new(r"(?i)-full.nupkg$").unwrap();
    static ref ENTRY_SUFFIX_DELTA: Regex = Regex::new(r"(?i)-delta.nupkg$").unwrap();
    static ref ENTRY_VERSION_START: Regex = Regex::new(r"[\.-](0|[1-9]\d*)\.(0|[1-9]\d*)($|[^\d])").unwrap();
}

/// Parse a package file path into an EntryNameInfo object. Returns None if couldn't be parsed.
pub fn parse_package_file_path<P: AsRef<Path>>(path: P) -> Option<EntryNameInfo> {
    let path = path.as_ref();
    let name = path.file_name()?.to_string_lossy().to_string();
    if let Some(mut m) = parse_package_file_name(name) {
        m.file_path = path.to_string_lossy().to_string();
        return Some(m);
    }
    None
}

fn parse_package_file_name<T: AsRef<str>>(name: T) -> Option<EntryNameInfo> {
    let name = name.as_ref();
    let full = ENTRY_SUFFIX_FULL.is_match(name);
    let delta = ENTRY_SUFFIX_DELTA.is_match(name);
    if !full && !delta {
        return None;
    }

    let mut entry = EntryNameInfo {
        is_delta: delta,
        ..Default::default()
    };

    let name_and_ver = if full {
        ENTRY_SUFFIX_FULL.replace(name, "")
    } else {
        ENTRY_SUFFIX_DELTA.replace(name, "")
    };
    let ver_idx = ENTRY_VERSION_START.find(&name_and_ver)?;

    let ver_start = ver_idx.start();
    entry.name = name_and_ver[0..ver_start].to_string();
    let ver_start = ver_start + 1;
    let version = name_and_ver[ver_start..].to_string();

    let sv = Version::parse(&version);
    if sv.is_err() {
        return None;
    }

    entry.version = sv.unwrap();
    Some(entry)
}

#[test]
fn test_parse_package_file_name() {
    // test no rid
    let entry = parse_package_file_name("Velopack-1.0.0-full.nupkg").unwrap();
    assert_eq!(entry.name, "Velopack");
    assert_eq!(entry.version, Version::parse("1.0.0").unwrap());
    assert_eq!(entry.is_delta, false);

    let entry = parse_package_file_name("Velopack-1.0.0-delta.nupkg").unwrap();
    assert_eq!(entry.name, "Velopack");
    assert_eq!(entry.version, Version::parse("1.0.0").unwrap());
    assert_eq!(entry.is_delta, true);

    let entry = parse_package_file_name("My.Cool-App-1.1.0-full.nupkg").unwrap();
    assert_eq!(entry.name, "My.Cool-App");
    assert_eq!(entry.version, Version::parse("1.1.0").unwrap());
    assert_eq!(entry.is_delta, false);

    // test invalid names
    assert!(parse_package_file_name("MyCoolApp-1.2.3-beta1-win7-x64-full.nupkg.zip").is_none());
    assert!(parse_package_file_name("MyCoolApp-1.2.3-beta1-win7-x64-full.zip").is_none());
    assert!(parse_package_file_name("MyCoolApp-1.2.3.nupkg").is_none());
    assert!(parse_package_file_name("MyCoolApp-1.2-full.nupkg").is_none());
}

#[test]
fn test_read_manifest_with_cdata_release_notes() {
    let xml = r#"<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
<metadata>
<id>TestApp</id>
<title>Test App</title>
<description>Test App</description>
<authors>test</authors>
<version>1.0.0</version>
<channel></channel>
<mainExe>test.exe</mainExe>
<os>win</os>
<rid>win-x64</rid>
<machineArchitecture>x64</machineArchitecture>
<releaseNotes><![CDATA[
Feature A&B: support for <configuration> files
Fixed: paths with '&' and '<' characters
Unicode: café 🎉
]]></releaseNotes>
</metadata>
</package>"#;

    let manifest = read_manifest_from_string(xml).unwrap();
    assert_eq!(manifest.id, "TestApp");
    assert_eq!(manifest.version, Version::parse("1.0.0").unwrap());
    assert_eq!(manifest.machine_architecture, "x64");
    assert!(manifest.release_notes.contains("Feature A&B"));
    assert!(manifest.release_notes.contains("<configuration>"));
    assert!(manifest.release_notes.contains("café 🎉"));
}

#[test]
fn test_read_manifest_plain_and_cdata_mixed() {
    // Verify that plain text elements still work alongside CDATA elements
    let xml = r#"<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
<metadata>
<id>MixedApp</id>
<title>Mixed App</title>
<description>Mixed App</description>
<authors>author</authors>
<version>2.0.0</version>
<channel>stable</channel>
<mainExe>app.exe</mainExe>
<os>win</os>
<rid>win-x64</rid>
<osMinVersion>10.0.19043</osMinVersion>
<machineArchitecture>x64</machineArchitecture>
<releaseNotes><![CDATA[
Notes with special chars: & < > " '
]]></releaseNotes>
</metadata>
</package>"#;

    let manifest = read_manifest_from_string(xml).unwrap();
    assert_eq!(manifest.id, "MixedApp");
    assert_eq!(manifest.version, Version::parse("2.0.0").unwrap());
    assert_eq!(manifest.os_min_version, "10.0.19043");
    assert_eq!(manifest.machine_architecture, "x64");
    assert_eq!(manifest.channel, "stable");
    assert!(manifest.release_notes.contains("& < > \" '"));
}
