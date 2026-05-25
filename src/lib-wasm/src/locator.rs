#![allow(missing_docs)]

use crate::bundle::{self, Manifest};
use crate::errors::Error;
use crate::host_fs;
use crate::types::VelopackLocatorConfig;
use semver::Version;

pub struct VelopackLocator {
    paths: VelopackLocatorConfig,
    manifest: Manifest,
}

impl VelopackLocator {
    pub fn new(config: &VelopackLocatorConfig) -> Result<VelopackLocator, Error> {
        let manifest = read_current_manifest(&config.ManifestPath)?;
        Ok(VelopackLocator {
            paths: config.clone(),
            manifest,
        })
    }

    pub fn get_packages_dir(&self) -> String {
        self.paths.PackagesDir.clone()
    }

    pub fn get_root_dir(&self) -> String {
        self.paths.RootAppDir.clone()
    }

    pub fn get_update_path(&self) -> String {
        self.paths.UpdateExePath.clone()
    }

    pub fn get_manifest(&self) -> &Manifest {
        &self.manifest
    }

    pub fn get_manifest_id(&self) -> String {
        self.manifest.id.clone()
    }

    pub fn get_manifest_version(&self) -> Version {
        self.manifest.version.clone()
    }

    pub fn get_manifest_version_full_string(&self) -> String {
        self.manifest.version.to_string()
    }

    pub fn get_manifest_channel(&self) -> String {
        self.manifest.channel.clone()
    }

    pub fn get_is_portable(&self) -> bool {
        self.paths.IsPortable
    }

    pub fn get_staged_user_id(&self) -> String {
        let beta_id_path = format!("{}/.betaId", self.paths.PackagesDir);
        if let Ok(id) = host_fs::read_to_string(&beta_id_path) {
            if !id.trim().is_empty() {
                return id.trim().to_string();
            }
        }
        let new_id = crate::misc::generate_uuid_v4();
        let _ = host_fs::write_all(&beta_id_path, new_id.as_bytes());
        new_id
    }
}

pub fn find_local_full_packages(packages_dir: &str) -> Vec<(String, Manifest)> {
    let mut results = Vec::new();
    let Ok(entries) = host_fs::list_dir(packages_dir) else {
        return results;
    };
    for name in entries {
        if name.to_lowercase().ends_with("-full.nupkg") {
            let path = format!("{}/{}", packages_dir, name);
            if let Ok(manifest) = bundle::load_manifest_from_file(&path) {
                results.push((path, manifest));
            }
        }
    }
    results
}

pub fn find_latest_full_package(packages_dir: &str) -> Option<(String, Manifest)> {
    find_local_full_packages(packages_dir)
        .into_iter()
        .max_by(|(_, a), (_, b)| a.version.cmp(&b.version))
}

fn read_current_manifest(nuspec_path: &str) -> Result<Manifest, Error> {
    if host_fs::file_exists(nuspec_path) {
        if let Ok(nuspec) = crate::misc::retry_io(|| host_fs::read_to_string(nuspec_path)) {
            return bundle::read_manifest_from_string(&nuspec);
        }
    }
    Err(Error::NotInstalled(format!(
        "Manifest file does not exist or is not readable: {}",
        nuspec_path
    )))
}
