use crate::locale_constants::*;
use crate::localization::format_message;
use fluent::FluentArgs;

// --- No-arg functions ---

pub fn btn_cancel() -> String {
    format_message(BTN_CANCEL, None)
}

pub fn btn_install_update() -> String {
    format_message(BTN_INSTALL_UPDATE, None)
}

pub fn btn_install() -> String {
    format_message(BTN_INSTALL, None)
}

pub fn btn_update() -> String {
    format_message(BTN_UPDATE, None)
}

pub fn btn_downgrade() -> String {
    format_message(BTN_DOWNGRADE, None)
}

pub fn btn_repair() -> String {
    format_message(BTN_REPAIR, None)
}

pub fn btn_open_log() -> String {
    format_message(BTN_OPEN_LOG, None)
}

pub fn btn_open_install_dir() -> String {
    format_message(BTN_OPEN_INSTALL_DIR, None)
}

pub fn btn_ok() -> String {
    format_message(BTN_OK, None)
}

pub fn btn_hide() -> String {
    format_message(BTN_HIDE, None)
}

pub fn elevate_header() -> String {
    format_message(ELEVATE_HEADER, None)
}

pub fn restart_header() -> String {
    format_message(RESTART_HEADER, None)
}

pub fn missing_deps_header() -> String {
    format_message(MISSING_DEPS_HEADER, None)
}

pub fn uninstall_errors_header() -> String {
    format_message(UNINSTALL_ERRORS_HEADER, None)
}

pub fn error_header() -> String {
    format_message(ERROR_HEADER, None)
}

pub fn setup_error_header() -> String {
    format_message(SETUP_ERROR_HEADER, None)
}

pub fn install_hook_header() -> String {
    format_message(INSTALL_HOOK_HEADER, None)
}

pub fn uninstall_header() -> String {
    format_message(UNINSTALL_HEADER, None)
}

pub fn start_corrupt_header() -> String {
    format_message(START_CORRUPT_HEADER, None)
}

pub fn apply_header() -> String {
    format_message(APPLY_HEADER, None)
}

pub fn restart_body() -> String {
    format_message(RESTART_BODY, None)
}

pub fn overwrite_repair_body() -> String {
    format_message(OVERWRITE_REPAIR_BODY, None)
}

pub fn uninstall_body() -> String {
    format_message(UNINSTALL_BODY, None)
}

pub fn install_hook_body() -> String {
    format_message(INSTALL_HOOK_BODY, None)
}

pub fn start_corrupt_body() -> String {
    format_message(START_CORRUPT_BODY, None)
}

// --- With-arg functions ---

pub fn title_update(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(TITLE_UPDATE, Some(&args))
}

pub fn title_setup(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(TITLE_SETUP, Some(&args))
}

pub fn title_uninstall(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(TITLE_UNINSTALL, Some(&args))
}

pub fn error_title(program_name: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("program_name", program_name.to_string());
    format_message(ERROR_TITLE, Some(&args))
}

pub fn elevate_body(app: &str, version: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    args.set("app_version", version.to_string());
    format_message(ELEVATE_BODY, Some(&args))
}

pub fn missing_deps_body(app: &str, deps: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    args.set("deps", deps.to_string());
    format_message(MISSING_DEPS_BODY, Some(&args))
}

pub fn uninstall_errors_body(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(UNINSTALL_ERRORS_BODY, Some(&args))
}

pub fn uninstall_errors_log(path: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("path", path.to_string());
    format_message(UNINSTALL_ERRORS_LOG, Some(&args))
}

pub fn overwrite_already_installed(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(OVERWRITE_ALREADY_INSTALLED, Some(&args))
}

pub fn overwrite_older_installed(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(OVERWRITE_OLDER_INSTALLED, Some(&args))
}

pub fn overwrite_update_body(old: &str, version: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("old_version", old.to_string());
    args.set("app_version", version.to_string());
    format_message(OVERWRITE_UPDATE_BODY, Some(&args))
}

pub fn overwrite_newer_installed(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(OVERWRITE_NEWER_INSTALLED, Some(&args))
}

pub fn overwrite_downgrade_body(old: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("old_version", old.to_string());
    format_message(OVERWRITE_DOWNGRADE_BODY, Some(&args))
}

pub fn overwrite_footer(path: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("path", path.to_string());
    format_message(OVERWRITE_FOOTER, Some(&args))
}

pub fn splash_header(app: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    format_message(SPLASH_HEADER, Some(&args))
}

pub fn splash_body(app: &str, version: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_title", app.to_string());
    args.set("app_version", version.to_string());
    format_message(SPLASH_BODY, Some(&args))
}

pub fn deps_download_header() -> String {
    format_message(DEPS_DOWNLOAD_HEADER, None)
}

pub fn deps_download_body(dep_name: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("dep_name", dep_name.to_string());
    format_message(DEPS_DOWNLOAD_BODY, Some(&args))
}

pub fn apply_body(version: &str) -> String {
    let mut args = FluentArgs::new();
    args.set("app_version", version.to_string());
    format_message(APPLY_BODY, Some(&args))
}

pub fn progress_cancelling() -> String {
    format_message(PROGRESS_CANCELLING, None)
}
