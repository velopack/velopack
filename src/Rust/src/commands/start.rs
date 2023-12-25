use crate::shared;
use anyhow::{bail, Result};
use std::path::Path;

pub fn start(wait_for_parent: bool, exe_name: Option<&String>, exe_args: Option<Vec<&str>>, legacy_args: Option<&String>) -> Result<()> {
    if legacy_args.is_some() && exe_args.is_some() {
        bail!("Cannot use both legacy args and new args format.");
    }

    if wait_for_parent {
        shared::wait_for_parent_to_exit(60_000)?; // 1 minute
    }

    let (root_path, app) = shared::detect_current_manifest()?;

    let current = app.get_current_path(&root_path);
    let exe_to_execute = if let Some(exe) = exe_name {
        Path::new(&current).join(exe)
    } else {
        let exe = app.get_main_exe_path(&root_path);
        Path::new(&exe).to_path_buf()
    };

    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{}'", exe_to_execute.to_string_lossy());
    }

    crate::windows::assert_can_run_binary_authenticode(&exe_to_execute)?;

    info!("About to launch: '{}' in dir '{}'", exe_to_execute.to_string_lossy(), current);

    if let Some(args) = exe_args {
        crate::shared::run_process(exe_to_execute, args, current)?;
    } else if let Some(args) = legacy_args {
        crate::windows::run_process_raw_args(exe_to_execute, args, current)?;
    } else {
        crate::shared::run_process(exe_to_execute, vec![], current)?;
    };

    Ok(())
}
