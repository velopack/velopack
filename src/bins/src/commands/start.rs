use crate::shared::{self, OperationWait};
use anyhow::Result;

#[allow(unused_variables, unused_imports)]
pub fn start(
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
    super::start_windows_impl::start_impl(exe_name, exe_args, legacy_args)?;

    #[cfg(not(target_os = "windows"))]
    {
        use velopack::locator::{auto_locate_app_manifest, LocationContext};
        let locator = auto_locate_app_manifest(LocationContext::IAmUpdateExe)?;
        shared::start_package(&locator, exe_args, None)?;
    }

    Ok(())
}
