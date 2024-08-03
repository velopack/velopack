use std::process::exit;

/// The main VelopackApp struct. This is the main entry point for your app.
pub struct VelopackApp {}

impl VelopackApp {
    /// Create a new VelopackApp instance.
    pub fn build() -> Self {
        VelopackApp {}
    }
    /// Runs the Velopack startup logic. This should be the first thing to run in your app.
    /// In some circumstances it may terminate/restart the process to perform tasks.
    pub fn run(&self) {
        for (_, arg) in std::env::args().enumerate() {
            match arg.to_ascii_lowercase().as_str() {
                "--veloapp-install" => exit(0),
                "--veloapp-updated" => exit(0),
                "--veloapp-obsolete" => exit(0),
                "--veloapp-uninstall" => exit(0),
                _ => {}
            }
        }
    }
}
