#[derive(PartialEq, Clone, Copy, Debug)]
pub enum DialogResult {
    Ok,
    Cancel,
    Retry,
    Continue,
}

pub const MSG_CLOSE: i16 = -1;
pub const MSG_INDEFINITE: i16 = -2;
