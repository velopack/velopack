use semver::Version;
use std::env;
use std::process::exit;

use crate::{
    constants::*,
    locator::{self, VelopackLocatorConfig},
    manager, sources,
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
    auto_apply: bool,
    #[allow(dead_code)]
    custom_aumid: Option<String>,
    args: Vec<String>,
    locator: Option<VelopackLocatorConfig>,
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
            auto_apply: true, // Default to true
            custom_aumid: None,
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
    pub fn set_auto_apply_on_startup(mut self, apply: bool) -> Self {
        self.auto_apply = apply;
        self
    }

    /// Override the Application User Model ID (AUMID) set for this process on Windows.
    /// By default, the AUMID is read from the package manifest (shortcutAumid), falling back to "velopack.{AppId}".
    #[cfg(target_os = "windows")]
    pub fn set_app_user_model_id(mut self, aumid: &str) -> Self {
        self.custom_aumid = Some(aumid.to_string());
        self
    }

    /// Override the default file locator with a custom one (eg. for testing)
    pub fn set_locator(mut self, locator: VelopackLocatorConfig) -> Self {
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
            match args[0].to_ascii_lowercase().as_str() {
                HOOK_CLI_INSTALL => Self::call_fast_hook(&mut self.install_hook, &args[1]),
                HOOK_CLI_UPDATED => Self::call_fast_hook(&mut self.update_hook, &args[1]),
                HOOK_CLI_OBSOLETE => Self::call_fast_hook(&mut self.obsolete_hook, &args[1]),
                HOOK_CLI_UNINSTALL => Self::call_fast_hook(&mut self.uninstall_hook, &args[1]),
                _ => {} // do nothing
            }
        }

        let manager = manager::UpdateManager::new(sources::NoneSource {}, None, self.locator.clone());
        if let Err(e) = manager {
            error!("VelopackApp: Error loading manager/locator: {:?}", e);
            return;
        }
        let manager = manager.unwrap();

        #[cfg(target_os = "windows")]
        {
            let aumid = self
                .custom_aumid
                .as_deref()
                .map(|s| s.to_string())
                .unwrap_or_else(|| manager.get_locator().get_app_user_model_id());
            info!("Setting current process explicit AppUserModelID to '{}'", aumid);
            unsafe {
                let wide = crate::wide_strings::string_to_wide(&aumid);
                let _ = windows::Win32::UI::Shell::SetCurrentProcessExplicitAppUserModelID(wide.as_pcwstr());
            }
        }

        let my_version = manager.get_current_version();
        let packages_dir = manager.get_locator().get_packages_dir();

        // Load all local packages once — used for both auto-apply and cleanup.
        let local_packages = locator::find_local_full_packages(&packages_dir);
        let latest_full = local_packages
            .iter()
            .filter(|(_, m)| m.version > my_version)
            .max_by(|(_, a), (_, b)| a.version.cmp(&b.version));

        let firstrun = env::var(HOOK_ENV_FIRSTRUN).is_ok();
        env::remove_var(HOOK_ENV_FIRSTRUN);

        let restarted = env::var(HOOK_ENV_RESTART).is_ok();
        env::remove_var(HOOK_ENV_RESTART);

        // if auto apply is true and we haven't just been restarted via Velopack apply,
        // we should check for a local package downloaded with a version greater than ours.
        // If it exists, we should quit and apply it now.
        let pending_version = if let Some((path, manifest)) = latest_full {
            let pending_ver = manifest.version.clone();
            if self.auto_apply && !restarted {
                let asset = manager::local_path_to_asset(manifest, path);
                if let Err(e) = manager.apply_updates_and_restart_with_args(&asset, &args) {
                    error!("VelopackApp: Error applying pending updates on startup: {:?}", e);
                }
            }
            Some(pending_ver)
        } else {
            None
        };

        // clean up old packages
        cleanup_old_packages_from_list(local_packages, &my_version, pending_version.as_ref());

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
        info!("VelopackApp: Fast callback hook triggered.");
        if let Some(hook) = hook_option.take() {
            if let Ok(version) = Version::parse(arg) {
                hook(version);
            }
        }

        let debug_mode = env::var(HOOK_ENV_DEBUG).is_ok();
        if debug_mode {
            warn!("VelopackApp: Debug mode enabled, not quitting for fast callback hook.");
        } else {
            exit(0);
        }
    }
}

fn cleanup_old_packages_from_list(
    packages: Vec<(std::path::PathBuf, crate::bundle::Manifest)>,
    current_version: &Version,
    pending_version: Option<&Version>,
) {
    for (path, manifest) in &packages {
        if manifest.version == *current_version {
            continue;
        }
        if let Some(pv) = pending_version {
            if &manifest.version == pv {
                continue;
            }
        }
        info!("Removing old package: {:?}", path);
        let _ = std::fs::remove_file(path);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use zip::write::SimpleFileOptions;

    /// Creates a minimal .nupkg (zip with a .nuspec) in the given directory.
    fn create_test_nupkg(dir: &std::path::Path, id: &str, version: &str) -> std::path::PathBuf {
        let filename = format!("{}-{}-full.nupkg", id, version);
        let path = dir.join(&filename);
        let file = std::fs::File::create(&path).unwrap();
        let mut zip = zip::ZipWriter::new(file);
        let nuspec = format!(
            r#"<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>{}</id>
    <version>{}</version>
    <title>{}</title>
    <authors>test</authors>
    <description>test</description>
    <mainExe>test.exe</mainExe>
  </metadata>
</package>"#,
            id, version, id
        );
        zip.start_file(format!("{}.nuspec", id), SimpleFileOptions::default()).unwrap();
        zip.write_all(nuspec.as_bytes()).unwrap();
        zip.finish().unwrap();
        path
    }

    #[test]
    fn test_cleanup_old_packages_removes_old_keeps_current_and_pending() {
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let packages_dir = tmp_dir.path();

        let current = Version::parse("2.0.0").unwrap();
        let pending = Version::parse("3.0.0").unwrap();

        // old version — should be deleted
        let old_pkg = create_test_nupkg(packages_dir, "TestApp", "1.0.0");
        // current version — should be kept
        let current_pkg = create_test_nupkg(packages_dir, "TestApp", "2.0.0");
        // pending version — should be kept
        let pending_pkg = create_test_nupkg(packages_dir, "TestApp", "3.0.0");

        let packages = locator::find_local_full_packages(&packages_dir.to_path_buf());
        cleanup_old_packages_from_list(packages, &current, Some(&pending));

        assert!(!old_pkg.exists(), "Old package should have been deleted");
        assert!(current_pkg.exists(), "Current version package should be kept");
        assert!(pending_pkg.exists(), "Pending version package should be kept");
    }

    #[test]
    fn test_cleanup_old_packages_no_pending() {
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let packages_dir = tmp_dir.path();

        let current = Version::parse("2.0.0").unwrap();

        let old_pkg = create_test_nupkg(packages_dir, "TestApp", "1.0.0");
        let current_pkg = create_test_nupkg(packages_dir, "TestApp", "2.0.0");
        let newer_pkg = create_test_nupkg(packages_dir, "TestApp", "3.0.0");

        let packages = locator::find_local_full_packages(&packages_dir.to_path_buf());
        cleanup_old_packages_from_list(packages, &current, None);

        assert!(!old_pkg.exists(), "Old package should have been deleted");
        assert!(current_pkg.exists(), "Current version package should be kept");
        assert!(!newer_pkg.exists(), "Newer package with no pending should be deleted");
    }

    #[test]
    fn test_cleanup_old_packages_invalid_nupkg_not_loaded() {
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let packages_dir = tmp_dir.path();

        // Write a corrupt nupkg (not a valid zip) — load_local_packages skips it
        let bad_path = packages_dir.join("garbage-1.0.0-full.nupkg");
        std::fs::write(&bad_path, b"not a zip file").unwrap();

        let current_pkg = create_test_nupkg(packages_dir, "TestApp", "1.0.0");

        let packages = locator::find_local_full_packages(&packages_dir.to_path_buf());
        assert_eq!(packages.len(), 1, "Only valid packages should be loaded");
        assert!(current_pkg.exists());
        assert!(bad_path.exists(), "Invalid nupkg is not in the loaded list, so not touched by cleanup");
    }
}
