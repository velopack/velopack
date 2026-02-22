use enum_flags::enum_flags;

#[enum_flags]
#[derive(PartialEq, Clone, Copy, strum::IntoStaticStr)]
#[repr(u8)]
pub enum DialogButton {
    Ok = 1,
    Yes = 2,
    No = 4,
    Cancel = 8,
    Retry = 16,
    Close = 32,
}

#[derive(PartialEq, Clone, Copy, strum::IntoStaticStr)]
#[repr(u8)]
pub enum DialogIcon {
    Warning = 1,
    Error = 2,
    Information = 4,
}

#[derive(PartialEq, Clone, Copy, strum::IntoStaticStr)]
#[repr(u8)]
pub enum DialogResult {
    Unknown = 0,
    Ok = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
    Tryagain = 10,
    Continue = 11,
}

#[cfg(target_os = "windows")]
impl DialogButton {
    pub fn to_win(&self) -> windows::Win32::UI::Controls::TASKDIALOG_COMMON_BUTTON_FLAGS {
        use windows::Win32::UI::Controls::*;
        let mut result = TASKDIALOG_COMMON_BUTTON_FLAGS(0);
        if self.has_ok() {
            result |= TDCBF_OK_BUTTON;
        }
        if self.has_yes() {
            result |= TDCBF_YES_BUTTON;
        }
        if self.has_no() {
            result |= TDCBF_NO_BUTTON;
        }
        if self.has_cancel() {
            result |= TDCBF_CANCEL_BUTTON;
        }
        if self.has_retry() {
            result |= TDCBF_RETRY_BUTTON;
        }
        if self.has_close() {
            result |= TDCBF_CLOSE_BUTTON;
        }
        result
    }
}

impl DialogIcon {
    #[cfg(target_os = "windows")]
    pub fn to_win(&self) -> windows::core::PCWSTR {
        use windows::Win32::UI::Controls::*;
        match self {
            DialogIcon::Warning => TD_WARNING_ICON,
            DialogIcon::Error => TD_ERROR_ICON,
            DialogIcon::Information => TD_INFORMATION_ICON,
        }
    }
}

#[cfg(target_os = "windows")]
impl DialogResult {
    pub fn from_win(dlg_id: i32) -> DialogResult {
        use windows::Win32::UI::WindowsAndMessaging::*;
        let dlg_id = MESSAGEBOX_RESULT(dlg_id);
        match dlg_id {
            IDOK => DialogResult::Ok,
            IDCANCEL => DialogResult::Cancel,
            IDABORT => DialogResult::Abort,
            IDRETRY => DialogResult::Retry,
            IDIGNORE => DialogResult::Ignore,
            IDYES => DialogResult::Yes,
            IDNO => DialogResult::No,
            IDTRYAGAIN => DialogResult::Tryagain,
            IDCONTINUE => DialogResult::Continue,
            _ => DialogResult::Unknown,
        }
    }
}
