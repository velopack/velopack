#![allow(missing_docs)]

pub const HOOK_ENV_FIRSTRUN: &str = "VELOPACK_FIRSTRUN";
pub const HOOK_ENV_DEBUG: &str = "VELOPACK_DEBUG";
pub const HOOK_ENV_RESTART: &str = "VELOPACK_RESTART";
pub const HOOK_CLI_INSTALL: &str = "--veloapp-install";
pub const HOOK_CLI_UPDATED: &str = "--veloapp-updated";
pub const HOOK_CLI_OBSOLETE: &str = "--veloapp-obsolete";
pub const HOOK_CLI_UNINSTALL: &str = "--veloapp-uninstall";

#[cfg(target_os = "windows")]
pub const DEFAULT_CHANNEL_NAME: &str = "win";

#[cfg(target_os = "linux")]
pub const DEFAULT_CHANNEL_NAME: &str = "linux";

#[cfg(target_os = "macos")]
pub const DEFAULT_CHANNEL_NAME: &str = "osx";