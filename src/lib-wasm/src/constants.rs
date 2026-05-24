#![allow(missing_docs)]

pub const HOOK_ENV_FIRSTRUN: &str = "VELOPACK_FIRSTRUN";
pub const HOOK_ENV_DEBUG: &str = "VELOPACK_DEBUG";
pub const HOOK_ENV_RESTART: &str = "VELOPACK_RESTART";
pub const HOOK_CLI_INSTALL: &str = "--veloapp-install";
pub const HOOK_CLI_UPDATED: &str = "--veloapp-updated";
pub const HOOK_CLI_OBSOLETE: &str = "--veloapp-obsolete";
pub const HOOK_CLI_UNINSTALL: &str = "--veloapp-uninstall";

/// Returns the default channel name for the current platform.
/// In WASM, `cfg(target_os)` is always `wasm32`, so we read the
/// `VELOPACK_CHANNEL_DEFAULT` environment variable instead.
/// Falls back to an empty string if the variable is not set.
pub fn default_channel_name() -> String {
    std::env::var("VELOPACK_CHANNEL_DEFAULT").unwrap_or_default()
}
