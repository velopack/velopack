use crate::shared::{self, OperationWait};
use anyhow::{bail, Result};
use std::{ffi::OsString, path::PathBuf};
use velopack::{constants, locator, locator::VelopackLocator};

#[cfg(target_os = "linux")]
use super::apply_linux_impl::apply_package_impl;
#[cfg(target_os = "macos")]
use super::apply_osx_impl::apply_package_impl;
#[cfg(target_os = "windows")]
use super::apply_windows_impl::apply_package_impl;

#[derive(Clone, Copy, PartialEq, Eq)]
pub enum HookRunMode {
    /// Do not run any hooks.
    None,
    /// Run all hooks (pre and post).
    All,
    /// Only run post-apply hooks (e.g. --veloapp-updated). Used during legacy migration
    /// where the obsolete hook is irrelevant but the app needs to know it was updated.
    PostOnly,
}

pub fn apply(
    locator: &VelopackLocator,
    restart: bool,
    wait: OperationWait,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<OsString>>,
    hook_mode: HookRunMode,
) -> Result<VelopackLocator> {
    shared::operation_wait(wait);

    let packages_dir = locator.get_packages_dir();
    let package = package.cloned().or_else(|| locator::find_latest_full_package(&packages_dir).map(|x| x.0));

    match package {
        Some(package) => {
            info!(
                "Getting ready to apply package to {} ver {}: {:?}",
                locator.get_manifest_id(),
                locator.get_manifest_version_full_string(),
                package
            );
            match apply_package_impl(locator, &package, hook_mode) {
                Ok(applied_locator) => {
                    info!(
                        "Package version {} applied successfully.",
                        applied_locator.get_manifest_version_full_string()
                    );
                    // if successful, we want to restart the new version of the app, which could have different metadata
                    if restart {
                        shared::start_package(&applied_locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
                    }
                    Ok(applied_locator)
                }
                Err(e) => {
                    if restart {
                        shared::start_package(locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
                    }
                    if e.downcast_ref::<velopack::Error>()
                        .is_some_and(|ve| matches!(ve, velopack::Error::Cancelled))
                    {
                        info!("Update cancelled by user.");
                        return Ok(locator.clone());
                    }
                    bail!("Error applying package: {}", e);
                }
            }
        }
        None => {
            if restart {
                shared::start_package(locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
            }
            bail!("Failed to locate full package to apply. Please provide with the --package {{path}} argument");
        }
    }
}
