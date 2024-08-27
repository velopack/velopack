use std::sync::{Arc, Mutex};

use lazy_static::lazy_static;
use log::{Level, Log, Metadata, Record};
use neon::{event::Channel, prelude::*};

static LOGGER: LoggerImpl = LoggerImpl {};

lazy_static! {
    static ref LOGGER_CB: Arc<Mutex<Option<Root<JsFunction>>>> = Arc::new(Mutex::new(None));
    static ref LOGGER_CONSOLE_LOG: Arc<Mutex<Option<Root<JsFunction>>>> = Arc::new(Mutex::new(None));
    static ref LOGGER_CHANNEL: Arc<Mutex<Option<Channel>>> = Arc::new(Mutex::new(None));
}

struct LoggerImpl {}

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
        };

        if let Ok(channel_opt) = LOGGER_CHANNEL.lock() {
            if channel_opt.is_some() {
                let channel = channel_opt.as_ref().unwrap();

                channel.send(move |mut cx| {
                    // If custom callback exists, then use that.
                    if let Ok(cb_lock) = LOGGER_CB.lock() {
                        if let Some(cb) = &*cb_lock {
                            let undefined = cx.undefined();
                            let args = vec![cx.string(level).upcast(), cx.string(text).upcast()];
                            cb.to_inner(&mut cx).call(&mut cx, undefined, args)?;
                            return Ok(());
                        }
                    }

                    // If no custom callback, then use console.log
                    if let Ok(console) = LOGGER_CONSOLE_LOG.lock() {
                        if let Some(cb) = &*console {
                            let undefined = cx.undefined();
                            let args = vec![cx.string(level).upcast(), cx.string(text).upcast()];
                            cb.to_inner(&mut cx).call(&mut cx, undefined, args)?;
                            return Ok(());
                        }
                    }
                    Ok(())
                });
            }
        }
    }

    fn flush(&self) {}
}

pub fn init_logger(cx: &mut ModuleContext) {
    let _ = log::set_logger(&LOGGER);
    log::set_max_level(log::LevelFilter::Trace);

    if let Ok(mut ch) = LOGGER_CONSOLE_LOG.lock() {
        if let Ok(console) = cx.global::<JsObject>("console") {
            if let Ok(log_fn) = console.get::<JsFunction, ModuleContext, &str>(cx, "log") {
                *ch = Some(log_fn.root(cx));
            }
        }
    }

    if let Ok(mut ch) = LOGGER_CHANNEL.lock() {
        // let mut log_channel = Channel::new(cx);
        let mut log_channel = cx.channel();
        log_channel.unref(cx); // Unref the channel so that the event loop can exit while this channel is open
        *ch = Some(log_channel);
    }
}

pub fn set_logger_callback(callback: Option<Root<JsFunction>>, cx: &mut FunctionContext) {
    if let Ok(mut cb_lock) = LOGGER_CB.lock() {
        let cb_taken = cb_lock.take();
        if let Some(cb_exist) = cb_taken {
            cb_exist.drop(cx);
        }
        *cb_lock = callback;
    }
}
