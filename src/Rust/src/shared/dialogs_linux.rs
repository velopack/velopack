use dialog::{DialogBox, Choice};
use super::dialogs_const::*;
use anyhow::{Result, anyhow};

pub fn generate_alert(title: &str, header: Option<&str>, body: &str, _ok_text: Option<&str>, _btns: DialogButton, _ico: DialogIcon) -> Result<()> {
    let mut body = body.to_string();
    if let Some(h) = header {
        body = format!("{}\n{}", h, body);
    }

    dialog::Message::new(body).title(title).show()
        .map_err(|e| anyhow!("Failed to open dialog ({})", e))?;
    Ok(())
}

pub fn generate_confirm(title: &str, header: Option<&str>, body: &str, _ok_text: Option<&str>, _btns: DialogButton, _ico: DialogIcon) -> Result<DialogResult> {
    let mut body = body.to_string();
    if let Some(h) = header {
        body = format!("{}\n{}", h, body);
    }

    let result = dialog::Question::new(body).title(title).show()
        .map_err(|e| anyhow!("Failed to open dialog ({})", e))?;

    Ok(match result {
        Choice::Cancel => DialogResult::Cancel,
        Choice::No => DialogResult::No,
        Choice::Yes => DialogResult::Yes,
    })
}
