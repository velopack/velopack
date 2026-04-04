#![allow(dead_code)]

use std::ptr::null_mut;
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::Mutex;
use std::time::Duration;

use widestring::U16CString;
use windows::core::{BOOL, HRESULT, PCWSTR};
use windows::Win32::Foundation::{FALSE, HMODULE, HWND, LPARAM, S_OK, TRUE, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Controls::{
    TaskDialogIndirect, TASKDIALOGCONFIG, TASKDIALOGCONFIG_0, TASKDIALOGCONFIG_1, TASKDIALOG_BUTTON, TASKDIALOG_COMMON_BUTTON_FLAGS,
    TASKDIALOG_FLAGS, TASKDIALOG_NOTIFICATIONS, TDE_CONTENT, TDE_EXPANDED_INFORMATION, TDE_FOOTER, TDE_MAIN_INSTRUCTION, TDF_CALLBACK_TIMER,
    TDF_SHOW_PROGRESS_BAR, TDF_SIZE_TO_CONTENT, TDM_SET_BUTTON_ELEVATION_REQUIRED_STATE, TDM_SET_ELEMENT_TEXT, TDM_SET_MARQUEE_PROGRESS_BAR,
    TDM_SET_PROGRESS_BAR_MARQUEE, TDM_SET_PROGRESS_BAR_POS, TDN_CREATED, TDN_DESTROYED, TDN_HYPERLINK_CLICKED, TDN_TIMER, TD_ERROR_ICON,
    TD_INFORMATION_ICON, TD_WARNING_ICON,
};
use windows::Win32::UI::WindowsAndMessaging::{EndDialog, SendMessageW, HICON};

use crate::backends::{DialogManager, DialogProxy, XDialogError, XDialogIcon, XDialogOptions, XDialogResult};

#[derive(Debug, PartialEq)]
enum DialogRequest {
    None,
    Close,
    SetProgress(f32),
    SetIndeterminate,
    SetText(String),
}

/// Manages Win32 Task Dialogs. Each `show()` call spawns a new dialog thread.
pub struct TaskDialogManager;

impl TaskDialogManager {
    pub fn new() -> Self {
        TaskDialogManager
    }
}

impl DialogManager for TaskDialogManager {
    fn show(&mut self, options: XDialogOptions) -> Result<Box<dyn DialogProxy>, XDialogError> {
        let has_progress = options.has_progress;
        let (command_tx, command_rx) = channel::<DialogRequest>();
        let (result_tx, result_rx) = oneshot::channel::<XDialogResult>();

        std::thread::spawn(move || {
            let mut config = TaskDialogConfig::new(command_rx);
            config.window_title = options.title;
            config.main_instruction = options.main_instruction;
            config.content = options.message;
            let mut default_button: Option<i32> = None;
            for (idx, text) in options.buttons.iter().enumerate().rev() {
                if default_button.is_none() {
                    default_button = Some(idx as i32);
                }
                let button = TaskDialogButton {
                    text: text.clone(),
                    id: idx as i32,
                };
                config.buttons.push(button);
            }
            config.default_button = default_button.unwrap_or(0);
            config.main_icon = convert_icon(options.icon);
            config.progress = if has_progress {
                ProgressState::Pos(0f32)
            } else {
                ProgressState::None
            };
            config.flags = TDF_SIZE_TO_CONTENT | TDF_CALLBACK_TIMER;
            if has_progress {
                config.flags |= TDF_SHOW_PROGRESS_BAR;
            }
            config.callback = Some(|hwnd, msg, _w_param, _l_param, ref_data| {
                if msg == TDN_TIMER {
                    let config = unsafe { &mut *ref_data };
                    loop {
                        let message = config.command_rx.try_recv().unwrap_or(DialogRequest::None);
                        match message {
                            DialogRequest::None => break,
                            DialogRequest::Close => unsafe {
                                let _ = EndDialog(hwnd, -1);
                            },
                            DialogRequest::SetProgress(val) => {
                                if config.progress == ProgressState::Indeterminate {
                                    config.set_progress_bar_marquee_on_off(false);
                                    config.set_progress_bar_marquee_progress(false);
                                }
                                config.set_progress_bar_pos((val * 100f32) as usize);
                                config.progress = ProgressState::Pos(val);
                            }
                            DialogRequest::SetIndeterminate => {
                                config.set_progress_bar_marquee_on_off(true);
                                config.set_progress_bar_marquee_progress(true);
                                config.progress = ProgressState::Indeterminate;
                            }
                            DialogRequest::SetText(text) => config.set_content(&text),
                        }
                    }
                }
                S_OK
            });

            let result = unsafe { execute_task_dialog(&mut config) };

            let xresult = match result {
                Ok(result) => {
                    if result.button_id < 0 {
                        XDialogResult::WindowClosed
                    } else {
                        XDialogResult::ButtonPressed(result.button_id as usize)
                    }
                }
                Err(_) => XDialogResult::WindowClosed,
            };

            let _ = result_tx.send(xresult);
        });

        Ok(Box::new(TaskDialogProxy {
            command_tx,
            state: Mutex::new(ProxyState::Pending(result_rx)),
        }))
    }
}

enum ProxyState {
    Pending(oneshot::Receiver<XDialogResult>),
    Resolved(XDialogResult),
}

struct TaskDialogProxy {
    command_tx: Sender<DialogRequest>,
    state: Mutex<ProxyState>,
}

impl TaskDialogProxy {
    fn resolve(&self, timeout: Option<Duration>) -> Result<XDialogResult, XDialogError> {
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        match &*state {
            ProxyState::Resolved(result) => Ok(result.clone()),
            ProxyState::Pending(_) => {
                // Take ownership of the receiver by swapping with a temporary value
                let ProxyState::Pending(rx) = std::mem::replace(&mut *state, ProxyState::Resolved(XDialogResult::WindowClosed)) else {
                    unreachable!()
                };
                let result = match timeout {
                    Some(d) => match rx.recv_timeout(d) {
                        Ok(r) => Ok(r),
                        Err(oneshot::RecvTimeoutError::Timeout) => Ok(XDialogResult::TimeoutElapsed),
                        Err(oneshot::RecvTimeoutError::Disconnected) => Err(XDialogError::NoResult(oneshot::RecvError)),
                    },
                    None => rx.recv().map_err(XDialogError::NoResult),
                };
                match &result {
                    Ok(r) => *state = ProxyState::Resolved(r.clone()),
                    Err(_) => *state = ProxyState::Resolved(XDialogResult::WindowClosed),
                }
                result
            }
        }
    }
}

impl DialogProxy for TaskDialogProxy {
    fn close(&self) {
        let _ = self.command_tx.send(DialogRequest::Close);
    }

    fn set_progress_value(&self, progress: f32) {
        let _ = self.command_tx.send(DialogRequest::SetProgress(progress));
    }

    fn set_progress_text(&self, text: &str) {
        let _ = self.command_tx.send(DialogRequest::SetText(text.to_string()));
    }

    fn set_progress_indeterminate(&self) {
        let _ = self.command_tx.send(DialogRequest::SetIndeterminate);
    }

    fn get_result(&self, timeout: Option<Duration>) -> Result<XDialogResult, XDialogError> {
        self.resolve(timeout)
    }

    fn set_progress_value_i16(&self, progress: i16) {
        let progress_f32 = (progress as f32) / 100f32;
        self.set_progress_value(progress_f32);
    }
}

impl Drop for TaskDialogProxy {
    fn drop(&mut self) {
        let _ = self.command_tx.send(DialogRequest::Close);
    }
}

type TaskDialogHyperlinkCallback = Option<fn(context: &str) -> ()>;

type TaskDialogWndProcCallback =
    Option<fn(hwnd: HWND, msg: TASKDIALOG_NOTIFICATIONS, w_param: WPARAM, l_param: LPARAM, ref_data: *mut TaskDialogConfig) -> HRESULT>;

fn convert_icon(icon: XDialogIcon) -> TASKDIALOGCONFIG_0 {
    match icon {
        XDialogIcon::None => TASKDIALOGCONFIG_0 {
            hMainIcon: HICON(null_mut()),
        },
        XDialogIcon::Error => TASKDIALOGCONFIG_0 { pszMainIcon: TD_ERROR_ICON },
        XDialogIcon::Warning => TASKDIALOGCONFIG_0 {
            pszMainIcon: TD_WARNING_ICON,
        },
        XDialogIcon::Information => TASKDIALOGCONFIG_0 {
            pszMainIcon: TD_INFORMATION_ICON,
        },
    }
}

#[derive(Debug, PartialEq)]
enum ProgressState {
    None,
    Indeterminate,
    Pos(f32),
}

struct TaskDialogConfig {
    pub parent: HWND,
    pub instance: HMODULE,
    pub flags: TASKDIALOG_FLAGS,
    pub common_buttons: TASKDIALOG_COMMON_BUTTON_FLAGS,
    pub window_title: String,
    pub main_instruction: String,
    pub content: String,
    pub verification_text: String,
    pub expanded_information: String,
    pub expanded_control_text: String,
    pub collapsed_control_text: String,
    pub footer: String,
    pub buttons: Vec<TaskDialogButton>,
    pub default_button: i32,
    pub radio_buttons: Vec<TaskDialogButton>,
    pub default_radio_buttons: i32,
    pub main_icon: TASKDIALOGCONFIG_0,
    pub footer_icon: TASKDIALOGCONFIG_1,
    pub dialog_hwnd: HWND,
    pub is_destroyed: bool,
    pub hyperlink_callback: TaskDialogHyperlinkCallback,
    pub callback: TaskDialogWndProcCallback,
    pub cx_width: u32,
    pub progress: ProgressState,
    pub command_rx: Receiver<DialogRequest>,
}

impl TaskDialogConfig {
    fn new(command_rx: Receiver<DialogRequest>) -> Self {
        TaskDialogConfig {
            parent: HWND(null_mut()),
            instance: HMODULE(null_mut()),
            flags: TASKDIALOG_FLAGS(0),
            common_buttons: TASKDIALOG_COMMON_BUTTON_FLAGS(0),
            window_title: "".to_string(),
            main_instruction: "".to_string(),
            content: "".to_string(),
            verification_text: "".to_string(),
            expanded_information: "".to_string(),
            expanded_control_text: "".to_string(),
            collapsed_control_text: "".to_string(),
            footer: "".to_string(),
            buttons: vec![],
            default_button: 0,
            radio_buttons: vec![],
            default_radio_buttons: 0,
            main_icon: TASKDIALOGCONFIG_0 {
                hMainIcon: HICON(null_mut()),
            },
            footer_icon: TASKDIALOGCONFIG_1 {
                hFooterIcon: HICON(null_mut()),
            },
            dialog_hwnd: HWND(null_mut()),
            is_destroyed: false,
            hyperlink_callback: None,
            callback: None,
            cx_width: 0,
            progress: ProgressState::None,
            command_rx,
        }
    }
}

impl TaskDialogConfig {
    pub fn set_progress_bar_marquee_on_off(&mut self, enable: bool) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        unsafe {
            let v = if enable {
                TRUE.0 as usize
            } else {
                FALSE.0 as usize
            };
            SendMessageW(self.dialog_hwnd, TDM_SET_PROGRESS_BAR_MARQUEE.0 as u32, Some(WPARAM(v)), Some(LPARAM(0)));
        }
    }

    pub fn set_progress_bar_marquee_progress(&mut self, enable: bool) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        unsafe {
            let v = if enable {
                TRUE.0 as usize
            } else {
                FALSE.0 as usize
            };
            SendMessageW(self.dialog_hwnd, TDM_SET_MARQUEE_PROGRESS_BAR.0 as u32, Some(WPARAM(v)), Some(LPARAM(0)));
        }
    }

    pub fn set_progress_bar_pos(&mut self, percentage: usize) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        unsafe {
            SendMessageW(
                self.dialog_hwnd,
                TDM_SET_PROGRESS_BAR_POS.0 as u32,
                Some(WPARAM(percentage)),
                Some(LPARAM(0)),
            );
        }
    }

    pub fn set_content(&mut self, content: &str) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        self.content = content.to_string();
        unsafe {
            let content_wchar = U16CString::from_str_unchecked(content);
            SendMessageW(
                self.dialog_hwnd,
                TDM_SET_ELEMENT_TEXT.0 as u32,
                Some(WPARAM(TDE_CONTENT.0 as usize)),
                Some(LPARAM(content_wchar.as_ptr() as isize)),
            );
        }
    }

    pub fn set_main_instruction(&mut self, main_instruction: &str) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        self.main_instruction = main_instruction.to_string();
        unsafe {
            let main_instruction_wchar = U16CString::from_str_unchecked(main_instruction);
            SendMessageW(
                self.dialog_hwnd,
                TDM_SET_ELEMENT_TEXT.0 as u32,
                Some(WPARAM(TDE_MAIN_INSTRUCTION.0 as usize)),
                Some(LPARAM(main_instruction_wchar.as_ptr() as isize)),
            );
        }
    }

    pub fn set_footer(&mut self, footer: &str) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        self.footer = footer.to_string();
        unsafe {
            let footer_wchar = U16CString::from_str_unchecked(footer);
            SendMessageW(
                self.dialog_hwnd,
                TDM_SET_ELEMENT_TEXT.0 as u32,
                Some(WPARAM(TDE_FOOTER.0 as usize)),
                Some(LPARAM(footer_wchar.as_ptr() as isize)),
            );
        }
    }

    pub fn set_expanded_information(&mut self, expanded_information: &str) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        self.expanded_information = expanded_information.to_string();
        unsafe {
            let expanded_information_wchar = U16CString::from_str_unchecked(expanded_information);
            SendMessageW(
                self.dialog_hwnd,
                TDM_SET_ELEMENT_TEXT.0 as u32,
                Some(WPARAM(TDE_EXPANDED_INFORMATION.0 as usize)),
                Some(LPARAM(expanded_information_wchar.as_ptr() as isize)),
            );
        }
    }

    pub fn set_button_elevation_required_state(&mut self, button_id: usize, enable: bool) {
        if self.dialog_hwnd.is_invalid() {
            return;
        }
        unsafe {
            SendMessageW(
                self.dialog_hwnd,
                TDM_SET_BUTTON_ELEVATION_REQUIRED_STATE.0 as u32,
                Some(WPARAM(button_id)),
                Some(LPARAM(if enable { 1 } else { 0 })),
            );
        }
    }
}

struct TaskDialogButton {
    pub id: i32,
    pub text: String,
}

struct TaskDialogResult {
    pub button_id: i32,
    pub radio_button_id: i32,
    pub checked: bool,
}

impl Default for TaskDialogResult {
    fn default() -> Self {
        TaskDialogResult {
            button_id: 0,
            radio_button_id: 0,
            checked: false,
        }
    }
}

unsafe fn execute_task_dialog(conf: &mut TaskDialogConfig) -> Result<TaskDialogResult, windows::core::Error> {
    let mut result = TaskDialogResult::default();
    let conf_ptr: *mut TaskDialogConfig = conf;
    let conf_long_ptr = conf_ptr as isize;

    let instance = if conf.instance.is_invalid() {
        GetModuleHandleW(PCWSTR(std::ptr::null()))?
    } else {
        conf.instance
    };

    let window_title: U16CString = U16CString::from_str_unchecked(&conf.window_title);
    let main_instruction: U16CString = U16CString::from_str_unchecked(&conf.main_instruction);
    let content: U16CString = U16CString::from_str_unchecked(&conf.content);
    let verification_text: U16CString = U16CString::from_str_unchecked(&conf.verification_text);
    let expanded_information: U16CString = U16CString::from_str_unchecked(&conf.expanded_information);
    let expanded_control_text: U16CString = U16CString::from_str_unchecked(&conf.expanded_control_text);
    let collapsed_control_text: U16CString = U16CString::from_str_unchecked(&conf.collapsed_control_text);
    let footer: U16CString = U16CString::from_str_unchecked(&conf.footer);

    let btn_text: Vec<U16CString> = conf.buttons.iter().map(|btn| U16CString::from_str_unchecked(&btn.text)).collect();
    let buttons: Vec<TASKDIALOG_BUTTON> = conf
        .buttons
        .iter()
        .enumerate()
        .map(|(i, btn)| TASKDIALOG_BUTTON {
            nButtonID: btn.id,
            pszButtonText: PCWSTR(btn_text[i].as_ptr()),
        })
        .collect();

    let radio_btn_text: Vec<U16CString> = conf.radio_buttons.iter().map(|btn| U16CString::from_str_unchecked(&btn.text)).collect();
    let radio_buttons: Vec<TASKDIALOG_BUTTON> = conf
        .radio_buttons
        .iter()
        .enumerate()
        .map(|(i, btn)| TASKDIALOG_BUTTON {
            nButtonID: btn.id,
            pszButtonText: PCWSTR(radio_btn_text[i].as_ptr()),
        })
        .collect();

    unsafe extern "system" fn callback(hwnd: HWND, msg: TASKDIALOG_NOTIFICATIONS, _w_param: WPARAM, _l_param: LPARAM, lp_ref_data: isize) -> HRESULT {
        let conf = std::ptr::with_exposed_provenance_mut::<TaskDialogConfig>(lp_ref_data as usize);
        match msg {
            TDN_CREATED => {
                (*conf).dialog_hwnd = hwnd;
            }
            TDN_DESTROYED => {
                (*conf).is_destroyed = true;
            }
            TDN_HYPERLINK_CLICKED => {
                let link = U16CString::from_ptr_str(_l_param.0 as *const u16).to_string().unwrap_or_default();
                if let Some(callback) = (*conf).hyperlink_callback {
                    callback(&link);
                }
            }
            _ => {}
        };
        if let Some(callback) = (*conf).callback {
            return callback(hwnd, msg, _w_param, _l_param, lp_ref_data as _);
        }
        S_OK
    }

    let config = TASKDIALOGCONFIG {
        cbSize: std::mem::size_of::<TASKDIALOGCONFIG>() as u32,
        hwndParent: conf.parent,
        hInstance: instance.into(),
        dwFlags: conf.flags,
        dwCommonButtons: conf.common_buttons,
        pszWindowTitle: PCWSTR(window_title.as_ptr()),
        pszMainInstruction: PCWSTR(main_instruction.as_ptr()),
        pszContent: PCWSTR(content.as_ptr()),
        pszVerificationText: PCWSTR(verification_text.as_ptr()),
        pszExpandedInformation: PCWSTR(expanded_information.as_ptr()),
        pszExpandedControlText: PCWSTR(expanded_control_text.as_ptr()),
        pszCollapsedControlText: PCWSTR(collapsed_control_text.as_ptr()),
        pszFooter: PCWSTR(footer.as_ptr()),
        cButtons: buttons.len() as u32,
        pButtons: buttons.as_slice().as_ptr(),
        nDefaultButton: conf.default_button,
        cRadioButtons: radio_buttons.len() as u32,
        pRadioButtons: radio_buttons.as_slice().as_ptr(),
        nDefaultRadioButton: conf.default_radio_buttons,
        Anonymous1: conf.main_icon,
        Anonymous2: conf.footer_icon,
        pfCallback: Some(callback),
        lpCallbackData: conf_long_ptr,
        cxWidth: conf.cx_width,
    };

    let mut verify: BOOL = FALSE;
    let dialog_result = TaskDialogIndirect(&config, Some(&mut result.button_id), Some(&mut result.radio_button_id), Some(&mut verify));
    result.checked = verify != BOOL(0);
    dialog_result?;
    Ok(result)
}
