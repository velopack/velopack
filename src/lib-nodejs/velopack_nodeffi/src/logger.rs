use std::sync::{Arc, Mutex};

use lazy_static::lazy_static;
use log::{Level, LevelFilter, Log, Metadata, Record};
use neon::{event::Channel, prelude::*};
use simplelog::{Config, SharedLogger};

lazy_static! {
    static ref LOGGER_CB: Arc<Mutex<Option<Root<JsFunction>>>> = Arc::new(Mutex::new(None));
    static ref LOGGER_CHANNEL: Arc<Mutex<Option<Channel>>> = Arc::new(Mutex::new(None));
}

struct LoggerImpl {}

impl SharedLogger for LoggerImpl {
    fn level(&self) -> LevelFilter {
        LevelFilter::max()
    }

    fn config(&self) -> Option<&Config> {
        None
    }

    fn as_log(self: Box<Self>) -> Box<dyn Log> {
        Box::new(*self)
    }
}

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
                    if let Ok(cb_lock) = LOGGER_CB.lock() {
                        if let Some(cb) = &*cb_lock {
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

pub fn create_shared_logger() -> Box<dyn SharedLogger> {
    Box::new(LoggerImpl {})
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
