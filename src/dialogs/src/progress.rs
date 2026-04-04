use crate::backends::{DialogProxy, XDialogError, XDialogIcon, XDialogOptions, XDialogResult};

pub struct NoopDialogProxy;

impl DialogProxy for NoopDialogProxy {
    fn close(&self) {}
    fn set_progress_value(&self, _: f32) {}
    fn set_progress_value_i16(&self, _: i16) {}
    fn set_progress_text(&self, _: &str) {}
    fn set_progress_indeterminate(&self) {}
    fn get_result(&self, _: Option<std::time::Duration>) -> Result<XDialogResult, XDialogError> {
        Ok(XDialogResult::SilentMode)
    }
}

fn show_progress_dialog(title: &str, header: &str, body: &str) -> Box<dyn DialogProxy> {
    if crate::get_silent() {
        return Box::new(NoopDialogProxy);
    }
    match crate::with_manager(|mgr| {
        mgr.show(XDialogOptions {
            title: title.to_string(),
            main_instruction: header.to_string(),
            message: body.to_string(),
            icon: XDialogIcon::Information,
            buttons: vec![],
            has_progress: true,
        })
    }) {
        Ok(proxy) => proxy,
        Err(e) => {
            warn!("Failed to show progress dialog: {:?}", e);
            Box::new(NoopDialogProxy)
        }
    }
}

pub fn show_apply_progress(app_name: &str, version: &str) -> Box<dyn DialogProxy> {
    let title = crate::locale::strings::title_update(app_name);
    let header = crate::locale::strings::apply_header();
    let body = crate::locale::strings::apply_body(version);
    show_progress_dialog(&title, &header, &body)
}

#[cfg(windows)]
pub fn show_deps_download_progress(dep_name: &str, is_update: bool) -> Box<dyn DialogProxy> {
    let title = if is_update {
        crate::locale::strings::title_update(dep_name)
    } else {
        crate::locale::strings::title_setup(dep_name)
    };
    let header = crate::locale::strings::deps_download_header();
    let body = crate::locale::strings::deps_download_body(dep_name);
    show_progress_dialog(&title, &header, &body)
}

// only used by the splash module
#[cfg(windows)]
pub(crate) fn show_splash_progress(app_name: &str, app_version: &str) -> Box<dyn DialogProxy> {
    let title = crate::locale::strings::title_setup(app_name);
    let header = crate::locale::strings::splash_header(app_name);
    let body = crate::locale::strings::splash_body(app_name, app_version);
    show_progress_dialog(&title, &header, &body)
}
