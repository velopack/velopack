use locator::*;
use neon::prelude::*;
use semver::Version;
use std::cell::RefCell;
use std::rc::Rc;
use std::sync::{Arc, Mutex};
use std::thread;
use velopack::sources::*;
use velopack::*;

mod logger;

struct UpdateManagerWrapper {
    manager: UpdateManager,
}
impl Finalize for UpdateManagerWrapper {}
type BoxedUpdateManager = JsBox<RefCell<UpdateManagerWrapper>>;

fn args_get_locator(cx: &mut FunctionContext, i: usize) -> NeonResult<Option<VelopackLocatorConfig>> {
    let arg_locator = cx.argument_opt(i);
    if let Some(js_value) = arg_locator {
        if js_value.is_a::<JsString, _>(cx) {
            if let Ok(js_string) = js_value.downcast::<JsString, _>(cx) {
                let arg_locator = js_string.value(cx);
                if !arg_locator.is_empty() {
                    let locator = serde_json::from_str::<VelopackLocatorConfig>(&arg_locator).or_else(|e| cx.throw_error(e.to_string()))?;
                    return Ok(Some(locator));
                }
            }
        }
    }

    Ok(None)
}

fn args_array_to_vec_string(cx: &mut FunctionContext, arg: Handle<JsArray>) -> NeonResult<Vec<String>> {
    let mut vec: Vec<String> = Vec::new();
    for i in 0..arg.len(cx) {
        let arg: Handle<JsValue> = arg.get(cx, i)?;
        if let Ok(str) = arg.downcast::<JsString, _>(cx) {
            let str = str.value(cx);
            vec.push(str);
        } else {
            return cx.throw_type_error("arg must be an array of strings");
        }
    }
    Ok(vec)
}

fn js_new_update_manager(mut cx: FunctionContext) -> JsResult<BoxedUpdateManager> {
    let arg_source = cx.argument::<JsString>(0)?.value(&mut cx);
    let arg_options = cx.argument::<JsString>(1)?.value(&mut cx);

    let mut options: Option<UpdateOptions> = None;
    let locator = args_get_locator(&mut cx, 2)?;

    if !arg_options.is_empty() {
        let new_opt = serde_json::from_str::<UpdateOptions>(&arg_options).or_else(|e| cx.throw_error(e.to_string()))?;
        options = Some(new_opt);
    }

    let source = AutoSource::new(&arg_source);
    let manager = UpdateManager::new(source, options, locator).or_else(|e| cx.throw_error(e.to_string()))?;
    let wrapper = UpdateManagerWrapper { manager };
    Ok(cx.boxed(RefCell::new(wrapper)))
}

fn js_get_current_version(mut cx: FunctionContext) -> JsResult<JsString> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let version = mgr_ref.get_current_version_as_string();
    Ok(cx.string(version))
}

fn js_get_app_id(mut cx: FunctionContext) -> JsResult<JsString> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let id = mgr_ref.get_app_id();
    Ok(cx.string(id))
}

fn js_is_portable(mut cx: FunctionContext) -> JsResult<JsBoolean> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let is_portable = mgr_ref.get_is_portable();
    Ok(cx.boolean(is_portable))
}

fn js_update_pending_restart(mut cx: FunctionContext) -> JsResult<JsValue> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let pending_restart = mgr_ref.get_update_pending_restart();

    if let Some(asset) = pending_restart {
        let json = serde_json::to_string(&asset);
        match json {
            Ok(json) => Ok(cx.string(json).upcast()),
            Err(e) => cx.throw_error(e.to_string()),
        }
    } else {
        let nil = cx.null().upcast();
        Ok(nil)
    }
}

fn js_check_for_updates_async(mut cx: FunctionContext) -> JsResult<JsPromise> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let mgr_clone = mgr_ref.clone();
    let (deferred, promise) = cx.promise();
    let channel = cx.channel();

    thread::spawn(move || {
        let result = mgr_clone.check_for_updates();
        channel.send(move |mut cx| {
            match result {
                Ok(res) => {
                    if let UpdateCheck::UpdateAvailable(upd) = &res {
                        let json = serde_json::to_string(&upd);
                        if let Err(e) = &json {
                            let err = cx.error(e.to_string()).unwrap();
                            deferred.reject(&mut cx, err);
                        } else {
                            let val = cx.string(json.unwrap());
                            deferred.resolve(&mut cx, val);
                        }
                    } else {
                        let nil = cx.null();
                        deferred.resolve(&mut cx, nil);
                    }
                }
                Err(e) => {
                    let err = cx.error(e.to_string()).unwrap();
                    deferred.reject(&mut cx, err);
                }
            };
            Ok(())
        });
    });

    Ok(promise)
}

fn js_download_update_async(mut cx: FunctionContext) -> JsResult<JsPromise> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let mgr_clone = mgr_ref.clone();

    let arg_update = cx.argument::<JsString>(1)?.value(&mut cx);
    let callback_rc = cx.argument::<JsFunction>(2)?.root(&mut cx);
    let channel1 = cx.channel();
    let channel2 = cx.channel();

    let update_info = serde_json::from_str::<UpdateInfo>(&arg_update).or_else(|e| cx.throw_error(e.to_string()))?;
    let (deferred, promise) = cx.promise();
    let (sender, receiver) = std::sync::mpsc::channel::<i16>();

    // spawn a thread to handle the progress updates
    thread::spawn(move || {
        let callback_moved = Arc::new(Mutex::new(Some(callback_rc)));
        while let Ok(progress) = receiver.recv() {
            let callback_clone = callback_moved.clone();
            channel1.send(move |mut cx| {
                if let Ok(guard) = callback_clone.lock() {
                    if let Some(cb_s) = guard.as_ref() {
                        let callback_inner = cb_s.to_inner(&mut cx);
                        let this = cx.undefined();
                        let args = vec![cx.number(progress).upcast()];
                        callback_inner.call(&mut cx, this, args).unwrap();
                    }
                }
                Ok(())
            });
        }

        // dispose of the callback on the main JS thread
        channel1.send(move |mut cx| {
            if let Ok(mut cb_r) = callback_moved.lock() {
                let callback = cb_r.take();
                if let Some(cb_s) = callback {
                    cb_s.drop(&mut cx);
                }
            }
            Ok(())
        });
    });

    // spawn a thread to download the updates
    thread::spawn(move || match mgr_clone.download_updates(&update_info, Some(sender)) {
        Ok(_) => channel2.send(|mut cx| {
            let val = cx.undefined();
            deferred.resolve(&mut cx, val);
            Ok(())
        }),
        Err(e) => channel2.send(move |mut cx| {
            let err = cx.error(e.to_string()).unwrap();
            deferred.reject(&mut cx, err);
            Ok(())
        }),
    });

    Ok(promise)
}

fn js_wait_exit_then_apply_update(mut cx: FunctionContext) -> JsResult<JsUndefined> {
    let mgr_boxed = cx.argument::<BoxedUpdateManager>(0)?;
    let mgr_ref = &mgr_boxed.borrow().manager;
    let arg_update = cx.argument::<JsString>(1)?.value(&mut cx);
    let arg_silent = cx.argument::<JsBoolean>(2)?.value(&mut cx);
    let arg_restart = cx.argument::<JsBoolean>(3)?.value(&mut cx);

    let asset = serde_json::from_str::<VelopackAsset>(&arg_update).or_else(|e| cx.throw_error(e.to_string()))?;

    let arg_restart_args = cx.argument::<JsArray>(4)?;
    let restart_args = args_array_to_vec_string(&mut cx, arg_restart_args)?;

    mgr_ref.wait_exit_then_apply_updates(asset, arg_silent, arg_restart, restart_args).or_else(|e| cx.throw_error(e.to_string()))?;
    Ok(cx.undefined())
}

fn js_appbuilder_run(mut cx: FunctionContext) -> JsResult<JsUndefined> {
    let arg_cb = cx.argument::<JsFunction>(0)?;

    let arg_argarray = cx.argument::<JsValue>(1)?;
    let mut argarray: Option<Vec<String>> = None;

    if arg_argarray.is_a::<JsArray, _>(&mut cx) {
        if let Ok(str) = arg_argarray.downcast::<JsArray, _>(&mut cx) {
            argarray = Some(args_array_to_vec_string(&mut cx, str)?)
        } else {
            return cx.throw_type_error("arg must be an array of strings");
        }
    }

    let locator = args_get_locator(&mut cx, 2)?;
    
    let auto_apply = cx.argument::<JsBoolean>(3)?.value(&mut cx);

    let undefined = cx.undefined();
    let cx_ref = Rc::new(RefCell::new(cx));

    let hook_handler = move |hook_name: &str, current_version: Version| {
        let mut cx = cx_ref.borrow_mut();
        let hook_name = cx.string(hook_name.to_string());
        let current_version = cx.string(current_version.to_string());
        let args = vec![hook_name.upcast(), current_version.upcast()];
        let this = cx.undefined();
        arg_cb.call(&mut *cx, this, args).unwrap();
    };

    let mut builder = VelopackApp::build()
        .on_restarted(|semver| hook_handler("restarted", semver))
        .on_first_run(|semver| hook_handler("first-run", semver))
        .set_auto_apply_on_startup(auto_apply);

    #[cfg(target_os = "windows")]
    {
        builder = builder
            .on_after_install_fast_callback(|semver| hook_handler("after-install", semver))
            .on_before_uninstall_fast_callback(|semver| hook_handler("before-uninstall", semver))
            .on_before_update_fast_callback(|semver| hook_handler("before-update", semver))
            .on_after_update_fast_callback(|semver| hook_handler("after-update", semver));
    }

    if let Some(locator) = &locator {
        builder = builder.set_locator(locator.clone());
    }

    if let Some(arg_array) = argarray {
        builder = builder.set_args(arg_array);
    }

    // init logging
    let log_file = if let Some(locator) = &locator {
        logging::default_logfile_path(locator)
    } else {
        logging::default_logfile_path(LocationContext::FromCurrentExe)
    };

    logging::init_logging("lib-nodejs", Some(&log_file), false, false, Some(logger::create_shared_logger()));
    builder.run();

    Ok(undefined)
}

fn js_set_logger_callback(mut cx: FunctionContext) -> JsResult<JsUndefined> {
    let arg_cb = cx.argument::<JsFunction>(0)?;
    let cb_root = arg_cb.root(&mut cx);
    logger::set_logger_callback(Some(cb_root), &mut cx);
    Ok(cx.undefined())
}

#[neon::main]
fn main(mut cx: ModuleContext) -> NeonResult<()> {
    cx.export_function("js_new_update_manager", js_new_update_manager)?;
    cx.export_function("js_get_current_version", js_get_current_version)?;
    cx.export_function("js_get_app_id", js_get_app_id)?;
    cx.export_function("js_is_portable", js_is_portable)?;
    cx.export_function("js_update_pending_restart", js_update_pending_restart)?;
    cx.export_function("js_check_for_updates_async", js_check_for_updates_async)?;
    cx.export_function("js_download_update_async", js_download_update_async)?;
    cx.export_function("js_wait_exit_then_apply_update", js_wait_exit_then_apply_update)?;
    cx.export_function("js_appbuilder_run", js_appbuilder_run)?;
    cx.export_function("js_set_logger_callback", js_set_logger_callback)?;
    Ok(())
}
