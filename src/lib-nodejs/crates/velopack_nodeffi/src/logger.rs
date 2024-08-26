use log::{Level, Log, Metadata, Record};
use neon::{event::Channel, prelude::*};

struct LoggerImpl {
    channel: Channel,
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

        self.channel.send(move |mut cx| {
            let console = cx.global::<JsObject>("console")?;
            let log_fn: Handle<JsFunction> = console.get(&mut cx, level)?;
            let args = vec![cx.string(text).upcast()];
            log_fn.call(&mut cx, console, args.clone())?;
            Ok(())
        });
    }

    fn flush(&self) {}
}

pub fn init_logger_callback(channel: Channel) {
    let logger = LoggerImpl { channel };
    let _ = log::set_boxed_logger(Box::new(logger));
    log::set_max_level(log::LevelFilter::Trace);
}
