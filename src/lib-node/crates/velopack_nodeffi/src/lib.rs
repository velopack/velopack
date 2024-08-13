use neon::prelude::*;
use std::cell::RefCell;
use std::thread;
use velopack::sources::*;
use velopack::*;

struct UpdateManagerWrapper {
    manager: UpdateManager,
}
impl Finalize for UpdateManagerWrapper {}
type BoxedUpdateManager = JsBox<RefCell<UpdateManagerWrapper>>;

fn js_new_update_manager(mut cx: FunctionContext) -> JsResult<BoxedUpdateManager> {
    let arg_source = cx.argument::<JsString>(0)?.value(&mut cx);
    let arg_options = cx.argument::<JsString>(1)?.value(&mut cx);

    let mut options: Option<UpdateOptions> = None;

    if !arg_options.is_empty() {
        let new_opt = serde_json::from_str::<UpdateOptions>(&arg_options).or_else(|e| cx.throw_error(e.to_string()))?;
        options = Some(new_opt);
    }
    
    let source = AutoSource::new(&arg_source);
    let manager = UpdateManager::new(source, options).or_else(|e| cx.throw_error(e.to_string()))?;
    let wrapper = UpdateManagerWrapper { manager };
    Ok(cx.boxed(RefCell::new(wrapper)))
}

fn js_get_current_version(mut cx: FunctionContext) -> JsResult<JsString> {
    let this = cx.this::<BoxedUpdateManager>()?;
    let version = this.borrow().manager.current_version().or_else(|e| cx.throw_error(e.to_string()))?;
    Ok(cx.string(version))
}

fn js_check_for_updates_async(mut cx: FunctionContext) -> JsResult<JsPromise> {
    let this = cx.this::<BoxedUpdateManager>()?;
    let (deferred, promise) = cx.promise();
    let channel = cx.channel();
    let manager = this.borrow().manager.clone();

    thread::spawn(move || {
        let result = manager.check_for_updates();
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

#[neon::main]
fn main(mut cx: ModuleContext) -> NeonResult<()> {
    cx.export_function("js_new_update_manager", js_new_update_manager)?;
    cx.export_function("js_get_current_version", js_get_current_version)?;
    // cx.export_function("js_get_app_id", js_get_app_id)?;
    // cx.export_function("js_is_portable", js_is_portable)?;
    // cx.export_function("js_is_installed", js_is_installed)?;
    // cx.export_function("js_is_update_pending_restart", js_is_update_pending_restart)?;
    cx.export_function("js_check_for_updates_async", js_check_for_updates_async)?;
    // cx.export_function("js_download_update_async", js_download_update_async)?;
    // cx.export_function("js_wait_then_apply_update_async", js_wait_then_apply_update_async)?;
    Ok(())
}
