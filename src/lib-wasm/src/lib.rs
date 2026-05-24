#[macro_use]
mod macros;

pub mod constants;
pub mod errors;
pub mod types;
pub mod misc;
pub mod bundle;
pub mod locator;
pub mod download;
pub mod sources;
pub mod manager;
pub mod app;

wit_bindgen::generate!({
    world: "velopack",
    path: "wit/world.wit",
});

use std::cell::RefCell;
use std::collections::HashMap;

thread_local! {
    static MANAGERS: RefCell<HashMap<String, manager::UpdateManager>> = RefCell::new(HashMap::new());
}

fn with_manager<F, R>(token: &str, f: F) -> Result<R, String>
where
    F: FnOnce(&manager::UpdateManager) -> Result<R, errors::Error>,
{
    MANAGERS.with(|m| {
        let map = m.borrow();
        let mgr = map.get(token).ok_or_else(|| format!("Invalid manager token: {}", token))?;
        f(mgr).map_err(|e| e.to_string())
    })
}

struct VelopackComponent;

impl Guest for VelopackComponent {
    fn create_update_manager(
        source_url: String,
        options_json: Option<String>,
        locator_json: Option<String>,
    ) -> Result<String, String> {
        let options: Option<types::UpdateOptions> = match options_json {
            Some(ref s) if !s.is_empty() => Some(serde_json::from_str(s).map_err(|e| e.to_string())?),
            _ => None,
        };
        let locator: Option<types::VelopackLocatorConfig> = match locator_json {
            Some(ref s) if !s.is_empty() => Some(serde_json::from_str(s).map_err(|e| e.to_string())?),
            _ => None,
        };

        let source = sources::AutoSource::new(&source_url);
        let mgr = manager::UpdateManager::new(
            source,
            options,
            locator,
        ).map_err(|e| e.to_string())?;

        let token = misc::random_string(16);
        MANAGERS.with(|m| {
            m.borrow_mut().insert(token.clone(), mgr);
        });
        Ok(token)
    }

    fn get_current_version(manager_token: String) -> Result<String, String> {
        with_manager(&manager_token, |mgr| Ok(mgr.get_current_version_as_string()))
    }

    fn get_app_id(manager_token: String) -> Result<String, String> {
        with_manager(&manager_token, |mgr| Ok(mgr.get_app_id()))
    }

    fn get_is_portable(manager_token: String) -> Result<bool, String> {
        with_manager(&manager_token, |mgr| Ok(mgr.get_is_portable()))
    }

    fn get_update_pending_restart(manager_token: String) -> Result<Option<String>, String> {
        with_manager(&manager_token, |mgr| {
            match mgr.get_update_pending_restart() {
                Some(asset) => {
                    let json = serde_json::to_string(&asset)?;
                    Ok(Some(json))
                }
                None => Ok(None),
            }
        })
    }

    fn check_for_updates(manager_token: String) -> Result<Option<String>, String> {
        wstd::runtime::block_on(async move {
            let ptr = MANAGERS.with(|m| {
                let map = m.borrow();
                let mgr = map.get(&manager_token)
                    .ok_or_else(|| format!("Invalid manager token: {}", manager_token))?;
                Ok::<_, String>(mgr as *const manager::UpdateManager)
            })?;
            let mgr = unsafe { &*ptr };
            match mgr.check_for_updates().await {
                Ok(types::UpdateCheck::UpdateAvailable(info)) => {
                    let json = serde_json::to_string(&info).map_err(|e| e.to_string())?;
                    Ok(Some(json))
                }
                Ok(_) => Ok(None),
                Err(e) => Err(e.to_string()),
            }
        })
    }

    fn download_updates(
        manager_token: String,
        update_json: String,
    ) -> Result<(), String> {
        let update: types::UpdateInfo = serde_json::from_str(&update_json).map_err(|e| e.to_string())?;
        wstd::runtime::block_on(async move {
            let ptr = MANAGERS.with(|m| {
                let map = m.borrow();
                let mgr = map.get(&manager_token)
                    .ok_or_else(|| format!("Invalid manager token: {}", manager_token))?;
                Ok::<_, String>(mgr as *const manager::UpdateManager)
            })?;
            let mgr = unsafe { &*ptr };
            mgr.download_updates(&update, &|p| {
                crate::velopack::core::progress::report(p as u32);
            }).await.map_err(|e| e.to_string())
        })
    }

    fn wait_exit_then_apply_update(
        manager_token: String,
        asset_json: String,
        silent: bool,
        restart: bool,
        restart_args: Vec<String>,
    ) -> Result<(), String> {
        let asset: types::VelopackAsset = serde_json::from_str(&asset_json).map_err(|e| e.to_string())?;
        with_manager(&manager_token, |mgr| {
            mgr.wait_exit_then_apply_updates(&asset, silent, restart, restart_args.clone())
        })
    }

    fn app_run(
        args: Vec<String>,
        locator_json: Option<String>,
        auto_apply: bool,
    ) -> Result<Option<String>, String> {
        let locator: Option<types::VelopackLocatorConfig> = match locator_json {
            Some(ref s) if !s.is_empty() => Some(serde_json::from_str(s).map_err(|e| e.to_string())?),
            _ => None,
        };

        let result = app::app_run(&args, locator, auto_apply).map_err(|e| e.to_string())?;

        // Serialize the result as JSON: { hook: [name, version] | null, firstRun: version | null, restarted: version | null }
        let json = serde_json::json!({
            "hook": result.hook,
            "firstRun": result.first_run,
            "restarted": result.restarted,
        });
        Ok(Some(json.to_string()))
    }
}

export!(VelopackComponent);
