#[cfg(windows)]
pub mod taskdialog;

#[cfg(target_os = "linux")]
pub mod gtkdialog;

#[cfg(target_os = "macos")]
pub mod cfdialog;

#[derive(Debug, Clone, Eq, PartialEq, Default)]
/// The icon to display in the dialog, or None for no icon.
pub enum XDialogIcon {
    /// No icon
    #[default]
    None = 0,
    /// Error icon
    Error,
    /// Warning icon
    Warning,
    /// Information icon
    Information,
}

#[derive(Debug, Clone, Eq, PartialEq, Default)]
/// Options for constructing a new custom message dialog
pub struct XDialogOptions {
    /// The title of the dialog window (required)
    pub title: String,
    /// The main instruction / header text. Can be set to an empty string to hide this element.
    pub main_instruction: String,
    /// The body text of the dialog. Can be set to an empty string to hide this element.
    pub message: String,
    /// The icon to display in the dialog, or None for no icon.
    pub icon: XDialogIcon,
    /// The buttons to display in the dialog. This can be an empty array to collapse the button panel.
    pub buttons: Vec<String>,
    /// Whether this dialog includes a progress bar. This is used by backends to determine whether to include progress-related UI elements.
    pub has_progress: bool,
}

#[derive(Debug, Clone, Eq, PartialEq)]
/// The result of a blocking dialog operation
pub enum XDialogResult {
    /// The dialog was closed without a button being pressed (eg. user clicked 'X' button)
    WindowClosed,
    /// The dialog was closed because the timeout elapsed
    TimeoutElapsed,
    /// The dialog was not shown because silent mode is currently enabled
    SilentMode,
    /// A button was pressed, with the index of the button in the `buttons` array
    ButtonPressed(usize),
}

#[allow(missing_docs)]
#[derive(thiserror::Error, Debug)]
pub enum XDialogError {
    #[error("xdialog backend not initialized")]
    NotInitialized,
    #[error("xdialog command returned no result: {0}")]
    NoResult(oneshot::RecvError),
    #[error("xdialog generic error: {0}")]
    SystemError(String),
}

/// Trait for managing dialog windows. Allows showing, closing, and updating progress dialogs.
pub trait DialogManager {
    /// Show a dialog with the given options.
    fn show(&mut self, options: XDialogOptions) -> Result<Box<dyn DialogProxy>, XDialogError>;
}

pub trait DialogProxy {
    /// Close this dialog.
    fn close(&self);
    /// Set the progress bar value (0.0 to 1.0) for this dialog.
    fn set_progress_value(&self, progress: f32);
    /// Set the progress bar value (0 to 100) for this dialog.
    fn set_progress_value_i16(&self, progress: i16);
    /// Set the progress body text for this dialog.
    fn set_progress_text(&self, text: &str);
    /// Set the progress bar to indeterminate (marquee) mode.
    fn set_progress_indeterminate(&self);
    /// Get a receiver for the result of this dialog (eg. which button was pressed, or if it was closed).
    fn get_result(&self, timeout: Option<std::time::Duration>) -> Result<XDialogResult, XDialogError>;
}
