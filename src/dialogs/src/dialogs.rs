use crate::backends::{XDialogIcon, XDialogOptions, XDialogResult};
use crate::locale::strings as locale_strings;
use anyhow::{bail, Result};
use std::path::PathBuf;

fn show_dialog(title: &str, header: &str, body: &str, icon: XDialogIcon, buttons: Vec<String>) -> Option<XDialogResult> {
    if crate::get_silent() {
        return Some(XDialogResult::SilentMode);
    }
    let result = crate::with_manager(|mgr| {
        let proxy = mgr.show(XDialogOptions {
            title: title.to_string(),
            main_instruction: header.to_string(),
            message: body.to_string(),
            icon,
            buttons,
            has_progress: false,
        })?;
        proxy.get_result(None)
    });
    match result {
        Ok(r) => Some(r),
        Err(e) => {
            warn!("Failed to show dialog: {:?}", e);
            None
        }
    }
}

fn show_error(title: &str, header: &str, body: &str) {
    show_dialog(title, header, body, XDialogIcon::Error, vec!["OK".to_string()]);
}

fn show_warn(title: &str, header: &str, body: &str) {
    show_dialog(title, header, body, XDialogIcon::Warning, vec!["OK".to_string()]);
}

fn show_info(title: &str, header: &str, body: &str) {
    show_dialog(title, header, body, XDialogIcon::Information, vec!["OK".to_string()]);
}

fn show_ok_cancel(title: &str, header: &str, body: &str, ok_text: Option<&str>) -> bool {
    let ok_label = ok_text.unwrap_or("OK").to_string();
    let cancel_label = locale_strings::btn_cancel();
    let result = show_dialog(title, header, body, XDialogIcon::Warning, vec![ok_label, cancel_label]);
    matches!(result, Some(XDialogResult::ButtonPressed(0)))
}

pub fn ask_user_to_elevate(app_title: &str, new_version: &str) -> Result<()> {
    if crate::get_silent() {
        bail!("Not allowed to ask for elevated permissions because --silent flag is set.");
    }

    let title = locale_strings::title_update(app_title);
    let header = locale_strings::elevate_header();
    let body = locale_strings::elevate_body(app_title, new_version);
    let ok_text = locale_strings::btn_install_update();

    info!("Showing user elevation prompt?");
    if show_ok_cancel(&title, &header, &body, Some(&ok_text)) {
        info!("User answered yes to elevation...");
        Ok(())
    } else {
        bail!("User cancelled elevation prompt.");
    }
}

pub fn show_restart_required(app_name: &str, _app_version: &str) {
    let title = locale_strings::title_setup(app_name);
    let header = locale_strings::restart_header();
    let body = locale_strings::restart_body();
    show_warn(&title, &header, &body);
}

pub fn show_update_missing_dependencies_dialog(app_name: &str, dependency_string: &str, _from_ver: &str, _to_ver: &str) -> bool {
    if crate::get_silent() {
        warn!("Cancelling pre-requisite installation because silent flag is true.");
        return false;
    }

    let title = locale_strings::title_update(app_name);
    let header = locale_strings::missing_deps_header();
    let body = locale_strings::missing_deps_body(app_name, dependency_string);
    let button = locale_strings::btn_install();
    show_ok_cancel(&title, &header, &body, Some(&button))
}

pub fn show_setup_missing_dependencies_dialog(app_name: &str, _app_version: &str, dependency_string: &str) -> bool {
    if crate::get_silent() {
        return true;
    }

    let title = locale_strings::title_setup(app_name);
    let header = locale_strings::missing_deps_header();
    let body = locale_strings::missing_deps_body(app_name, dependency_string);
    let button = locale_strings::btn_install();
    show_ok_cancel(&title, &header, &body, Some(&button))
}

pub fn show_uninstall_complete_with_errors_dialog(app_title: &str, log_path: Option<&PathBuf>) {
    let title = locale_strings::title_uninstall(app_title);
    let header = locale_strings::uninstall_errors_header();
    let body = locale_strings::uninstall_errors_body(app_title);

    let has_log = log_path.map(|p| p.exists()).unwrap_or(false);
    if has_log {
        let log_str = log_path.unwrap().to_string_lossy().to_string();
        let footer = locale_strings::uninstall_errors_log(&log_str);
        let open_log_label = locale_strings::btn_open_log();
        let ok_label = "OK".to_string();
        let full_body = format!("{}\n\n{}", body, footer);
        let result = show_dialog(&title, &header, &full_body, XDialogIcon::Warning, vec![ok_label, open_log_label]);
        if matches!(result, Some(XDialogResult::ButtonPressed(1))) {
            open_path(&PathBuf::from(log_str));
        }
    } else {
        show_warn(&title, &header, &body);
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
    if crate::get_silent() {
        return true;
    }

    let title = locale_strings::title_setup(app_title);
    let path_str = root_path.display().to_string();

    let instruction;
    let body;
    let yes_label;

    if let Some(old_version) = installed_version {
        let old_str = old_version.to_string();
        let ver_str = app_version.to_string();
        if old_version < app_version {
            instruction = locale_strings::overwrite_older_installed(app_title);
            body = locale_strings::overwrite_update_body(&old_str, &ver_str);
            yes_label = locale_strings::btn_update();
        } else if old_version > app_version {
            instruction = locale_strings::overwrite_newer_installed(app_title);
            body = locale_strings::overwrite_downgrade_body(&old_str);
            yes_label = locale_strings::btn_downgrade();
        } else {
            instruction = locale_strings::overwrite_already_installed(app_title);
            body = locale_strings::overwrite_repair_body();
            yes_label = locale_strings::btn_repair();
        }
    } else {
        instruction = locale_strings::overwrite_already_installed(app_title);
        body = locale_strings::overwrite_repair_body();
        yes_label = locale_strings::btn_repair();
    }

    let footer = locale_strings::overwrite_footer(&path_str);

    let cancel_label = locale_strings::btn_cancel();
    let open_dir_label = locale_strings::btn_open_install_dir();
    let full_body = format!("{}\n\n{}", body, footer);

    let result = show_dialog(
        &title,
        &instruction,
        &full_body,
        XDialogIcon::Warning,
        vec![yes_label, open_dir_label, cancel_label],
    );

    match result {
        Some(XDialogResult::ButtonPressed(0)) => true,
        Some(XDialogResult::ButtonPressed(1)) => {
            open_path(root_path);
            show_overwrite_repair_dialog(app_title, app_version, app_id, root_path, _root_is_default, installed_version)
        }
        _ => false,
    }
}

pub fn show_generic_error(program_name: &str, error_string: &str) {
    let title = locale_strings::error_title(program_name);
    let header = locale_strings::error_header();
    show_error(&title, &header, error_string);
}

pub fn show_install_hook_warning(app_title: &str, _app_id: &str) {
    let title = locale_strings::title_setup(app_title);
    let header = locale_strings::install_hook_header();
    let body = locale_strings::install_hook_body();
    show_warn(&title, &header, &body);
}

pub fn show_uninstall_complete(app_title: &str) {
    let title = locale_strings::title_uninstall(app_title);
    let header = locale_strings::uninstall_header();
    let body = locale_strings::uninstall_body();
    show_info(&title, &header, &body);
}

pub fn show_setup_error(app_title: &str, error_string: &str) {
    let title = locale_strings::title_setup(app_title);
    let header = locale_strings::setup_error_header();
    show_error(&title, &header, error_string);
}

pub fn show_start_corrupt_error(app_title: &str) {
    let header = locale_strings::start_corrupt_header();
    let body = locale_strings::start_corrupt_body();
    show_error(app_title, &header, &body);
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
