use crate::shared::{self, OperationWait};
use velopack::{locator, locator::VelopackLocator, constants};
use anyhow::{bail, Result};
use std::path::PathBuf;

#[cfg(target_os = "linux")]
use super::apply_linux_impl::apply_package_impl;
#[cfg(target_os = "macos")]
use super::apply_osx_impl::apply_package_impl;
#[cfg(target_os = "windows")]
use super::apply_windows_impl::apply_package_impl;

pub fn apply<'a>(
    locator: &VelopackLocator,
    restart: bool,
    wait: OperationWait,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    run_hooks: bool,
) -> Result<VelopackLocator> {
    shared::operation_wait(wait);

    let packages_dir = locator.get_packages_dir();
    let package = package.cloned().or_else(|| locator::find_latest_full_package(&packages_dir).map(|x| x.0));

    match package {
        Some(package) => {
            info!("Getting ready to apply package to {} ver {}: {}", 
                locator.get_manifest_id(), 
                locator.get_manifest_version_full_string(), 
                package.to_string_lossy());
            match apply_package_impl(&locator, &package, run_hooks) {
                Ok(applied_locator) => {
                    info!("Package version {} applied successfully.", applied_locator.get_manifest_version_full_string());
                    // if successful, we want to restart the new version of the app, which could have different metadata
                    if restart {
                        shared::start_package(&applied_locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
                    }
                    return Ok(applied_locator);
                }
                Err(e) => {
                    error!("Error applying package: {}", e);
                }
            }
        }
        None => {
            error!("Failed to locate full package to apply. Please provide with the --package {{path}} argument");
        }
    }

    // an error occurred if we're here, but we still want to restart the old version of the app if it was requested
    if restart {
        shared::start_package(&locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
    }

    bail!("Apply failed, see logs for details.");
}
