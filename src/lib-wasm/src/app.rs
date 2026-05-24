use semver::Version;
use std::env;

use crate::{
    constants::*,
    errors::Error,
    locator::{self, VelopackLocator},
    manager,
    sources,
    types::VelopackLocatorConfig,
};

/// Result of running `app_run` -- indicates what hook was triggered, if any,
/// and whether first-run or restarted conditions were detected.
pub struct AppRunResult {
    /// If a fast hook was triggered, this contains `(hook_name, version)`.
    ///
    /// Hook names:
    /// - `"after-install"` -- the app was just installed
    /// - `"after-update"` -- the app was just updated
    /// - `"before-update"` -- the previous version is about to be replaced
    /// - `"before-uninstall"` -- the app is about to be uninstalled
    pub hook: Option<(String, String)>,
    /// Set to the current version string if this is the very first run after
    /// installation.
    pub first_run: Option<String>,
    /// Set to the current version string if the app was restarted by
    /// Velopack after applying an update.
    pub restarted: Option<String>,
}

/// Runs the Velopack startup logic. This should be called as early as
/// possible in the application's entry point.
///
/// In the WASM environment this function never calls `process::exit()`.
/// Instead it returns an `AppRunResult` so the host can decide how to
/// handle lifecycle events (fast hooks, auto-apply, first-run, restart).
///
/// # Arguments
///
/// * `args` -- The application's command-line arguments (excluding the
///   program name / arg-0). In native Rust this would be
///   `env::args().skip(1)`.
/// * `locator_config` -- The locator configuration. Required to read the
///   app manifest and find local packages.
/// * `auto_apply` -- If `true` and there is a downloaded update with a
///   version greater than the current one, the update binary will be
///   launched to apply it and the host should exit afterwards.
pub fn app_run(
    args: &[String],
    locator_config: Option<VelopackLocatorConfig>,
    auto_apply: bool,
) -> Result<AppRunResult, Error> {
    let mut result = AppRunResult {
        hook: None,
        first_run: None,
        restarted: None,
    };

    // -----------------------------------------------------------------------
    // Fast hooks -- these are CLI arguments injected by the update binary
    // during install / update / uninstall.
    // -----------------------------------------------------------------------
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

                // In debug mode we do NOT return early so the caller can
                // continue running for testing purposes.
                let debug_mode = env::var(HOOK_ENV_DEBUG).is_ok();
                if !debug_mode {
                    return Ok(result);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Try to create a locator to read app metadata.
    // -----------------------------------------------------------------------
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

    // Load all local packages once -- used for both auto-apply and cleanup.
    let local_packages = locator::find_local_full_packages(&packages_dir);
    let latest_full = local_packages
        .iter()
        .filter(|(_, m)| m.version > my_version)
        .max_by(|(_, a), (_, b)| a.version.cmp(&b.version));

    let firstrun = env::var(HOOK_ENV_FIRSTRUN).is_ok();
    env::remove_var(HOOK_ENV_FIRSTRUN);

    let restarted = env::var(HOOK_ENV_RESTART).is_ok();
    env::remove_var(HOOK_ENV_RESTART);

    // -----------------------------------------------------------------------
    // Auto-apply pending updates
    // -----------------------------------------------------------------------
    let pending_version = if let Some((path, manifest)) = latest_full {
        let pending_ver = manifest.version.clone();
        if auto_apply && !restarted {
            let asset = manager::local_path_to_asset(manifest, path);
            let mgr = manager::UpdateManager::new(
                sources::AutoSource::None(sources::NoneSource),
                None,
                Some(locator_config.clone()),
            );
            if let Ok(mgr) = mgr {
                let app_args: Vec<String> = args.to_vec();
                // In native code this would call exit(0) after launching
                // the updater. In WASM we just launch it and return -- the
                // host is responsible for exiting.
                let _ = mgr.wait_exit_then_apply_updates(
                    &asset, false, true, app_args,
                );
            }
        }
        Some(pending_ver)
    } else {
        None
    };

    // -----------------------------------------------------------------------
    // Clean up old packages
    // -----------------------------------------------------------------------
    cleanup_old_packages(local_packages, &my_version, pending_version.as_ref());

    // -----------------------------------------------------------------------
    // First-run / restarted callbacks
    // -----------------------------------------------------------------------
    if firstrun {
        result.first_run = Some(my_version.to_string());
    }

    if restarted {
        result.restarted = Some(my_version.to_string());
    }

    Ok(result)
}

/// Removes local packages that are neither the current version nor a
/// pending update.
fn cleanup_old_packages(
    packages: Vec<(String, crate::bundle::Manifest)>,
    current_version: &Version,
    pending_version: Option<&Version>,
) {
    for (path, manifest) in &packages {
        if manifest.version == *current_version {
            continue;
        }
        if let Some(pv) = pending_version {
            if &manifest.version == pv {
                continue;
            }
        }
        let _ = std::fs::remove_file(path);
    }
}
