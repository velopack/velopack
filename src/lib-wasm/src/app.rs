use semver::Version;
use std::env;

use crate::{
    constants::*,
    errors::Error,
    host_fs,
    locator::{self, VelopackLocator},
    manager, sources,
    types::VelopackLocatorConfig,
};

pub struct AppRunResult {
    pub hook: Option<(String, String)>,
    pub first_run: Option<String>,
    pub restarted: Option<String>,
}

pub fn app_run(args: &[String], locator_config: Option<VelopackLocatorConfig>, auto_apply: bool) -> Result<AppRunResult, Error> {
    let mut result = AppRunResult {
        hook: None,
        first_run: None,
        restarted: None,
    };

    if args.len() >= 2 {
        let hook_name = match args[0].to_ascii_lowercase().as_str() {
            x if x == HOOK_CLI_INSTALL => Some("after-install"),
            x if x == HOOK_CLI_UPDATED => Some("after-update"),
            x if x == HOOK_CLI_OBSOLETE => Some("before-update"),
            x if x == HOOK_CLI_UNINSTALL => Some("before-uninstall"),
            _ => None,
        };

        if let Some(name) = hook_name {
            if let Ok(version) = Version::parse(&args[1]) {
                result.hook = Some((name.to_string(), version.to_string()));

                let debug_mode = env::var(HOOK_ENV_DEBUG).is_ok();
                if !debug_mode {
                    return Ok(result);
                }
            }
        }
    }

    let locator_config = match locator_config {
        Some(c) => c,
        None => return Ok(result),
    };

    let locator = match VelopackLocator::new(&locator_config) {
        Ok(l) => l,
        Err(_) => return Ok(result),
    };

    let my_version = locator.get_manifest_version();
    let packages_dir = locator.get_packages_dir();

    let local_packages = locator::find_local_full_packages(&packages_dir);
    let latest_full = local_packages
        .iter()
        .filter(|(_, m)| m.version > my_version)
        .max_by(|(_, a), (_, b)| a.version.cmp(&b.version));

    let firstrun = env::var(HOOK_ENV_FIRSTRUN).is_ok();
    crate::velopack::core::host_process::remove_env(HOOK_ENV_FIRSTRUN);

    let restarted = env::var(HOOK_ENV_RESTART).is_ok();
    crate::velopack::core::host_process::remove_env(HOOK_ENV_RESTART);

    let pending_version = if let Some((path, manifest)) = latest_full {
        let pending_ver = manifest.version.clone();
        if auto_apply && !restarted {
            let asset = manager::local_path_to_asset(manifest, path);
            let mgr = manager::UpdateManager::new(sources::AutoSource::None(sources::NoneSource), None, Some(locator_config.clone()));
            if let Ok(mgr) = mgr {
                let app_args: Vec<String> = args.to_vec();
                let _ = mgr.wait_exit_then_apply_updates(&asset, false, true, app_args);
            }
        }
        Some(pending_ver)
    } else {
        None
    };

    cleanup_old_packages(local_packages, &my_version, pending_version.as_ref());

    if firstrun {
        result.first_run = Some(my_version.to_string());
    }

    if restarted {
        result.restarted = Some(my_version.to_string());
    }

    Ok(result)
}

fn cleanup_old_packages(packages: Vec<(String, crate::bundle::Manifest)>, current_version: &Version, pending_version: Option<&Version>) {
    for (path, manifest) in &packages {
        if manifest.version == *current_version {
            continue;
        }
        if let Some(pv) = pending_version {
            if &manifest.version == pv {
                continue;
            }
        }
        let _ = host_fs::delete_file(path);
    }
}
