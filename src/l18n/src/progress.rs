use crate::types::{MSG_CLOSE, MSG_INDEFINITE};
use std::sync::mpsc::Sender;

pub trait ProgressReporter: Send {
    fn set_progress(&self, value: i16);
    fn set_indeterminate(&self);
    fn set_text(&self, text: &str);
    fn close(&self);
}

pub struct ChannelProgressReporter {
    tx: Sender<i16>,
}

impl ChannelProgressReporter {
    pub fn new(tx: Sender<i16>) -> Self {
        Self { tx }
    }
}

impl ProgressReporter for ChannelProgressReporter {
    fn set_progress(&self, value: i16) {
        let _ = self.tx.send(value);
    }

    fn set_indeterminate(&self) {
        let _ = self.tx.send(MSG_INDEFINITE);
    }

    fn set_text(&self, _text: &str) {
        // Channel-based progress does not support changing text
    }

    fn close(&self) {
        let _ = self.tx.send(MSG_CLOSE);
    }
}

struct XDialogProgressReporter {
    proxy: xdialog::ProgressDialogProxy,
}

impl XDialogProgressReporter {
    fn new(proxy: xdialog::ProgressDialogProxy) -> Self {
        Self { proxy }
    }
}

impl ProgressReporter for XDialogProgressReporter {
    fn set_progress(&self, value: i16) {
        let _ = self.proxy.set_value(value as f32 / 100.0);
    }

    fn set_indeterminate(&self) {
        let _ = self.proxy.set_indeterminate();
    }

    fn set_text(&self, text: &str) {
        let _ = self.proxy.set_text(text);
    }

    fn close(&self) {
        let _ = self.proxy.close();
    }
}

pub struct NoopProgressReporter;

impl ProgressReporter for NoopProgressReporter {
    fn set_progress(&self, _value: i16) {}
    fn set_indeterminate(&self) {}
    fn set_text(&self, _text: &str) {}
    fn close(&self) {}
}

fn show_progress_dialog(title: &str, header: &str, body: &str) -> Box<dyn ProgressReporter> {
    if crate::dialogs::get_silent() {
        return Box::new(NoopProgressReporter);
    }
    let buttons = if cfg!(target_os = "windows") {
        vec![crate::locale_strings::btn_hide()]
    } else {
        vec![]
    };
    let options = xdialog::XDialogOptions {
        title: title.to_string(),
        main_instruction: header.to_string(),
        message: body.to_string(),
        icon: xdialog::XDialogIcon::Information,
        buttons,
    };
    match xdialog::show_progress_ex(options) {
        Ok(proxy) => Box::new(XDialogProgressReporter::new(proxy)),
        Err(e) => {
            warn!("Failed to show progress dialog: {:?}", e);
            Box::new(NoopProgressReporter)
        }
    }
}

pub fn show_apply_progress(app_name: &str, version: &str) -> Box<dyn ProgressReporter> {
    let title = crate::locale_strings::title_update(app_name);
    let header = crate::locale_strings::apply_header();
    let body = crate::locale_strings::apply_body(version);
    show_progress_dialog(&title, &header, &body)
}

pub fn show_splash_progress(app_name: &str, app_version: &str) -> Box<dyn ProgressReporter> {
    let title = crate::locale_strings::title_setup(app_name);
    let header = crate::locale_strings::splash_header(app_name);
    let body = crate::locale_strings::splash_body(app_name, app_version);
    show_progress_dialog(&title, &header, &body)
}

pub fn show_deps_download_progress(dep_name: &str, is_update: bool) -> Box<dyn ProgressReporter> {
    let title = if is_update {
        crate::locale_strings::title_update(dep_name)
    } else {
        crate::locale_strings::title_setup(dep_name)
    };
    let header = crate::locale_strings::deps_download_header();
    let body = crate::locale_strings::deps_download_body(dep_name);
    show_progress_dialog(&title, &header, &body)
}
