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

pub struct XDialogProgressReporter {
    proxy: xdialog::ProgressDialogProxy,
}

impl XDialogProgressReporter {
    pub fn new(proxy: xdialog::ProgressDialogProxy) -> Self {
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
        let _ = self.proxy.set_text(text.to_string());
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

pub fn show_progress_dialog(title: &str, body: &str) -> Box<dyn ProgressReporter> {
    match xdialog::show_progress(title, body, "", xdialog::XDialogIcon::Information) {
        Ok(proxy) => Box::new(XDialogProgressReporter::new(proxy)),
        Err(e) => {
            warn!("Failed to show progress dialog: {:?}", e);
            Box::new(NoopProgressReporter)
        }
    }
}

pub fn show_apply_progress(app_name: &str, version: &str) -> Box<dyn ProgressReporter> {
    if crate::dialogs::get_silent() {
        return Box::new(NoopProgressReporter);
    }
    let mut args = fluent::FluentArgs::new();
    args.set("app", app_name.to_string());
    args.set("version", version.to_string());
    let title = crate::localization::t("apply-title", Some(&args));
    let body = crate::localization::t("apply-body", Some(&args));
    show_progress_dialog(&title, &body)
}
