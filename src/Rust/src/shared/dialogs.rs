use super::bundle::Manifest;
use std::{
    path::PathBuf,
    sync::atomic::{AtomicBool, Ordering},
};
use winsafe::{self as w, co, prelude::*, WString};

static SILENT: AtomicBool = AtomicBool::new(false);

pub fn set_silent(silent: bool) {
    SILENT.store(silent, Ordering::Relaxed);
}

pub fn get_silent() -> bool {
    SILENT.load(Ordering::Relaxed)
}

pub fn show_error<T: AsRef<str>, T2: AsRef<str>>(err: T, title: T2) {
    if get_silent() {
        return;
    }
    let err = err.as_ref();
    let title = title.as_ref();
    let _ = w::HWND::GetDesktopWindow().MessageBox(err, title, co::MB::ICONERROR);
}

pub fn show_info<T: AsRef<str>, T2: AsRef<str>>(info: T, title: T2) {
    if get_silent() {
        return;
    }
    let info = info.as_ref();
    let title = title.as_ref();
    let _ = w::HWND::GetDesktopWindow().MessageBox(info, title, co::MB::ICONINFORMATION);
}

pub fn show_warning<T: AsRef<str>, T2: AsRef<str>>(warning: T, title: T2) {
    if get_silent() {
        return;
    }
    let warning = warning.as_ref();
    let title = title.as_ref();
    let _ = w::HWND::GetDesktopWindow().MessageBox(warning, title, co::MB::ICONWARNING);
}

pub fn show_restart_required(app: &Manifest) {
    if get_silent() {
        return;
    }

    let hwnd = w::HWND::GetDesktopWindow();
    let _ = warn(
        &hwnd,
        format!("{} Setup {}", app.title, app.version).as_str(),
        Some("Restart Required"),
        "A restart is required before Setup can continue. Please restart your computer and try again.",
    );
}

pub fn show_missing_dependencies_dialog(app: &Manifest, depedency_string: &str) -> bool {
    if get_silent() {
        return true;
    }

    let hwnd = w::HWND::GetDesktopWindow();
    ok_cancel(
        &hwnd,
        format!("{} Setup {}", app.title, app.version).as_str(),
        Some(format!("{} has missing system dependencies.", app.title).as_str()),
        format!("{} requires the following packages to be installed: {}, would you like to continue?", app.title, depedency_string).as_str(),
        Some("Install"),
    )
    .unwrap_or(false)
}

pub fn show_uninstall_complete_with_errors_dialog(app: &Manifest, log_path: &PathBuf) {
    if get_silent() {
        return;
    }

    let mut setup_name = WString::from_str(format!("{} Uninstall", app.title));
    let mut instruction = WString::from_str(format!("{} uninstall has completed with errors.", app.title));
    let mut content = WString::from_str(
        "There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again.",
    );
    let mut footer = WString::from_str(format!("Log file: '<A HREF=\"na\">{}</A>'", log_path.display()));

    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.dwFlags = co::TDF::ENABLE_HYPERLINKS | co::TDF::SIZE_TO_CONTENT;
    config.dwCommonButtons = co::TDCBF::OK;
    config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::WARNING));
    config.set_pszWindowTitle(Some(&mut setup_name));
    config.set_pszMainInstruction(Some(&mut instruction));
    config.set_pszContent(Some(&mut content));

    if log_path.exists() {
        config.set_pszFooterIcon(w::IconId::Id(co::TD_ICON::INFORMATION.into()));
        config.set_pszFooter(Some(&mut footer));
    }

    config.lpCallbackData = log_path as *const PathBuf as usize;
    config.pfCallback = Some(task_dialog_callback);

    let _ = w::TaskDialogIndirect(&config, None);
}

pub fn show_overwrite_repair_dialog(app: &Manifest, root_path: &PathBuf, root_is_default: bool) -> bool {
    if get_silent() {
        return true;
    }
    let mut setup_name = WString::from_str(format!("{} Setup {}", app.title, app.version));
    let mut instruction = WString::from_str(format!("{} is already installed.", app.title));
    let mut content = WString::from_str("This application is installed on your computer. You can attempt to repair the install, this may end up installing an older version of the application and may delete any saved settings/preferences.");
    let mut footer = if root_is_default {
        WString::from_str(format!("The install directory is '<A HREF=\"na\">%LocalAppData%\\{}</A>'", app.id))
    } else {
        WString::from_str(format!("The install directory is '<A HREF=\"na\">{}</A>'", root_path.display()))
    };

    let mut btn_yes_txt = WString::from_str(format!("Repair\nErase the application and install version {}.", app.version));
    let mut btn_yes = w::TASKDIALOG_BUTTON::default();
    btn_yes.set_nButtonID(co::DLGID::YES.into());
    btn_yes.set_pszButtonText(Some(&mut btn_yes_txt));

    let mut btn_cancel_txt = WString::from_str("Cancel\nBackup or save your work first.");
    let mut btn_cancel = w::TASKDIALOG_BUTTON::default();
    btn_cancel.set_nButtonID(co::DLGID::CANCEL.into());
    btn_cancel.set_pszButtonText(Some(&mut btn_cancel_txt));

    let mut custom_btns = Vec::with_capacity(2);
    custom_btns.push(btn_yes);
    custom_btns.push(btn_cancel);

    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.dwFlags = co::TDF::ENABLE_HYPERLINKS | co::TDF::USE_COMMAND_LINKS | co::TDF::SIZE_TO_CONTENT;
    config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::WARNING));
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

pub fn error(hparent: &w::HWND, title: &str, header: Option<&str>, body: &str) -> w::HrResult<()> {
    generate(hparent, title, header, body, None, co::TDCBF::OK, co::TD_ICON::ERROR).map(|_| ())
}

pub fn warn(hparent: &w::HWND, title: &str, header: Option<&str>, body: &str) -> w::HrResult<()> {
    generate(hparent, title, header, body, None, co::TDCBF::OK, co::TD_ICON::WARNING).map(|_| ())
}

pub fn info(hparent: &w::HWND, title: &str, header: Option<&str>, body: &str) -> w::HrResult<()> {
    generate(hparent, title, header, body, None, co::TDCBF::OK, co::TD_ICON::INFORMATION).map(|_| ())
}

#[must_use]
pub fn ok_cancel(hparent: &w::HWND, title: &str, header: Option<&str>, body: &str, ok_text: Option<&str>) -> w::HrResult<bool> {
    let mut btns = co::TDCBF::CANCEL;
    if ok_text.is_none() {
        btns |= co::TDCBF::OK;
    }

    generate(hparent, title, header, body, ok_text, btns, co::TD_ICON::WARNING).map(|dlg_id| dlg_id == co::DLGID::OK)
}

pub fn yes_no(hparent: &w::HWND, title: &str, header: Option<&str>, body: &str) -> w::HrResult<bool> {
    generate(hparent, title, header, body, None, co::TDCBF::YES | co::TDCBF::NO, co::TD_ICON::WARNING).map(|dlg_id| dlg_id == co::DLGID::YES)
}

pub fn yes_no_cancel(hparent: &w::HWND, title: &str, header: Option<&str>, body: &str) -> w::HrResult<co::DLGID> {
    generate(hparent, title, header, body, None, co::TDCBF::YES | co::TDCBF::NO | co::TDCBF::CANCEL, co::TD_ICON::WARNING)
}

fn generate(
    hparent: &w::HWND,
    title: &str,
    header: Option<&str>,
    body: &str,
    ok_text: Option<&str>,
    btns: co::TDCBF,
    ico: co::TD_ICON,
) -> w::HrResult<co::DLGID> {
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
    tdc.dwCommonButtons = btns;
    tdc.set_pszMainIcon(w::IconIdTdicon::Tdicon(ico));

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

    w::TaskDialogIndirect(&tdc, None).map(|(dlg_id, _)| dlg_id)
}
