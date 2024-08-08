use neon::prelude::*;
use velopack::*;
use velopack::sources::*;


struct UpdateManagerWrapper<'a> {
    manager: UpdateManager<'a>,
}

impl<'a> Finalize for UpdateManagerWrapper<'a> {}


fn get_js_options(mut cx: FunctionContext, obj: &Handle<JsObject>) -> JsResult<UpdateOptions> {
    let allow_downgrade = obj.get(&mut cx, "allowDowngrade")?;

}

fn js_new_from_http_source(mut cx: FunctionContext) -> JsResult<JsBox<UpdateManagerWrapper>> {
    let url = cx.argument::<JsString>(0)?.value(&mut cx);

    let options: Option<UpdateOptions> = None;


    let obj = cx.argument::<JsObject>(1)?;

    





    let source = HttpSource::new(&url);
    let um = UpdateManager::new(source, options).map_err(|e| cx.throw_error(e.to_string()))?;

    let wrapper = UpdateManagerWrapper { manager: um };


    Ok(cx.boxed(wrapper))
}

fn hello(mut cx: FunctionContext) -> JsResult<JsString> {
    Ok(cx.string("hello node"))
}

#[neon::main]
fn main(mut cx: ModuleContext) -> NeonResult<()> {
    cx.export_function("hello", hello)?;
    Ok(())
}
