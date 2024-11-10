use anyhow::Result;
use log::{Level, Log, Metadata, Record};
use std::ffi::{c_void, CString};
use std::sync::{Mutex, RwLock};
use velopack::locator::VelopackLocatorConfig;

use crate::types::*;

#[derive(Debug, Default, Clone)]
pub struct AppOptions {
    pub install_hook: Option<vpkc_hook_callback_t>,
    pub update_hook: Option<vpkc_hook_callback_t>,
    pub obsolete_hook: Option<vpkc_hook_callback_t>,
    pub uninstall_hook: Option<vpkc_hook_callback_t>,
    pub firstrun_hook: Option<vpkc_hook_callback_t>,
    pub restarted_hook: Option<vpkc_hook_callback_t>,
    pub auto_apply: Option<bool>,
    pub args: Option<Vec<String>>,
    pub locator: Option<VelopackLocatorConfig>,
}

lazy_static::lazy_static! {
    static ref LAST_ERROR: RwLock<String> = RwLock::new(String::new());
    static ref LOG_CALLBACK: Mutex<Option<(vpkc_log_callback_t, usize)>> = Mutex::new(None);
    pub static ref VELOPACK_APP: RwLock<AppOptions> = RwLock::new(Default::default());
}

pub fn update_app_options<F>(op: F)
where
    F: FnOnce(&mut AppOptions),
{
    let mut app_options = VELOPACK_APP.write().unwrap();
    op(&mut app_options);
}

pub fn clear_last_error() {
    let mut last_error = LAST_ERROR.write().unwrap();
    last_error.clear();
}

pub fn get_last_error() -> String {
    let last_error = LAST_ERROR.read().unwrap();
    last_error.clone()
}

pub fn set_last_error(message: &str) {
    let mut last_error = LAST_ERROR.write().unwrap();
    *last_error = message.to_string();
}

pub fn wrap_error<F>(op: F) -> bool
where
    F: FnOnce() -> Result<()>,
{
    let mut last_error = LAST_ERROR.write().unwrap();
    last_error.clear();

    match op() {
        Ok(_) => true,
        Err(e) => {
            *last_error = format!("{:?}", e);
            false
        }
    }
}

pub fn set_log_callback(callback: vpkc_log_callback_t, user_data: *mut c_void) {
    // Initialize the logger if it hasn't been set yet
    let _ = log::set_logger(&LOGGER);
    log::set_max_level(log::LevelFilter::Trace);

    let mut log_callback = LOG_CALLBACK.lock().unwrap();
    *log_callback = Some((callback, user_data as usize));
}

pub fn clear_log_callback() {
    let mut log_callback = LOG_CALLBACK.lock().unwrap();
    *log_callback = None;
}

pub fn log_message(level: &str, message: &str) {
    let log_callback = LOG_CALLBACK.lock().unwrap();
    if let Some((callback, user_data)) = *log_callback {
        let c_level = CString::new(level).unwrap();
        let c_message = CString::new(message).unwrap();
        callback(user_data as *mut c_void, c_level.as_ptr(), c_message.as_ptr());
    }
}

struct LoggerImpl {}

static LOGGER: LoggerImpl = LoggerImpl {};

impl Log for LoggerImpl {
    fn enabled(&self, metadata: &Metadata) -> bool {
        metadata.level() <= log::max_level()
    }

    fn log(&self, record: &Record) {
        if !self.enabled(record.metadata()) {
            return;
        }

        let text = format!("{}", record.args());

        let level = match record.level() {
            Level::Error => "error",
            Level::Warn => "warn",
            Level::Info => "info",
            Level::Debug => "debug",
            Level::Trace => "trace",
        }
        .to_string();

        log_message(&level, &text);
    }

    fn flush(&self) {}
}
