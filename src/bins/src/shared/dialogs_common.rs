use super::dialogs_const::*;
use super::localization::t;
use anyhow::{bail, Result};
use fluent::FluentArgs;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use velopack::bundle::Manifest;
use velopack::locator::{auto_locate_app_manifest, LocationContext};
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

    let title = t("elevate-title", Some(&args));
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

    let title = t("restart-title", Some(&args));
    let header = t("restart-header", None);
    let body = t("restart-body", None);
    show_warn(&title, Some(&header), &body);
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

    let title = t("update-deps-title", Some(&args));
    let header = t("update-deps-header", Some(&args));
    let body = t("update-deps-body", Some(&args));
    let button = t("update-deps-button", None);
    show_ok_cancel(&title, Some(&header), &body, Some(&button))
}

pub fn show_setup_missing_dependencies_dialog(app_name: &str, app_version: &str, dependency_string: &str) -> bool {
    if get_silent() {
        return true;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_name.to_string());
    args.set("version", app_version.to_string());
    args.set("deps", dependency_string.to_string());

    let title = t("setup-deps-title", Some(&args));
    let header = t("setup-deps-header", Some(&args));
    let body = t("setup-deps-body", Some(&args));
    let button = t("setup-deps-button", None);
    show_ok_cancel(&title, Some(&header), &body, Some(&button))
}

pub fn show_uninstall_complete_with_errors_dialog(app_title: &str, log_path: Option<&PathBuf>) {
    if get_silent() {
        return;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());

    let title = t("uninstall-errors-title", Some(&args));
    let header = t("uninstall-errors-header", Some(&args));
    let body = t("uninstall-errors-body", None);

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
                main_instruction: header,
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
        show_warn(&title, Some(&header), &body);
    }
}

pub fn show_processes_locking_folder_dialog(app_title: &str, app_version: &str, process_names: &str) -> DialogResult {
    if get_silent() {
        return DialogResult::Cancel;
    }

    let mut args = FluentArgs::new();
    args.set("app", app_title.to_string());
    args.set("version", app_version.to_string());
    args.set("processes", process_names.to_string());

    let title = t("locking-title", Some(&args));
    let header = t("locking-header", Some(&args));
    let body = t("locking-body", Some(&args));
    let retry_label = t("locking-retry", None);
    let continue_label = t("locking-continue", None);
    let cancel_label = t("locking-cancel", None);

    let message = combine_header_body(Some(&header), &body);
    let result = xdialog::show_message(
        XDialogOptions {
            title,
            main_instruction: String::new(),
            message,
            icon: XDialogIcon::Information,
            buttons: vec![retry_label, continue_label, cancel_label],
        },
        None,
    );

    match result {
        Ok(XDialogResult::ButtonPressed(0)) => DialogResult::Retry,
        Ok(XDialogResult::ButtonPressed(1)) => DialogResult::Continue,
        _ => DialogResult::Cancel,
    }
}

pub fn show_overwrite_repair_dialog(app: &Manifest, root_path: &PathBuf, root_is_default: bool) -> bool {
    if get_silent() {
        return true;
    }

    let mut args = FluentArgs::new();
    args.set("app", app.title.clone());
    args.set("version", app.version.to_string());
    args.set("id", app.id.clone());
    args.set("path", root_path.display().to_string());

    let title = t("overwrite-title", Some(&args));

    let instruction;
    let body;
    let yes_label;

    // detect current app version to determine repair/update/downgrade
    let old_app = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(root_path.to_owned(), None));
    if let Ok(old) = old_app {
        let old_version = old.get_manifest_version();
        args.set("old", old_version.to_string());
        if old_version < app.version {
            instruction = t("overwrite-older-installed", Some(&args));
            body = t("overwrite-update-body", Some(&args));
            yes_label = t("overwrite-update-button", None);
        } else if old_version > app.version {
            instruction = t("overwrite-newer-installed", Some(&args));
            body = t("overwrite-downgrade-body", Some(&args));
            yes_label = t("overwrite-downgrade-button", None);
        } else {
            instruction = t("overwrite-already-installed", Some(&args));
            body = t("overwrite-repair-body", None);
            yes_label = t("overwrite-repair-button", None);
        }
    } else {
        instruction = t("overwrite-already-installed", Some(&args));
        body = t("overwrite-repair-body", None);
        yes_label = t("overwrite-repair-button", None);
    }

    let footer = if root_is_default {
        t("overwrite-footer-default", Some(&args))
    } else {
        t("overwrite-footer-custom", Some(&args))
    };

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
            show_overwrite_repair_dialog(app, root_path, root_is_default)
        }
        _ => false,
    }
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
