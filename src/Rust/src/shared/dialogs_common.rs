use super::dialogs::{generate_alert, generate_confirm};
use super::dialogs_const::*;
use std::sync::atomic::{AtomicBool, Ordering};

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
