use crate::shared::{self, bundle::Manifest};
use anyhow::Result;
use std::path::PathBuf;

#[cfg(target_os = "linux")]
use super::apply_linux_impl::apply_package_impl;
#[cfg(target_os = "macos")]
use super::apply_osx_impl::apply_package_impl;
#[cfg(target_os = "windows")]
use super::apply_windows_impl::apply_package_impl;

pub fn apply<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    restart: bool,
    wait_for_parent: bool,
    package: &PathBuf,
    exe_args: Option<Vec<&str>>,
    runhooks: bool,
) -> Result<()> {
    if wait_for_parent {
        if let Err(e) = shared::wait_for_parent_to_exit(60_000) {
            warn!("Failed to wait for parent process to exit ({}).", e);
        }
    }

    if let Err(e) = apply_package_impl(&root_path, &app, package, runhooks) {
        error!("Error applying package: {}", e);
        if restart {
            shared::start_package(&app, &root_path, exe_args, Some("VELOPACK_RESTART"))?;
        }
        return Err(e);
    }

    // TODO: if the package fails to start, or fails hooks, we could roll back the install
    if restart {
        shared::start_package(&app, &root_path, exe_args, Some("VELOPACK_RESTART"))?;
    }

    Ok(())
}
