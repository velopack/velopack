use super::dialogs_const::*;
use anyhow::Result;
use core_foundation::base::TCFType;
use core_foundation::string::CFString;
use core_foundation_sys::user_notification::CFUserNotificationDisplayAlert;
use std::ptr::null;

fn get_dialog_icon(ico: DialogIcon) -> usize {
    match ico {
        DialogIcon::Warning => 2,
        DialogIcon::Error => 0,
        DialogIcon::Information => 1,
    }
}

pub fn generate_alert(
    title: &str,
    header: Option<&str>,
    body: &str,
    _ok_text: Option<&str>,
    _btns: DialogButton,
    ico: DialogIcon,
) -> Result<()> {
    let mut body = body.to_string();
    if let Some(h) = header {
        body = format!("{}\n{}", h, body);
    }

    let default = CFString::from_static_string("Ok");
    let header = CFString::new(title);
    let message = CFString::new(&body);

    let mut response = 0;
    unsafe {
        CFUserNotificationDisplayAlert(
            0f64,
            get_dialog_icon(ico),
            null(),
            null(),
            null(),
            header.as_CFTypeRef() as _,
            message.as_CFTypeRef() as _,
            default.as_CFTypeRef() as _,
            null(),
            null(),
            &mut response,
        );
    }

    Ok(())
}

pub fn generate_confirm(
    title: &str,
    header: Option<&str>,
    body: &str,
    _ok_text: Option<&str>,
    _btns: DialogButton,
    ico: DialogIcon,
) -> Result<DialogResult> {
    let mut body = body.to_string();
    if let Some(h) = header {
        body = format!("{}\n{}", h, body);
    }

    let default = CFString::from_static_string("Yes");
    let alternate = CFString::from_static_string("No");
    let header = CFString::new(title);
    let message = CFString::new(&body);

    let mut response = 0;
    unsafe {
        CFUserNotificationDisplayAlert(
            0f64,
            get_dialog_icon(ico),
            null(),
            null(),
            null(),
            header.as_CFTypeRef() as _,
            message.as_CFTypeRef() as _,
            default.as_CFTypeRef() as _,
            alternate.as_CFTypeRef() as _,
            null(),
            &mut response,
        );
    }

    if response == 0 {
        Ok(DialogResult::Ok)
    } else {
        Ok(DialogResult::Cancel)
    }
}
