use super::{bundle::Manifest, dialogs_common::*, dialogs_const::*};
use anyhow::Result;
use std::path::PathBuf;
use winsafe::{self as w, co, prelude::*};

pub fn show_restart_required(app: &Manifest) {
    show_warn(
        format!("{} Setup {}", app.title, app.version).as_str(),
        Some("Restart Required"),
        "A restart is required before Setup can continue. Please restart your computer and try again.",
    );
}

pub fn show_update_missing_dependencies_dialog(app: &Manifest, depedency_string: &str, from: &semver::Version, to: &semver::Version) -> bool {
    if get_silent() {
        // this has different behavior to show_setup_missing_dependencies_dialog,
        // if silent is true then we will bail because the app is probably exiting
        // and installing dependencies may result in a UAC prompt.
        warn!("Cancelling pre-requisite installation because silent flag is true.");
        return false;
    }

    show_ok_cancel(
        format!("{} Update", app.title).as_str(),
        Some(format!("{} would like to update from {} to {}", app.title, from, to).as_str()),
        format!("{} {to} has missing dependencies which need to be installed: {}, would you like to continue?", app.title, depedency_string).as_str(),
        Some("Install & Update"),
    )
}

pub fn show_setup_missing_dependencies_dialog(app: &Manifest, depedency_string: &str) -> bool {
    if get_silent() {
        return true;
    }

    show_ok_cancel(
        format!("{} Setup {}", app.title, app.version).as_str(),
        Some(format!("{} has missing system dependencies.", app.title).as_str()),
        format!("{} requires the following packages to be installed: {}, would you like to continue?", app.title, depedency_string).as_str(),
        Some("Install"),
    )
}

pub fn show_uninstall_complete_with_errors_dialog(app: &Manifest, log_path: Option<&PathBuf>) {
    if get_silent() {
        return;
    }

    let setup_name = format!("{} Uninstall", app.title);
    let instruction = format!("{} uninstall has completed with errors.", app.title);
    let content =
        "There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again.";

    let mut config: w::TASKDIALOGCONFIG = Default::default();

    config.flags = co::TDF::ENABLE_HYPERLINKS | co::TDF::SIZE_TO_CONTENT;
    config.common_buttons = co::TDCBF::OK;
    config.main_icon = w::IconIdTd::Td(co::TD_ICON::WARNING);
    config.window_title = Some(setup_name.as_str());
    config.main_instruction = Some(instruction.as_str());
    config.content = Some(content);

    let footer_path = log_path.map(|p| p.to_string_lossy().to_string()).unwrap_or("".to_string());
    let footer = format!("Log file: '<A HREF=\"na\">{}</A>'", footer_path);
    if let Some(log_path) = log_path {
        if log_path.exists() {
            config.footer_icon = w::IconId::Id(co::TD_ICON::INFORMATION.into());
            config.footer_text = Some(footer.as_str());
            config.callback_data = log_path as *const PathBuf as usize;
            config.callback = Some(task_dialog_callback);
        }
    }

    let _ = w::TaskDialogIndirect(&config);
}

pub fn show_overwrite_repair_dialog(app: &Manifest, root_path: &PathBuf, root_is_default: bool) -> bool {
    if get_silent() {
        return true;
    }

    // these are the defaults, if we can't detect the current app version - we call it "Repair"
    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.main_icon = w::IconIdTd::Td(co::TD_ICON::WARNING);

    let mut setup_name = format!("{} Setup {}", app.title, app.version);
    let mut instruction = format!("{} is already installed.", app.title);
    let mut content = "This application is installed on your computer. If it is not functioning correctly, you can attempt to repair it.".to_owned();
    let mut btn_yes_txt = format!("Repair\nErase the application and re-install version {}.", app.version);
    let btn_cancel_txt = "Cancel\nBackup or save your work first";

    // if we can detect the current app version, we call it "Update" or "Downgrade"
    let possible_update = root_path.join("Update.exe");
    let old_app = super::detect_manifest_from_update_path(&possible_update).map(|v| v.1).ok();
    if let Some(old) = old_app {
        if old.version < app.version {
            instruction = format!("An older version of {} is installed.", app.title);
            content = format!("Would you like to update from {} to {}?", old.version, app.version);
            btn_yes_txt = format!("Update\nTo version {}", app.version);
            config.main_icon = w::IconIdTd::Td(co::TD_ICON::INFORMATION);
        } else if old.version > app.version {
            instruction = format!("A newer version of {} is installed.", app.title);
            content = format!("You already have {} installed. Would you like to downgrade this application to an older version?", old.version);
            btn_yes_txt = format!("Downgrade\nTo version {}", app.version);
        }
    }

    let mut footer = if root_is_default {
        format!("The install directory is '<A HREF=\"na\">%LocalAppData%\\{}</A>'", app.id)
    } else {
        format!("The install directory is '<A HREF=\"na\">{}</A>'", root_path.display())
    };

    let btn_yes_txt = btn_yes_txt.as_str();
    let buttons = [(co::DLGID::YES.into(), btn_yes_txt), (co::DLGID::CANCEL.into(), btn_cancel_txt)];
    config.buttons = &buttons;

    config.flags = co::TDF::ENABLE_HYPERLINKS | co::TDF::USE_COMMAND_LINKS;
    config.window_title = Some(&mut setup_name);
    config.main_instruction = Some(&mut instruction);
    config.content = Some(&mut content);
    config.footer_icon = w::IconId::Id(co::TD_ICON::INFORMATION.into());
    config.footer_text = Some(&mut footer);
    config.callback_data = root_path as *const PathBuf as usize;
    config.callback = Some(task_dialog_callback);

    let (btn, _, _) = w::TaskDialogIndirect(&config).ok().unwrap_or_else(|| (co::DLGID::YES, 0, true));
    return btn == co::DLGID::YES;
}

extern "system" fn task_dialog_callback(_: w::HWND, msg: co::TDN, _: usize, _: isize, lp_ref_data: usize) -> co::HRESULT {
    if msg == co::TDN::HYPERLINK_CLICKED {
        let raw = lp_ref_data as *const PathBuf;
        let path: &PathBuf = unsafe { &*raw };
        let dir = path.to_str().unwrap();
        w::HWND::GetDesktopWindow().ShellExecute("open", &dir, None, None, co::SW::SHOWDEFAULT).ok();
        return co::HRESULT::S_FALSE; // do not close dialog
    }
    return co::HRESULT::S_OK; // close dialog on button press
}

pub fn generate_confirm(title: &str, header: Option<&str>, body: &str, ok_text: Option<&str>, btns: DialogButton, ico: DialogIcon) -> Result<DialogResult> {
    let hparent = w::HWND::GetDesktopWindow();
    let hwnd = unsafe { hparent.raw_copy() };
    let custom_btns: Vec<(u16, &str)> = if ok_text.is_some() { vec![(co::DLGID::OK.into(), ok_text.unwrap())] } else { vec![] };

    let mut tdc = w::TASKDIALOGCONFIG::default();
    tdc.hwnd_parent = Some(&hwnd);
    tdc.flags = co::TDF::ALLOW_DIALOG_CANCELLATION | co::TDF::POSITION_RELATIVE_TO_WINDOW;
    tdc.common_buttons = btns.to_win();
    tdc.main_icon = w::IconIdTd::Td(ico.to_win());

    if ok_text.is_some() {
        tdc.buttons = &custom_btns;
    }

    tdc.window_title = Some(title);
    tdc.main_instruction = header;
    tdc.content = Some(body);

    let result = w::TaskDialogIndirect(&tdc).map(|(dlg_id, _, _)| dlg_id)?;
    Ok(DialogResult::from_win(result))
}

pub fn generate_alert(title: &str, header: Option<&str>, body: &str, ok_text: Option<&str>, btns: DialogButton, ico: DialogIcon) -> Result<()> {
    let _ = generate_confirm(title, header, body, ok_text, btns, ico).map(|_| ())?;
    Ok(())
}
