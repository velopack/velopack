use super::dialogs::{generate_alert, generate_confirm};
use super::dialogs_const::*;
use std::sync::atomic::{AtomicBool, Ordering};
use anyhow::{Result, bail};

static SILENT: AtomicBool = AtomicBool::new(false);

pub fn set_silent(silent: bool) {
    SILENT.store(silent, Ordering::Relaxed);
}

pub fn get_silent() -> bool {
    SILENT.load(Ordering::Relaxed)
}

pub fn show_error(title: &str, header: Option<&str>, body: &str) {
    if !get_silent() {
        let _ = generate_alert(title, header, body, None, DialogButton::Ok, DialogIcon::Error).map(|_| ());
    }
}

pub fn show_warn(title: &str, header: Option<&str>, body: &str) {
    if !get_silent() {
        let _ = generate_alert(title, header, body, None, DialogButton::Ok, DialogIcon::Warning).map(|_| ());
    }
}

pub fn show_info(title: &str, header: Option<&str>, body: &str) {
    if !get_silent() {
        let _ = generate_alert(title, header, body, None, DialogButton::Ok, DialogIcon::Information).map(|_| ());
    }
}

pub fn show_ok_cancel(title: &str, header: Option<&str>, body: &str, ok_text: Option<&str>) -> bool {
    if get_silent() {
        return false;
    }

    let mut btns = DialogButton::Cancel;
    if ok_text.is_none() {
        btns |= DialogButton::Ok;
    }
    generate_confirm(title, header, body, ok_text, btns, DialogIcon::Warning).map(|dlg_id| dlg_id == DialogResult::Ok).unwrap_or(false)
}

pub fn ask_user_to_elevate(app_to: &crate::bundle::Manifest, noelevate: bool) -> Result<()> {
    if noelevate {
        bail!("Not allowed to ask for elevated permissions because --noelevate flag is set.");
    }

    if get_silent() {
        bail!("Not allowed to ask for elevated permissions because --silent flag is set.");
    }

    let title = format!("{} Update", app_to.title);
    let body =
        format!("{} would like to update to version {}, but requested elevated permissions to do so. Would you like to proceed?", app_to.title, app_to.version);

    info!("Showing user elevation prompt?");
    if show_ok_cancel(title.as_str(), None, body.as_str(), Some("Install Update")) {
        info!("User answered yes to elevation...");
        Ok(())
    } else {
        bail!("User cancelled elevation prompt.");
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
    assert!(!show_ok_cancel("Ok/Cancel", None, "This is a question.", Some("Ok")));
}

// pub fn yes_no(title: &str, header: Option<&str>, body: &str) -> Result<bool> {
//     generate(title, header, body, None, co::TDCBF::YES | co::TDCBF::NO, co::TD_ICON::WARNING).map(|dlg_id| dlg_id == co::DLGID::YES)
// }

// pub fn yes_no_cancel(title: &str, header: Option<&str>, body: &str) -> Result<co::DLGID> {
//     generate(title, header, body, None, co::TDCBF::YES | co::TDCBF::NO | co::TDCBF::CANCEL, co::TD_ICON::WARNING)
// }
