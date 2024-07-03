use std::path::PathBuf;

use anyhow::{bail, Result};

use crate::bundle::Manifest;
use crate::shared::{self, OperationWait};

pub fn start(
    root_dir: &PathBuf,
    app: &Manifest,
    wait: OperationWait,
    exe_name: Option<&String>,
    exe_args: Option<Vec<&str>>,
    legacy_args: Option<&String>,
) -> Result<()> {
    #[cfg(target_os = "windows")]
    if legacy_args.is_some() && exe_args.is_some() {
        bail!("Cannot use both legacy args and new args format.");
    }

    shared::operation_wait(wait);

    #[cfg(target_os = "windows")]
    super::start_windows_impl::start_impl(&root_dir, &app, exe_name, exe_args, legacy_args)?;

    #[cfg(not(target_os = "windows"))]
    shared::start_package(&app, &root_dir, exe_args, None)?;

    Ok(())
}
