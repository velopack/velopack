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
            if let Some(channel) = channel_opt.as_ref() {
                // try_send fails (rather than panicking like send) once Node is shutting down.
                let _ = channel.try_send(move |mut cx| {
                    // Release the lock before calling into JS, so the callback can call setLogger again.
                    let cb = LOGGER_CB.lock().ok().and_then(|cb_lock| cb_lock.as_ref().map(|cb| cb.to_inner(&mut cx)));
                    if let Some(cb) = cb {
                        let undefined = cx.undefined();
                        let args = vec![cx.string(level).upcast(), cx.string(text).upcast()];
                        // An exception thrown by the callback must not become a fatal uncaught exception.
                        let _ = cx.try_catch(|cx| cb.call(cx, undefined, args));
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

    // Log records only reach the callback through this channel. It is unref'd so that
    // holding on to it does not keep the Node event loop alive.
    if let Ok(mut channel_lock) = LOGGER_CHANNEL.lock() {
        let mut channel = cx.channel();
        channel.unref(cx);
        *channel_lock = Some(channel);
    }
}
