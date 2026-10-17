use crate::shared::{self, OperationWait};
use anyhow::{bail, Result};
use std::{
    ffi::OsString,
    path::{Path, PathBuf},
};
use velopack::{constants, locator::VelopackLocator};

#[cfg(target_os = "windows")]
use super::apply_staged_windows_impl::apply_staged_impl;

/// Global flags forwarded to an elevated `apply-staged` child.
#[derive(Debug, Clone, Default)]
pub struct ElevationFlags {
    pub silent: bool,
    pub verbose: bool,
    pub log: Option<PathBuf>,
}

impl ElevationFlags {
    /// The command line fragment to forward.
    pub fn to_args(&self) -> Vec<OsString> {
        let mut args: Vec<OsString> = Vec::new();
        if self.silent {
            args.push("--silent".into());
        }
        if self.verbose {
            args.push("--verbose".into());
        }
        if let Some(log) = &self.log {
            args.push("--log".into());
            args.push(log.into());
        }
        args
    }
}

/// The `apply-staged` command (§7.2): swaps a pre-assembled staged tree into place of `current/`.
pub fn apply_staged(
    locator: &VelopackLocator,
    staged_dir: &Path,
    restart: bool,
    wait: OperationWait,
    exe_args: Option<Vec<OsString>>,
    hook_mode: super::HookRunMode,
    flags: &ElevationFlags,
) -> Result<VelopackLocator> {
    shared::operation_wait(wait);

    info!(
        "Getting ready to apply staged update to {} ver {}: {:?}",
        locator.get_manifest_id(),
        locator.get_manifest_version_full_string(),
        staged_dir
    );

    match apply_staged_impl(locator, staged_dir, hook_mode, flags) {
        Ok(applied_locator) => {
            info!(
                "Staged version {} applied successfully.",
                applied_locator.get_manifest_version_full_string()
            );
            if restart {
                shared::start_package(&applied_locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
            }
            Ok(applied_locator)
        }
        Err(e) => {
            if restart {
                shared::start_package(locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
            }
            bail!("Error applying staged update: {}", e);
        }
    }
}

#[cfg(not(target_os = "windows"))]
fn apply_staged_impl(
    _locator: &VelopackLocator,
    _staged_dir: &Path,
    _hook_mode: super::HookRunMode,
    _flags: &ElevationFlags,
) -> Result<VelopackLocator> {
    bail!("apply-staged is not supported on this platform");
}
