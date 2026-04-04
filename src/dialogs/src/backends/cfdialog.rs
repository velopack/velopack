use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{channel, Sender, TryRecvError};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use core_foundation::base::{kCFAllocatorDefault, CFRelease, TCFType};
use core_foundation::string::{CFString, CFStringRef};
use std::ffi::c_void;

use crate::backends::{DialogManager, DialogProxy, XDialogError, XDialogIcon, XDialogOptions, XDialogResult};

// --- FFI types and bindings for CFUserNotification ---

type CFUserNotificationRef = *mut c_void;
type CFOptionFlags = usize;
type CFDictionaryRef = *const c_void;
type CFAllocatorRef = *const c_void;

const CF_USER_NOTIFICATION_STOP_ALERT_LEVEL: CFOptionFlags = 0;
const CF_USER_NOTIFICATION_NOTE_ALERT_LEVEL: CFOptionFlags = 1;
const CF_USER_NOTIFICATION_CAUTION_ALERT_LEVEL: CFOptionFlags = 2;
const CF_USER_NOTIFICATION_PLAIN_ALERT_LEVEL: CFOptionFlags = 3;
const CF_USER_NOTIFICATION_NO_DEFAULT_BUTTON: CFOptionFlags = 1 << 5;

extern "C" {
    static kCFUserNotificationAlertHeaderKey: CFStringRef;
    static kCFUserNotificationAlertMessageKey: CFStringRef;
    static kCFUserNotificationDefaultButtonTitleKey: CFStringRef;
    static kCFUserNotificationAlternateButtonTitleKey: CFStringRef;
    static kCFUserNotificationOtherButtonTitleKey: CFStringRef;

    fn CFUserNotificationCreate(
        allocator: CFAllocatorRef,
        timeout: f64,
        flags: CFOptionFlags,
        error: *mut i32,
        dictionary: CFDictionaryRef,
    ) -> CFUserNotificationRef;

    fn CFUserNotificationUpdate(user_notification: CFUserNotificationRef, timeout: f64, flags: CFOptionFlags, dictionary: CFDictionaryRef) -> i32;

    fn CFUserNotificationReceiveResponse(user_notification: CFUserNotificationRef, timeout: f64, response_flags: *mut CFOptionFlags) -> i32;

    fn CFUserNotificationCancel(user_notification: CFUserNotificationRef) -> i32;

    fn CFDictionaryCreate(
        allocator: CFAllocatorRef,
        keys: *const *const c_void,
        values: *const *const c_void,
        num_values: isize,
        key_callbacks: *const c_void,
        value_callbacks: *const c_void,
    ) -> CFDictionaryRef;

    static kCFTypeDictionaryKeyCallBacks: c_void;
    static kCFTypeDictionaryValueCallBacks: c_void;
}

// --- Progress text rendering ---

const BAR_WIDTH: usize = 10;
const BOUNCE_SIZE: usize = 3;

/// Determinate progress bar, e.g. "●●●●●○○○○○"
fn progress_text(pct: i32) -> String {
    let filled = (pct as usize * BAR_WIDTH) / 100;
    let empty = BAR_WIDTH - filled;
    format!("{}{}", "●".repeat(filled), "○".repeat(empty))
}

/// Indeterminate bouncing indicator, e.g. "○○○●●●○○○○"
fn indeterminate_text(frame: usize) -> String {
    let max_pos = BAR_WIDTH - BOUNCE_SIZE;
    let cycle = max_pos * 2;
    let raw = frame % cycle;
    let pos = if raw <= max_pos {
        raw
    } else {
        cycle - raw
    };

    let mut bar = String::new();
    for i in 0..BAR_WIDTH {
        if i >= pos && i < pos + BOUNCE_SIZE {
            bar.push('●');
        } else {
            bar.push('○');
        }
    }
    bar
}

// --- Dictionary builder ---

fn build_dict(header: &str, message: &str, buttons: &[String], icon_flags: CFOptionFlags) -> (CFDictionaryRef, CFOptionFlags) {
    let header_key = unsafe { CFString::wrap_under_get_rule(kCFUserNotificationAlertHeaderKey) };
    let message_key = unsafe { CFString::wrap_under_get_rule(kCFUserNotificationAlertMessageKey) };

    let header_val = CFString::new(header);
    let message_val = CFString::new(message);

    let mut keys: Vec<*const c_void> = vec![header_key.as_CFTypeRef(), message_key.as_CFTypeRef()];
    let mut values: Vec<*const c_void> = vec![header_val.as_CFTypeRef(), message_val.as_CFTypeRef()];

    // We need to keep CFString values alive until after CFDictionaryCreate
    let mut _btn_vals: Vec<CFString> = Vec::new();
    let mut _btn_keys: Vec<CFString> = Vec::new();
    let mut flags = icon_flags;

    let button_key_refs: [CFStringRef; 3] = unsafe {
        [
            kCFUserNotificationDefaultButtonTitleKey,
            kCFUserNotificationAlternateButtonTitleKey,
            kCFUserNotificationOtherButtonTitleKey,
        ]
    };

    if buttons.is_empty() {
        flags |= CF_USER_NOTIFICATION_NO_DEFAULT_BUTTON;
    }

    for (i, text) in buttons.iter().enumerate().take(3) {
        let key = unsafe { CFString::wrap_under_get_rule(button_key_refs[i]) };
        let val = CFString::new(text);
        keys.push(key.as_CFTypeRef());
        values.push(val.as_CFTypeRef());
        _btn_keys.push(key);
        _btn_vals.push(val);
    }

    let dict = unsafe {
        CFDictionaryCreate(
            kCFAllocatorDefault as CFAllocatorRef,
            keys.as_ptr(),
            values.as_ptr(),
            keys.len() as isize,
            &kCFTypeDictionaryKeyCallBacks as *const c_void,
            &kCFTypeDictionaryValueCallBacks as *const c_void,
        )
    };

    (dict, flags)
}

fn icon_to_alert_level(icon: &XDialogIcon) -> CFOptionFlags {
    match icon {
        XDialogIcon::Error => CF_USER_NOTIFICATION_STOP_ALERT_LEVEL,
        XDialogIcon::Information => CF_USER_NOTIFICATION_NOTE_ALERT_LEVEL,
        XDialogIcon::Warning => CF_USER_NOTIFICATION_CAUTION_ALERT_LEVEL,
        XDialogIcon::None => CF_USER_NOTIFICATION_PLAIN_ALERT_LEVEL,
    }
}

// --- Dialog request enum ---

#[derive(Debug, PartialEq)]
enum DialogRequest {
    Close,
    SetProgress(f32),
    SetIndeterminate,
    SetText(String),
}

// --- CfDialogManager ---

/// Manages macOS CFUserNotification dialogs. Each `show()` spawns a dedicated thread.
pub struct CfDialogManager;

impl CfDialogManager {
    pub fn new() -> Self {
        CfDialogManager
    }
}

impl DialogManager for CfDialogManager {
    fn show(&mut self, options: XDialogOptions) -> Result<Box<dyn DialogProxy>, XDialogError> {
        let (command_tx, command_rx) = channel::<DialogRequest>();
        let (result_tx, result_rx) = oneshot::channel::<XDialogResult>();

        std::thread::spawn(move || {
            let alert_level = icon_to_alert_level(&options.icon);
            let has_progress = options.has_progress;

            let header = if options.main_instruction.is_empty() {
                &options.title
            } else {
                &options.main_instruction
            };

            // Build initial message body
            let initial_body = if has_progress {
                if options.message.is_empty() {
                    indeterminate_text(0)
                } else {
                    format!("{}\n\n{}", options.message, indeterminate_text(0))
                }
            } else {
                options.message.clone()
            };

            let (dict, flags) = build_dict(header, &initial_body, &options.buttons, alert_level);
            let mut error: i32 = 0;

            let notification = unsafe { CFUserNotificationCreate(kCFAllocatorDefault as CFAllocatorRef, 0.0, flags, &mut error, dict) };
            unsafe { CFRelease(dict as *const c_void) };

            if notification.is_null() || error != 0 {
                error!("Failed to create CFUserNotification (error: {})", error);
                let _ = result_tx.send(XDialogResult::WindowClosed);
                return;
            }

            // Spawn response-waiting thread
            let notif_ptr = notification as usize;
            let dismissed = Arc::new(AtomicBool::new(false));
            let dismissed2 = dismissed.clone();
            let response_flag = Arc::new(Mutex::new(0usize));
            let response_flag2 = response_flag.clone();

            std::thread::spawn(move || {
                let notif = notif_ptr as CFUserNotificationRef;
                let mut response: CFOptionFlags = 0;
                unsafe {
                    CFUserNotificationReceiveResponse(notif, 0.0, &mut response);
                }
                *response_flag2.lock().unwrap_or_else(|e| e.into_inner()) = response;
                dismissed2.store(true, Ordering::SeqCst);
            });

            // Update loop: poll commands and animate progress
            let mut frame: usize = 0;
            let mut is_indeterminate = has_progress;
            let mut current_progress: f32 = 0.0;
            let mut current_text = options.message.clone();

            loop {
                std::thread::sleep(Duration::from_millis(200));

                if dismissed.load(Ordering::SeqCst) {
                    break;
                }

                // Drain all pending commands
                let mut should_close = false;
                loop {
                    match command_rx.try_recv() {
                        Ok(DialogRequest::Close) => {
                            should_close = true;
                            break;
                        }
                        Ok(DialogRequest::SetProgress(val)) => {
                            current_progress = val;
                            is_indeterminate = false;
                        }
                        Ok(DialogRequest::SetIndeterminate) => {
                            is_indeterminate = true;
                        }
                        Ok(DialogRequest::SetText(text)) => {
                            current_text = text;
                        }
                        Err(TryRecvError::Empty) => break,
                        Err(TryRecvError::Disconnected) => {
                            should_close = true;
                            break;
                        }
                    }
                }

                if should_close {
                    unsafe { CFUserNotificationCancel(notification) };
                    // Wait for response thread to finish
                    while !dismissed.load(Ordering::SeqCst) {
                        std::thread::sleep(Duration::from_millis(50));
                    }
                    break;
                }

                // Update dialog if progress is active
                if has_progress {
                    frame += 1;
                    let bar = if is_indeterminate {
                        indeterminate_text(frame)
                    } else {
                        progress_text((current_progress * 100.0) as i32)
                    };

                    let body = if current_text.is_empty() {
                        bar
                    } else {
                        format!("{}\n\n{}", current_text, bar)
                    };

                    let (dict, update_flags) = build_dict(header, &body, &options.buttons, alert_level);
                    unsafe {
                        CFUserNotificationUpdate(notification, 0.0, update_flags, dict);
                        CFRelease(dict as *const c_void);
                    }
                }
            }

            // Map response to XDialogResult
            let response = *response_flag.lock().unwrap_or_else(|e| e.into_inner());
            let response_button = response & 0x3;
            let xresult = match response_button {
                0 if !options.buttons.is_empty() => XDialogResult::ButtonPressed(0),
                1 if options.buttons.len() > 1 => XDialogResult::ButtonPressed(1),
                3 if options.buttons.len() > 2 => XDialogResult::ButtonPressed(2),
                _ => XDialogResult::WindowClosed,
            };

            unsafe { CFRelease(notification) };
            let _ = result_tx.send(xresult);
        });

        Ok(Box::new(CfDialogProxy {
            command_tx,
            state: Mutex::new(ProxyState::Pending(result_rx)),
        }))
    }
}

// --- Proxy implementation (mirrors TaskDialogProxy / GtkDialogProxy) ---

enum ProxyState {
    Pending(oneshot::Receiver<XDialogResult>),
    Resolved(XDialogResult),
}

struct CfDialogProxy {
    command_tx: Sender<DialogRequest>,
    state: Mutex<ProxyState>,
}

impl CfDialogProxy {
    fn resolve(&self, timeout: Option<Duration>) -> Result<XDialogResult, XDialogError> {
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        match &*state {
            ProxyState::Resolved(result) => Ok(result.clone()),
            ProxyState::Pending(_) => {
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

impl DialogProxy for CfDialogProxy {
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

impl Drop for CfDialogProxy {
    fn drop(&mut self) {
        let _ = self.command_tx.send(DialogRequest::Close);
    }
}
