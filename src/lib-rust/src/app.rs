use semver::Version;
use std::env;
use std::process::exit;

use crate::{
    locator::{auto_locate, VelopackLocator},
    Error,
};

/// VelopackApp helps you to handle app activation events correctly.
/// This should be used as early as possible in your application startup code.
/// (eg. the beginning of main() or wherever your entry point is)
pub struct VelopackApp<'a> {
    install_hook: Option<Box<dyn FnOnce(Version) + 'a>>,
    update_hook: Option<Box<dyn FnOnce(Version) + 'a>>,
    obsolete_hook: Option<Box<dyn FnOnce(Version) + 'a>>,
    uninstall_hook: Option<Box<dyn FnOnce(Version) + 'a>>,
    firstrun_hook: Option<Box<dyn FnOnce(Version) + 'a>>,
    restarted_hook: Option<Box<dyn FnOnce(Version) + 'a>>,
    // auto_apply: bool,
    args: Vec<String>,
    locator: Option<VelopackLocator>,
}

impl<'a> VelopackApp<'a> {
    /// Create a new VelopackApp instance.
    pub fn build() -> Self {
        VelopackApp {
            install_hook: None,
            update_hook: None,
            obsolete_hook: None,
            uninstall_hook: None,
            firstrun_hook: None,
            restarted_hook: None,
            // auto_apply: true, // Default to true
            args: env::args().skip(1).collect(),
            locator: None,
        }
    }

    /// Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
    pub fn set_args(mut self, args: Vec<String>) -> Self {
        self.args = args;
        self
    }

    /// Set whether to automatically apply downloaded updates on startup. This is ON by default.
    // pub fn set_auto_apply_on_startup(mut self, apply: bool) -> Self {
    //     self.auto_apply = apply;
    //     self
    // }

    /// Override the default file locator with a custom one (eg. for testing)
    pub fn set_locator(mut self, locator: VelopackLocator) -> Self {
        self.locator = Some(locator);
        self
    }

    /// This hook is triggered when the application is started for the first time after installation.
    pub fn on_first_run<F: FnOnce(Version) + 'a>(mut self, hook: F) -> Self {
        self.firstrun_hook = Some(Box::new(hook));
        self
    }

    /// This hook is triggered when the application is restarted by Velopack after installing updates.
    pub fn on_restarted<F: FnOnce(Version) + 'a>(mut self, hook: F) -> Self {
        self.restarted_hook = Some(Box::new(hook));
        self
    }

    /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
    /// Your code will be run and then the process will exit.
    /// If your code has not completed within 30 seconds, it will be terminated.
    /// Only supported on windows; On other operating systems, this will never be called.
    #[cfg(target_os = "windows")]
    pub fn on_after_install_fast_callback<F: FnOnce(Version) + 'a>(mut self, hook: F) -> Self {
        self.install_hook = Some(Box::new(hook));
        self
    }

    /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
    /// Your code will be run and then the process will exit.
    /// If your code has not completed within 15 seconds, it will be terminated.
    /// Only supported on windows; On other operating systems, this will never be called.
    #[cfg(target_os = "windows")]
    pub fn on_after_update_fast_callback<F: FnOnce(Version) + 'a>(mut self, hook: F) -> Self {
        self.update_hook = Some(Box::new(hook));
        self
    }

    /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
    /// Your code will be run and then the process will exit.
    /// If your code has not completed within 15 seconds, it will be terminated.
    /// Only supported on windows; On other operating systems, this will never be called.
    #[cfg(target_os = "windows")]
    pub fn on_before_update_fast_callback<F: FnOnce(Version) + 'a>(mut self, hook: F) -> Self {
        self.obsolete_hook = Some(Box::new(hook));
        self
    }

    /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
    /// Your code will be run and then the process will exit.
    /// If your code has not completed within 30 seconds, it will be terminated.
    /// Only supported on windows; On other operating systems, this will never be called.
    #[cfg(target_os = "windows")]
    pub fn on_before_uninstall_fast_callback<F: FnOnce(Version) + 'a>(mut self, hook: F) -> Self {
        self.uninstall_hook = Some(Box::new(hook));
        self
    }

    /// Runs the Velopack startup logic. This should be the first thing to run in your app.
    /// In some circumstances it may terminate/restart the process to perform tasks.
    pub fn run(&mut self) {
        let args: Vec<String> = self.args.clone();

        info!("VelopackApp: Running with args: {:?}", args);

        if args.len() >= 2 {
            match args[0].as_str() {
                "--veloapp-install" => Self::call_fast_hook(&mut self.install_hook, &args[1]),
                "--veloapp-updated" => Self::call_fast_hook(&mut self.update_hook, &args[1]),
                "--veloapp-obsolete" => Self::call_fast_hook(&mut self.obsolete_hook, &args[1]),
                "--veloapp-uninstall" => Self::call_fast_hook(&mut self.uninstall_hook, &args[1]),
                _ => {} // Handle other cases or do nothing
            }
        }

        let my_version = if let Ok(ver) = self.get_current_version() { ver } else { semver::Version::new(0, 0, 0) };

        let firstrun = env::var("VELOPACK_FIRSTRUN").is_ok();
        let restarted = env::var("VELOPACK_RESTART").is_ok();
        env::remove_var("VELOPACK_FIRSTRUN");
        env::remove_var("VELOPACK_RESTART");

        if firstrun {
            Self::call_hook(&mut self.firstrun_hook, &my_version);
        }

        if restarted {
            Self::call_hook(&mut self.restarted_hook, &my_version);
        }
    }

    fn call_hook(hook_option: &mut Option<Box<dyn FnOnce(Version) + 'a>>, version: &Version) {
        if let Some(hook) = hook_option.take() {
            hook(version.clone());
        }
    }

    fn call_fast_hook(hook_option: &mut Option<Box<dyn FnOnce(Version) + 'a>>, arg: &str) {
        if let Some(hook) = hook_option.take() {
            if let Ok(version) = Version::parse(arg) {
                hook(version);
                let debug_mode = env::var("VELOPACK_DEBUG").is_ok();
                if !debug_mode {
                    exit(0);
                }
            }
        }
    }

    fn get_locator(&self) -> Result<VelopackLocator, Error> {
        if let Some(locator) = &self.locator {
            return Ok(locator.clone());
        }

        return auto_locate();
    }

    fn get_current_version(&self) -> Result<Version, Error> {
        self.get_locator().map(|l| l.load_manifest()).map(|m| m.map(|m| m.version))?
    }
}
