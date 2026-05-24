#![allow(missing_docs)]

use crate::bundle::{self, Manifest};
use crate::errors::Error;
use crate::types::VelopackLocatorConfig;
use semver::Version;
use std::path::PathBuf;

/// Simplified locator for WASM.
///
/// No auto-detection, no Windows fallbacks, no glob.
/// The host always provides paths via [`VelopackLocatorConfig`].
pub struct VelopackLocator {
    paths: VelopackLocatorConfig,
    manifest: Manifest,
}

impl VelopackLocator {
    /// Create a new locator from the host-supplied configuration.
    /// Reads and parses the manifest from `config.ManifestPath`.
    pub fn new(config: &VelopackLocatorConfig) -> Result<VelopackLocator, Error> {
        let manifest = read_current_manifest(&config.ManifestPath)?;
        Ok(VelopackLocator {
            paths: config.clone(),
            manifest,
        })
    }

    /// Returns the path to the packages directory.
    pub fn get_packages_dir(&self) -> String {
        self.paths.PackagesDir.clone()
    }

    /// Returns the root application directory.
    pub fn get_root_dir(&self) -> String {
        self.paths.RootAppDir.clone()
    }

    /// Returns the path to the update binary.
    pub fn get_update_path(&self) -> String {
        self.paths.UpdateExePath.clone()
    }

    /// Returns a reference to the parsed manifest.
    pub fn get_manifest(&self) -> &Manifest {
        &self.manifest
    }

    /// Returns the application identifier from the manifest.
    pub fn get_manifest_id(&self) -> String {
        self.manifest.id.clone()
    }

    /// Returns the current application version.
    pub fn get_manifest_version(&self) -> Version {
        self.manifest.version.clone()
    }

    /// Returns the full version string (e.g. "1.2.3-beta.1").
    pub fn get_manifest_version_full_string(&self) -> String {
        self.manifest.version.to_string()
    }

    /// Returns the update channel from the manifest.
    pub fn get_manifest_channel(&self) -> String {
        self.manifest.channel.clone()
    }

    /// Returns whether this is a portable installation.
    pub fn get_is_portable(&self) -> bool {
        self.paths.IsPortable
    }

    /// Returns a stable identifier for this user, used for staged rollouts.
    ///
    /// Reads from `{PackagesDir}/.betaId` if it exists; otherwise generates a
    /// new UUID v4 (via WASI random), persists it, and returns it.
    pub fn get_staged_user_id(&self) -> String {
        let beta_id_path = PathBuf::from(&self.paths.PackagesDir).join(".betaId");
        if let Ok(id) = std::fs::read_to_string(&beta_id_path) {
            if !id.trim().is_empty() {
                return id.trim().to_string();
            }
        }
        let new_id = crate::misc::generate_uuid_v4();
        let _ = std::fs::create_dir_all(&self.paths.PackagesDir);
        let _ = std::fs::write(&beta_id_path, &new_id);
        new_id
    }
}

/// Find all `*-full.nupkg` files in a directory and return their paths
/// together with parsed manifests.
pub fn find_local_full_packages(packages_dir: &str) -> Vec<(String, Manifest)> {
    let mut results = Vec::new();
    let Ok(entries) = std::fs::read_dir(packages_dir) else {
        return results;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_default();
        if name.to_lowercase().ends_with("-full.nupkg") {
            let path_str = path.to_string_lossy().to_string();
            if let Ok(manifest) = bundle::load_manifest_from_file(&path_str) {
                results.push((path_str, manifest));
            }
        }
    }
    results
}

/// Return the path and manifest of the newest full package in `packages_dir`,
/// or `None` if no valid packages were found.
pub fn find_latest_full_package(packages_dir: &str) -> Option<(String, Manifest)> {
    find_local_full_packages(packages_dir)
        .into_iter()
        .max_by(|(_, a), (_, b)| a.version.cmp(&b.version))
}

fn read_current_manifest(nuspec_path: &str) -> Result<Manifest, Error> {
    let path = std::path::Path::new(nuspec_path);
    if path.exists() {
        if let Ok(nuspec) = crate::misc::retry_io(|| std::fs::read_to_string(path)) {
            return bundle::read_manifest_from_string(&nuspec);
        }
    }
    Err(Error::NotInstalled(format!(
        "Manifest file does not exist or is not readable: {}",
        nuspec_path
    )))
}
