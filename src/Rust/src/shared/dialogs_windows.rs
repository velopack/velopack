use super::{bundle::Manifest, dialogs_common::*, dialogs_const::*};
use anyhow::Result;
use std::path::PathBuf;
use winsafe::{self as w, co, prelude::*, WString};

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

    let mut setup_name = WString::from_str(format!("{} Uninstall", app.title));
    let mut instruction = WString::from_str(format!("{} uninstall has completed with errors.", app.title));
    let mut content = WString::from_str(
        "There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again.",
    );

    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.dwFlags = co::TDF::ENABLE_HYPERLINKS | co::TDF::SIZE_TO_CONTENT;
    config.dwCommonButtons = co::TDCBF::OK;
    config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::WARNING));
    config.set_pszWindowTitle(Some(&mut setup_name));
    config.set_pszMainInstruction(Some(&mut instruction));
    config.set_pszContent(Some(&mut content));

    let footer_path = log_path.map(|p| p.to_string_lossy().to_string()).unwrap_or("".to_string());
    let mut footer = WString::from_str(format!("Log file: '<A HREF=\"na\">{}</A>'", footer_path));
    if let Some(log_path) = log_path {
        if log_path.exists() {
            config.set_pszFooterIcon(w::IconId::Id(co::TD_ICON::INFORMATION.into()));
            config.set_pszFooter(Some(&mut footer));
            config.lpCallbackData = log_path as *const PathBuf as usize;
            config.pfCallback = Some(task_dialog_callback);
        }
    }

    let _ = w::TaskDialogIndirect(&config, None);
}

pub fn show_overwrite_repair_dialog(app: &Manifest, root_path: &PathBuf, root_is_default: bool) -> bool {
    if get_silent() {
        return true;
    }

    // these are the defaults, if we can't detect the current app version - we call it "Repair"
    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::WARNING));

    let mut setup_name = WString::from_str(format!("{} Setup {}", app.title, app.version));
    let mut instruction = WString::from_str(format!("{} is already installed.", app.title));
    let mut content = WString::from_str("This application is installed on your computer. If it is not functioning correctly, you can attempt to repair it.");
    let mut btn_yes_txt = WString::from_str(format!("Repair\nErase the application and re-install version {}.", app.version));
    let mut btn_cancel_txt = WString::from_str("Cancel\nBackup or save your work first");

    // if we can detect the current app version, we call it "Update" or "Downgrade"
    let possible_update = root_path.join("Update.exe");
    let old_app = super::detect_manifest_from_update_path(&possible_update).map(|v| v.1).ok();
    if let Some(old) = old_app {
        if old.version < app.version {
            instruction = WString::from_str(format!("An older version of {} is installed.", app.title));
            content = WString::from_str(format!("Would you like to update from {} to {}?", old.version, app.version));
            btn_yes_txt = WString::from_str(format!("Update\nTo version {}", app.version));
            config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::INFORMATION));
        } else if old.version > app.version {
            instruction = WString::from_str(format!("A newer version of {} is installed.", app.title));
            content = WString::from_str(format!(
                "You already have {} installed. Would you like to downgrade this application to an older version?",
                old.version
            ));
            btn_yes_txt = WString::from_str(format!("Downgrade\nTo version {}", app.version));
        }
    }

    let mut footer = if root_is_default {
        WString::from_str(format!("The install directory is '<A HREF=\"na\">%LocalAppData%\\{}</A>'", app.id))
    } else {
        WString::from_str(format!("The install directory is '<A HREF=\"na\">{}</A>'", root_path.display()))
    };

    let mut btn_yes = w::TASKDIALOG_BUTTON::default();
    btn_yes.set_nButtonID(co::DLGID::YES.into());
    btn_yes.set_pszButtonText(Some(&mut btn_yes_txt));

    let mut btn_cancel = w::TASKDIALOG_BUTTON::default();
    btn_cancel.set_nButtonID(co::DLGID::CANCEL.into());
    btn_cancel.set_pszButtonText(Some(&mut btn_cancel_txt));

    let mut custom_btns = Vec::with_capacity(2);
    custom_btns.push(btn_yes);
    custom_btns.push(btn_cancel);

    config.dwFlags = co::TDF::ENABLE_HYPERLINKS | co::TDF::USE_COMMAND_LINKS;
    config.set_pButtons(Some(&mut custom_btns));
    config.set_pszWindowTitle(Some(&mut setup_name));
    config.set_pszMainInstruction(Some(&mut instruction));
    config.set_pszContent(Some(&mut content));
    config.set_pszFooterIcon(w::IconId::Id(co::TD_ICON::INFORMATION.into()));
    config.set_pszFooter(Some(&mut footer));

    config.lpCallbackData = root_path as *const PathBuf as usize;
    config.pfCallback = Some(task_dialog_callback);

    let (btn, _) = w::TaskDialogIndirect(&config, None).ok().unwrap_or_else(|| (co::DLGID::YES, 0));
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
    let mut ok_text_buf = WString::from_opt_str(ok_text);
    let mut custom_btns = if ok_text.is_some() {
        let mut td_btn = w::TASKDIALOG_BUTTON::default();
        td_btn.set_nButtonID(co::DLGID::OK.into());
        td_btn.set_pszButtonText(Some(&mut ok_text_buf));
        let mut custom_btns = Vec::with_capacity(1);
        custom_btns.push(td_btn);
        custom_btns
    } else {
        Vec::<w::TASKDIALOG_BUTTON>::default()
    };

    let mut tdc = w::TASKDIALOGCONFIG::default();
    tdc.hwndParent = unsafe { hparent.raw_copy() };
    tdc.dwFlags = co::TDF::ALLOW_DIALOG_CANCELLATION | co::TDF::POSITION_RELATIVE_TO_WINDOW;
    tdc.dwCommonButtons = btns.to_win();
    tdc.set_pszMainIcon(w::IconIdTdicon::Tdicon(ico.to_win()));

    if ok_text.is_some() {
        tdc.set_pButtons(Some(&mut custom_btns));
    }

    let mut title_buf = WString::from_str(title);
    tdc.set_pszWindowTitle(Some(&mut title_buf));

    let mut header_buf = WString::from_opt_str(header);
    if header.is_some() {
        tdc.set_pszMainInstruction(Some(&mut header_buf));
    }

    let mut body_buf = WString::from_str(body);
    tdc.set_pszContent(Some(&mut body_buf));

    let result = w::TaskDialogIndirect(&tdc, None).map(|(dlg_id, _)| dlg_id)?;
    Ok(DialogResult::from_win(result))
}

pub fn generate_alert(title: &str, header: Option<&str>, body: &str, ok_text: Option<&str>, btns: DialogButton, ico: DialogIcon) -> Result<()> {
    let _ = generate_confirm(title, header, body, ok_text, btns, ico).map(|_| ())?;
    Ok(())
}
