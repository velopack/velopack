use anyhow::Result;
use std::sync::RwLock;
use velopack::locator::VelopackLocatorConfig;

use crate::types::*;

#[derive(Debug, Default, Clone)]
pub struct AppOptions {
    pub install_hook: vpkc_hook_callback_t,
    pub update_hook: vpkc_hook_callback_t,
    pub obsolete_hook: vpkc_hook_callback_t,
    pub uninstall_hook: vpkc_hook_callback_t,
    pub firstrun_hook: vpkc_hook_callback_t,
    pub restarted_hook: vpkc_hook_callback_t,
    pub auto_apply: Option<bool>,
    pub args: Option<Vec<String>>,
    pub locator: Option<VelopackLocatorConfig>,
}

lazy_static::lazy_static! {
    static ref LAST_ERROR: RwLock<String> = RwLock::new(String::new());
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
            log::error!("{:?}", e);
            false
        }
    }
}
