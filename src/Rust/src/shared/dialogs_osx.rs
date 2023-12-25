use super::dialogs_const::*;
use anyhow::Result;
use native_dialog::MessageDialog;

pub fn generate_alert(title: &str, header: Option<&str>, body: &str, _ok_text: Option<&str>, _btns: DialogButton, ico: DialogIcon) -> Result<()> {
    let mut body = body.to_string();
    if let Some(h) = header {
        body = format!("{}\n{}", h, body);
    }

    MessageDialog::new().set_type(ico.to_native()).set_title(title).set_text(&body).show_alert()?;

    Ok(())
}

pub fn generate_confirm(title: &str, header: Option<&str>, body: &str, _ok_text: Option<&str>, _btns: DialogButton, ico: DialogIcon) -> Result<DialogResult> {
    let mut body = body.to_string();
    if let Some(h) = header {
        body = format!("{}\n{}", h, body);
    }

    let result = MessageDialog::new().set_type(ico.to_native()).set_title(title).set_text(&body).show_confirm()?;
    if result {
        Ok(DialogResult::Ok)
    } else {
        Ok(DialogResult::Cancel)
    }
}
