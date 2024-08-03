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
    pub fn to_win(&self) -> winsafe::co::TDCBF {
        let mut result = unsafe { winsafe::co::TDCBF::from_raw(0) };
        if self.has_ok() {
            result |= winsafe::co::TDCBF::OK;
        }
        if self.has_yes() {
            result |= winsafe::co::TDCBF::YES;
        }
        if self.has_no() {
            result |= winsafe::co::TDCBF::NO;
        }
        if self.has_cancel() {
            result |= winsafe::co::TDCBF::CANCEL;
        }
        if self.has_retry() {
            result |= winsafe::co::TDCBF::RETRY;
        }
        if self.has_close() {
            result |= winsafe::co::TDCBF::CLOSE;
        }
        result
    }
}

impl DialogIcon {
    #[cfg(target_os = "windows")]
    pub fn to_win(&self) -> winsafe::co::TD_ICON {
        match self {
            DialogIcon::Warning => winsafe::co::TD_ICON::WARNING,
            DialogIcon::Error => winsafe::co::TD_ICON::ERROR,
            DialogIcon::Information => winsafe::co::TD_ICON::INFORMATION,
        }
    }
    #[cfg(target_os = "macos")]
    pub fn to_native(&self) -> native_dialog::MessageType {
        match self {
            DialogIcon::Warning => native_dialog::MessageType::Warning,
            DialogIcon::Error => native_dialog::MessageType::Error,
            DialogIcon::Information => native_dialog::MessageType::Info,
        }
    }
}

#[cfg(target_os = "windows")]
impl DialogResult {
    pub fn from_win(dlg_id: winsafe::co::DLGID) -> DialogResult {
        match dlg_id {
            winsafe::co::DLGID::OK => DialogResult::Ok,
            winsafe::co::DLGID::CANCEL => DialogResult::Cancel,
            winsafe::co::DLGID::ABORT => DialogResult::Abort,
            winsafe::co::DLGID::RETRY => DialogResult::Retry,
            winsafe::co::DLGID::IGNORE => DialogResult::Ignore,
            winsafe::co::DLGID::YES => DialogResult::Yes,
            winsafe::co::DLGID::NO => DialogResult::No,
            winsafe::co::DLGID::TRYAGAIN => DialogResult::Tryagain,
            winsafe::co::DLGID::CONTINUE => DialogResult::Continue,
            _ => DialogResult::Unknown,
        }
    }
}
