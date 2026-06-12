//! Minimal bridge forwarding Rust `log` records to Python's `logging` module.
//! This replaces the `pyo3-log` crate, which pins `pyo3 < 0.29` (even on master)
//! and would otherwise hold back pyo3 upgrades such as the one required for
//! RUSTSEC-2026-0176 / RUSTSEC-2026-0177.

use log::{Level, LevelFilter, Log, Metadata, Record};
use pyo3::prelude::*;

struct PythonLogger;

impl Log for PythonLogger {
    fn enabled(&self, _metadata: &Metadata) -> bool {
        true
    }

    fn log(&self, record: &Record) {
        // Python logger names are dotted paths, Rust targets are `::` separated.
        let target = record.target().replace("::", ".");
        let level: u8 = match record.level() {
            Level::Error => 40,
            Level::Warn => 30,
            Level::Info => 20,
            Level::Debug => 10,
            Level::Trace => 5,
        };
        let message = record.args().to_string();
        Python::attach(|py| {
            // Errors are swallowed: a failed log call must never raise into Python.
            let _ = py
                .import("logging")
                .and_then(|logging| logging.call_method1("getLogger", (target,)))
                .and_then(|logger| logger.call_method1("log", (level, message)));
        });
    }

    fn flush(&self) {}
}

/// Installs the bridge as the global `log` logger. Safe to call more than once;
/// subsequent calls are no-ops because a global logger is already registered.
pub fn init() {
    if log::set_boxed_logger(Box::new(PythonLogger)).is_ok() {
        log::set_max_level(LevelFilter::Debug);
    }
}
