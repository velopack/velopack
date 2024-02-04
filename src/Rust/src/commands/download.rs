use crate::{bundle::Manifest, shared};
use anyhow::{bail, Result};
use std::{
    fs,
    path::{Path, PathBuf},
};

pub fn download<A>(root_path: &PathBuf, app: &Manifest, path: &str, clean: bool, name: &str, mut progress: A) -> Result<PathBuf>
where
    A: FnMut(i16),
{
    if !name.ends_with(".nupkg") {
        bail!("Asset name must end with .nupkg");
    }

    let packages_dir_str = app.get_packages_path(root_path);
    let packages_dir = Path::new(&packages_dir_str);
    let target_file = packages_dir.join(name);

    let mut to_delete = Vec::new();

    if clean {
        let g = format!("{}/*.nupkg", packages_dir_str);
        info!("Searching for packages to clean in: '{}'", g);
        match glob::glob(&g) {
            Ok(paths) => {
                for path in paths {
                    if let Ok(path) = path {
                        to_delete.push(path);
                    }
                }
            }
            Err(e) => {
                error!("Error while searching for packages to clean: {}", e);
            }
        }
    }

    if shared::is_http_url(path) {
        info!("About to download from URL '{}' to file '{}'", path, target_file.to_string_lossy());
        shared::download::download_url_to_file(path, &target_file.to_string_lossy(), &mut progress)?;
    } else {
        let source_path = Path::new(path);
        let source_file = source_path.join(name);
        info!("About to copy local file from '{}' to '{}'", source_file.to_string_lossy(), target_file.to_string_lossy());

        if !source_file.exists() {
            bail!("Local file does not exist: {}", source_file.to_string_lossy());
        }

        fs::copy(&source_file, &target_file)?;
    }

    info!("Successfully placed file: '{}'", target_file.to_string_lossy());

    if clean {
        for path in to_delete {
            info!("Cleaning up old package: '{}'", path.to_string_lossy());
            fs::remove_file(&path)?;
        }
    }

    Ok(target_file)
}
