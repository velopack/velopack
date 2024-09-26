use crate::shared::{self, OperationWait};
use anyhow::Result;
use velopack::locator::VelopackLocator;

#[allow(unused_variables, unused_imports)]
pub fn start(
    locator: &VelopackLocator,
    wait: OperationWait,
    exe_name: Option<&String>,
    exe_args: Option<Vec<&str>>,
    legacy_args: Option<&String>,
) -> Result<()> {
    use anyhow::bail;

    #[cfg(target_os = "windows")]
    if legacy_args.is_some() && exe_args.is_some() {
        bail!("Cannot use both legacy args and new args format.");
    }

    shared::operation_wait(wait);

    #[cfg(target_os = "windows")]
    super::start_windows_impl::start_impl(&locator, exe_name, exe_args, legacy_args)?;

    #[cfg(not(target_os = "windows"))]
    shared::start_package(&app, &root_dir, exe_args, None)?;

    Ok(())
}
