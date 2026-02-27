use crate::localization::t;
use anyhow::{bail, Result};
use fluent::FluentArgs;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use xdialog::{XDialogIcon, XDialogOptions, XDialogResult};

static SILENT: AtomicBool = AtomicBool::new(false);

pub fn set_silent(silent: bool) {
    SILENT.store(silent, Ordering::Relaxed);
    xdialog::set_silent_mode(silent);
}

pub fn get_silent() -> bool {
    SILENT.load(Ordering::Relaxed)
}

pub fn show_error(title: &str, header: Option<&str>, body: &str) {
    if get_silent() {
        return;
    }
    let message = combine_header_body(header, body);
    let _ = xdialog::show_message_error_ok(title, "", &message);
}

pub fn show_warn(title: &str, header: Option<&str>, body: &str) {
    if get_silent() {
        return;
    }
    let message = combine_header_body(header, body);
    let _ = xdialog::show_message_warn_ok(title, "", &message);
}

pub fn show_info(title: &str, header: Option<&str>, body: &str) {
    if get_silent() {
        return;
    }
    let message = combine_header_body(header, body);
    let _ = xdialog::show_message_info_ok(title, "", &message);
}

pub fn show_ok_cancel(title: &str, header: Option<&str>, body: &str, ok_text: Option<&str>) -> bool {
    if get_silent() {
        return false;
    }
    let message = combine_header_body(header, body);
    if let Some(ok_label) = ok_text {
        let cancel_label = t("btn-cancel", None);
        let result = xdialog::show_message(
            XDialogOptions {
                title: title.to_string(),
                main_instruction: String::new(),
                message,
                icon: XDialogIcon::Warning,
                buttons: vec![ok_label.to_string(), cancel_label],
            },
            None,
        );
        matches!(result, Ok(XDialogResult::ButtonPressed(0)))
    } else {
        xdialog::show_message_ok_cancel(title, "", &message, XDialogIcon::Warning).unwrap_or(false)
    }
}

pub fn ask_user_to_elevate(app_title: &str, new_version: &str) -> Result<()> {
    if get_silent() {
        bail!("Not allowed to ask for elevated permissions because --silent flag is set.");
    }

    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());
    args.set("version", new_version.to_string());

    let title = t("title-update", Some(&args));
    let body = t("elevate-body", Some(&args));
    let ok_text = t("btn-install-update", None);

    info!("Showing user elevation prompt?");
    if show_ok_cancel(&title, None, &body, Some(&ok_text)) {
        info!("User answered yes to elevation...");
        Ok(())
    } else {
        bail!("User cancelled elevation prompt.");
    }
}

pub fn show_restart_required(app_name: &str, app_version: &str) {
    let mut args = FluentArgs::new();
    args.set("app", app_name.to_string());
    args.set("version", app_version.to_string());

    let title = t("title-setup", Some(&args));
    let body = t("restart-body", None);
    show_warn(&title, None, &body);
}

pub fn show_update_missing_dependencies_dialog(app_name: &str, dependency_string: &str, from_ver: &str, to_ver: &str) -> bool {
    if get_silent() {
        warn!("Cancelling pre-requisite installation because silent flag is true.");
        return false;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_name.to_string());
    args.set("from", from_ver.to_string());
    args.set("to", to_ver.to_string());
    args.set("deps", dependency_string.to_string());

    let title = t("title-update", Some(&args));
    let body = t("missing-deps-body", Some(&args));
    let button = t("btn-install", None);
    show_ok_cancel(&title, None, &body, Some(&button))
}

pub fn show_setup_missing_dependencies_dialog(app_name: &str, app_version: &str, dependency_string: &str) -> bool {
    if get_silent() {
        return true;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_name.to_string());
    args.set("version", app_version.to_string());
    args.set("deps", dependency_string.to_string());

    let title = t("title-setup", Some(&args));
    let body = t("missing-deps-body", Some(&args));
    let button = t("btn-install", None);
    show_ok_cancel(&title, None, &body, Some(&button))
}

pub fn show_uninstall_complete_with_errors_dialog(app_title: &str, log_path: Option<&PathBuf>) {
    if get_silent() {
        return;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());

    let title = t("title-uninstall", Some(&args));
    let body = t("uninstall-errors-body", Some(&args));

    let has_log = log_path.map(|p| p.exists()).unwrap_or(false);
    if has_log {
        let log_str = log_path.unwrap().to_string_lossy().to_string();
        let mut log_args = FluentArgs::new();
        log_args.set("path", log_str.clone());
        let footer = t("uninstall-errors-log", Some(&log_args));
        let open_log_label = t("btn-open-log", None);
        let ok_label = "OK".to_string();
        let full_body = format!("{}\n\n{}", body, footer);
        let result = xdialog::show_message(
            XDialogOptions {
                title,
                main_instruction: String::new(),
                message: full_body,
                icon: XDialogIcon::Warning,
                buttons: vec![ok_label, open_log_label],
            },
            None,
        );
        if matches!(result, Ok(XDialogResult::ButtonPressed(1))) {
            open_path(&PathBuf::from(log_str));
        }
    } else {
        show_warn(&title, None, &body);
    }
}

pub fn show_overwrite_repair_dialog(
    app_title: &str,
    app_version: &semver::Version,
    app_id: &str,
    root_path: &PathBuf,
    _root_is_default: bool,
    installed_version: Option<&semver::Version>,
) -> bool {
    if get_silent() {
        return true;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());
    args.set("version", app_version.to_string());
    args.set("id", app_id.to_string());
    args.set("path", root_path.display().to_string());

    let title = t("title-setup", Some(&args));

    let instruction;
    let body;
    let yes_label;

    if let Some(old_version) = installed_version {
        args.set("old", old_version.to_string());
        if old_version < app_version {
            instruction = t("overwrite-older-installed", Some(&args));
            body = t("overwrite-update-body", Some(&args));
            yes_label = t("btn-update", None);
        } else if old_version > app_version {
            instruction = t("overwrite-newer-installed", Some(&args));
            body = t("overwrite-downgrade-body", Some(&args));
            yes_label = t("btn-downgrade", None);
        } else {
            instruction = t("overwrite-already-installed", Some(&args));
            body = t("overwrite-repair-body", None);
            yes_label = t("btn-repair", None);
        }
    } else {
        instruction = t("overwrite-already-installed", Some(&args));
        body = t("overwrite-repair-body", None);
        yes_label = t("btn-repair", None);
    }

    let footer = t("overwrite-footer", Some(&args));

    let cancel_label = t("btn-cancel", None);
    let open_dir_label = t("btn-open-install-dir", None);
    let full_body = format!("{}\n\n{}", body, footer);
    let message = combine_header_body(Some(&instruction), &full_body);

    let result = xdialog::show_message(
        XDialogOptions {
            title,
            main_instruction: String::new(),
            message,
            icon: XDialogIcon::Warning,
            buttons: vec![yes_label, open_dir_label, cancel_label],
        },
        None,
    );

    match result {
        Ok(XDialogResult::ButtonPressed(0)) => true,
        Ok(XDialogResult::ButtonPressed(1)) => {
            open_path(root_path);
            // after opening the directory, re-show the dialog
            show_overwrite_repair_dialog(app_title, app_version, app_id, root_path, _root_is_default, installed_version)
        }
        _ => false,
    }
}

// --- Helper functions that encapsulate t() calls ---

pub fn show_generic_error(program_name: &str, error_string: &str) {
    let mut args = FluentArgs::new();
    args.set("program_name", program_name.to_string());
    let title = t("error-title", Some(&args));
    show_error(&title, None, error_string);
}

pub fn show_install_hook_warning(app_title: &str, app_id: &str) {
    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());
    args.set("id", app_id.to_string());
    let title = t("title-setup", Some(&args));
    let body = t("install-hook-body", None);
    show_warn(&title, None, &body);
}

pub fn show_uninstall_complete(app_title: &str) {
    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());
    let title = t("title-uninstall", Some(&args));
    let body = t("uninstall-body", None);
    show_info(&title, None, &body);
}

pub fn show_start_corrupt_error(app_title: &str) {
    let body = t("start-corrupt-body", None);
    show_error(app_title, None, &body);
}

fn combine_header_body(header: Option<&str>, body: &str) -> String {
    match header {
        Some(h) if !h.is_empty() => format!("{}\n\n{}", h, body),
        _ => body.to_string(),
    }
}

fn open_path(path: &PathBuf) {
    let path_str = path.to_string_lossy().to_string();
    #[cfg(target_os = "windows")]
    {
        let _ = std::process::Command::new("explorer").arg(&path_str).spawn();
    }
    #[cfg(target_os = "macos")]
    {
        let _ = std::process::Command::new("open").arg(&path_str).spawn();
    }
    #[cfg(target_os = "linux")]
    {
        let _ = std::process::Command::new("xdg-open").arg(&path_str).spawn();
    }
}

#[test]
#[ntest::timeout(2000)]
fn test_no_dialogs_show_if_silent() {
    set_silent(true);
    show_error("Error", None, "This is an error.");
    show_warn("Warning", None, "This is a warning.");
    show_info("Information", None, "This is information.");
    assert!(!show_ok_cancel("Ok/Cancel", None, "This is a question.", None));
}

#[test]
#[ignore]
fn test_show_all_dialogs() {
    set_silent(false);
    show_error("Error", None, "This is an error.");
    show_warn("Warning", None, "This is a warning.");
    show_info("Information", None, "This is information.");
    assert!(show_ok_cancel("Ok/Cancel", None, "This is a question.", None));
    assert!(!show_ok_cancel("Ok/Cancel", None, "This is a question.", Some("Dont click!")));
}
