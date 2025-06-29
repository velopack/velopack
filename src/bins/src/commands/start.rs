use crate::shared::{self, OperationWait};
use anyhow::Result;
use std::ffi::OsString;
use velopack::locator::LocationContext;

#[allow(unused_variables)]
pub fn start(
    wait: OperationWait,
    context: LocationContext,
    exe_name: Option<&OsString>,
    exe_args: Option<Vec<OsString>>,
    legacy_args: Option<&OsString>,
) -> Result<()> {
    shared::operation_wait(wait);

    #[cfg(target_os = "windows")]
    super::start_windows_impl::start_impl(context, exe_name, exe_args, legacy_args)?;

    #[cfg(not(target_os = "windows"))]
    {
        let locator = velopack::locator::auto_locate_app_manifest(context)?;
        shared::start_package(&locator, exe_args, None)?;
    }

    Ok(())
}
