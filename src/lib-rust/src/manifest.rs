use std::io::Cursor;

use semver::Version;
use xml::reader::{EventReader, XmlEvent};

use crate::Error;

#[derive(Debug, derivative::Derivative, Clone)]
#[derivative(Default)]
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
}

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
            Ok(XmlEvent::Characters(text)) => {
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
        return Err(Error::MissingNuspecProperty("id".to_owned()));
    }

    if obj.version == Version::new(0, 0, 0) {
        return Err(Error::MissingNuspecProperty("version".to_owned()));
    }

    #[cfg(target_os = "windows")]
    if obj.main_exe.is_empty() {
        return Err(Error::MissingNuspecProperty("mainExe".to_owned()));
    }

    if obj.title.is_empty() {
        obj.title = obj.id.clone();
    }

    Ok(obj)
}
