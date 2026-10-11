use semver::Version;
use std::env;
use std::process::exit;
use std::sync::Arc;

use crate::{
    constants::*,
    locator::{self, VelopackLocatorConfig},
    manager, sources,
};

/// A hook invoked with the file path or URL the operating system asked this app to open.
/// `Send + Sync + 'static` because on macOS it is kept for the life of the process and invoked from the Cocoa run loop.
pub(crate) type AssociationHook = Arc<dyn Fn(String) + Send + Sync + 'static>;

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
    file_association_hook: Option<AssociationHook>,
    url_association_hook: Option<AssociationHook>,
    auto_apply: bool,
    #[allow(dead_code)]
    custom_aumid: Option<String>,
    args: Vec<String>,
    /// True once `set_args` was used, meaning `args` no longer mirror this process's real command line.
    args_overridden: bool,
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
            file_association_hook: None,
            url_association_hook: None,
            auto_apply: true, // Default to true
            custom_aumid: None,
            // lossy rather than `env::args()`, which panics on non-Unicode arguments: file association launches
            // pass arbitrary file names (NTFS permits unpaired surrogates) and must never abort the process here.
            args: env::args_os().skip(1).map(|a| a.to_string_lossy().into_owned()).collect(),
            args_overridden: false,
            locator: None,
        }
    }

    /// Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
    pub fn set_args(mut self, args: Vec<String>) -> Self {
        self.args = args;
        self.args_overridden = true;
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

    /// This hook is triggered when the operating system opens this app to handle a file whose extension was
    /// registered with `vpk pack --fileAssociations`. The argument is the full path of the file to open.
    ///
    /// On Windows the path arrives as `--veloapp-file-association <path>` on the command line and the hook is
    /// invoked once from `run()`, after the regular startup logic (the app then continues starting normally).
    /// On macOS files arrive as Apple Events, which requires a Cocoa (NSApplication) run loop: the hook may then be
    /// invoked at any time during the app's lifetime, on the main thread, and Velopack's handler replaces the UI
    /// framework's own open-file handling. When a pending update is auto-applied at startup on macOS the process
    /// exits before that launch's Apple Event is delivered, so the file is lost; macOS apps using associations should
    /// call `set_auto_apply_on_startup(false)` and apply updates at a moment of their choosing. Not supported on Linux.
    pub fn on_file_association<F: Fn(String) + Send + Sync + 'static>(mut self, hook: F) -> Self {
        self.file_association_hook = Some(Arc::new(hook));
        self
    }

    /// This hook is triggered when the operating system opens this app to handle a URL whose scheme was
    /// registered with `vpk pack --urlProtocols`. The argument is the full URL (eg. `myapp://open?id=1`).
    ///
    /// On Windows the URL arrives as `--veloapp-url-association <url>` on the command line and the hook is
    /// invoked once from `run()`, after the regular startup logic (the app then continues starting normally).
    /// On macOS URLs arrive as Apple Events, which requires a Cocoa (NSApplication) run loop: the hook may then be
    /// invoked at any time during the app's lifetime, on the main thread, and Velopack's handler replaces the UI
    /// framework's own open-URL handling. When a pending update is auto-applied at startup on macOS the process
    /// exits before that launch's Apple Event is delivered, so the URL is lost; macOS apps using associations should
    /// call `set_auto_apply_on_startup(false)` and apply updates at a moment of their choosing. Not supported on Linux.
    pub fn on_url_association<F: Fn(String) + Send + Sync + 'static>(mut self, hook: F) -> Self {
        self.url_association_hook = Some(Arc::new(hook));
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

        if args.first().is_some_and(|a| is_association_flag(a)) {
            // the value (a file path or URL, which may carry tokens) is kept out of the persistent log
            info!("VelopackApp: Running with args: [{:?}, <{} more>]", args[0], args.len() - 1);
        } else {
            info!("VelopackApp: Running with args: {:?}", args);
        }

        if args.len() >= 2 {
            match args[0].to_ascii_lowercase().as_str() {
                HOOK_CLI_INSTALL => Self::call_fast_hook(&mut self.install_hook, &args[1]),
                HOOK_CLI_UPDATED => Self::call_fast_hook(&mut self.update_hook, &args[1]),
                HOOK_CLI_OBSOLETE => Self::call_fast_hook(&mut self.obsolete_hook, &args[1]),
                HOOK_CLI_UNINSTALL => Self::call_fast_hook(&mut self.uninstall_hook, &args[1]),
                _ => {} // do nothing
            }
        }

        // An OS file/URL association activation. Only recognised when the matching hook is registered,
        // otherwise the arguments are left untouched for the application to interpret itself.
        let association = self.detect_association(&args);

        #[cfg(target_os = "macos")]
        if self.file_association_hook.is_some() || self.url_association_hook.is_some() {
            crate::apple_events::install(self.file_association_hook.clone(), self.url_association_hook.clone());
        }

        match manager::UpdateManager::new(sources::NoneSource {}, None, self.locator.clone()) {
            Ok(manager) => self.run_installed_startup(&manager, &args, association.as_ref()),
            // not installed (eg. a development build): nothing to apply or clean up, but an association launch is
            // still delivered below, as the C# library does.
            Err(e) => error!("VelopackApp: Error loading manager/locator: {:?}", e),
        }

        if let Some((flag, value)) = association {
            let hook = if flag == HOOK_CLI_FILE_ASSOCIATION {
                &self.file_association_hook
            } else {
                &self.url_association_hook
            };
            if let Some(hook) = hook {
                info!("VelopackApp: Association activated ({}), {} chars.", flag, value.len());
                hook(value);
            }
        }
    }

    /// The part of startup which only applies when the app is installed: AUMID, auto-applying a pending update
    /// (may exit the process), package cleanup and the first-run / restarted hooks.
    fn run_installed_startup(&mut self, manager: &manager::UpdateManager, args: &[String], association: Option<&(&'static str, String)>) {
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
                // Hand the association on as exactly [flag, value] so the recovered value survives the restart intact.
                let restart_args: Vec<String> = match association {
                    Some((flag, value)) => vec![flag.to_string(), value.clone()],
                    None => args.to_vec(),
                };
                if let Err(e) = manager.apply_updates_and_restart_with_args(&asset, &restart_args) {
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

    /// Returns the association flag and its value when `args` are an OS association activation
    /// (`--veloapp-file-association <path>` / `--veloapp-url-association <url>`) for which a hook is registered.
    fn detect_association(&self, args: &[String]) -> Option<(&'static str, String)> {
        let flag = match args.first()?.to_ascii_lowercase().as_str() {
            HOOK_CLI_FILE_ASSOCIATION if self.file_association_hook.is_some() => HOOK_CLI_FILE_ASSOCIATION,
            HOOK_CLI_URL_ASSOCIATION if self.url_association_hook.is_some() => HOOK_CLI_URL_ASSOCIATION,
            _ => return None,
        };
        let value = self.recover_association_value(args, flag);
        if value.is_empty() {
            warn!("VelopackApp: {} was passed without a value, ignoring.", flag);
            return None;
        }
        Some((flag, value))
    }

    /// Recovers the value following `flag`. When the args are this process's real command line (not `set_args`)
    /// and the OS launched us directly (not a Velopack restart, which re-quotes each argument), the raw command
    /// line is used so a value containing quotes is not mangled by argv splitting. Otherwise `args[1..]` are joined.
    #[cfg_attr(not(target_os = "windows"), allow(unused_variables))]
    fn recover_association_value(&self, args: &[String], flag: &str) -> String {
        #[cfg(target_os = "windows")]
        if !self.args_overridden && env::var(HOOK_ENV_RESTART).is_err() {
            let command_line = unsafe { windows::Win32::System::Environment::GetCommandLineW() };
            let command_line = crate::wide_strings::wide_to_string_lossy(command_line);
            if let Some(value) = association_value_from_command_line(&command_line, flag) {
                return value;
            }
        }
        args[1..].join(" ")
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

fn is_association_flag(arg: &str) -> bool {
    let arg = arg.to_ascii_lowercase();
    arg == HOOK_CLI_FILE_ASSOCIATION || arg == HOOK_CLI_URL_ASSOCIATION
}

/// Recovers the value that follows `flag` in a raw (unparsed) command line, as the shell hands it over when a
/// registered association such as `"app.exe" --veloapp-url-association "%1"` is activated. The program path is
/// skipped, then everything after the flag token is taken verbatim, trimmed, and ONE pair of enclosing double
/// quotes is removed — so a URL that itself contains quotes or backslashes survives intact where argv splitting
/// would have mangled it. Returns `None` when the flag is absent or nothing follows it.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
pub(crate) fn association_value_from_command_line(command_line: &str, flag: &str) -> Option<String> {
    let command_line = command_line.trim_start();
    // Skip the program path: a quoted path runs to the closing quote, otherwise to the first whitespace.
    let args_start = if let Some(rest) = command_line.strip_prefix('"') {
        rest.find('"').map(|i| i + 2).unwrap_or(command_line.len())
    } else {
        command_line.find(char::is_whitespace).unwrap_or(command_line.len())
    };
    let args = &command_line[args_start..];

    // Flags are ASCII, so lowercasing keeps byte offsets aligned with `args`.
    let lower = args.to_ascii_lowercase();
    let flag = flag.to_ascii_lowercase();
    let mut search_from = 0;
    while let Some(pos) = lower[search_from..].find(&flag) {
        let start = search_from + pos;
        let end = start + flag.len();
        let is_token =
            (start == 0 || lower[..start].ends_with(char::is_whitespace)) && (end == lower.len() || lower[end..].starts_with(char::is_whitespace));
        if is_token {
            let rest = args[end..].trim();
            let value = match rest.strip_prefix('"').and_then(|r| r.strip_suffix('"')) {
                Some(unquoted) => unquoted,
                None => rest,
            };
            return (!value.is_empty()).then(|| value.to_string());
        }
        search_from = end;
    }
    None
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
    fn test_association_value_from_command_line_strips_one_pair_of_quotes() {
        let cmd = r#""C:\Apps\My App\current\MyApp.exe" --veloapp-url-association "myapp://open?title="hi there"&x=1""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_URL_ASSOCIATION).as_deref(),
            Some(r#"myapp://open?title="hi there"&x=1"#)
        );

        let cmd = r#""C:\Apps\MyApp.exe" --veloapp-url-association ""myapp://x"""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_URL_ASSOCIATION).as_deref(),
            Some(r#""myapp://x""#)
        );
    }

    #[test]
    fn test_association_value_from_command_line_unquoted_and_whitespace() {
        let cmd = "  MyApp.exe   --veloapp-file-association   C:\\docs\\my file.myproj   ";
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_FILE_ASSOCIATION).as_deref(),
            Some("C:\\docs\\my file.myproj")
        );
        // a trailing backslash (which argv parsing would eat together with a closing quote) is preserved
        let cmd = r#""C:\MyApp.exe" --veloapp-file-association "C:\docs\dir\""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_FILE_ASSOCIATION).as_deref(),
            Some(r"C:\docs\dir\")
        );
    }

    #[test]
    fn test_association_value_from_command_line_flag_matching() {
        // case-insensitive flag
        let cmd = r#""C:\MyApp.exe" --VeloApp-URL-Association "myapp://x""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_URL_ASSOCIATION).as_deref(),
            Some("myapp://x")
        );
        // the flag inside the program path, or as a prefix of a longer token, is not the flag
        let cmd = r#""C:\dir --veloapp-url-association \MyApp.exe" --veloapp-url-association-extra 1 --veloapp-url-association "myapp://y""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_URL_ASSOCIATION).as_deref(),
            Some("myapp://y")
        );
        // absent flag, flag with nothing after it, or only empty quotes
        assert_eq!(
            association_value_from_command_line(r#""C:\MyApp.exe" myapp://x"#, HOOK_CLI_URL_ASSOCIATION),
            None
        );
        assert_eq!(
            association_value_from_command_line(r#""C:\MyApp.exe" --veloapp-url-association"#, HOOK_CLI_URL_ASSOCIATION),
            None
        );
        assert_eq!(
            association_value_from_command_line(r#""C:\MyApp.exe" --veloapp-url-association  "" "#, HOOK_CLI_URL_ASSOCIATION),
            None
        );
        assert_eq!(association_value_from_command_line("", HOOK_CLI_URL_ASSOCIATION), None);
    }

    fn test_locator(packages_dir: &std::path::Path) -> VelopackLocatorConfig {
        let fixtures = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test/fixtures");
        let manifest = fixtures.join("Test.Squirrel-App.nuspec");
        VelopackLocatorConfig {
            RootAppDir: packages_dir.to_path_buf(),
            // only has to exist for the locator to accept the config; nothing is launched in these tests
            UpdateExePath: manifest.clone(),
            PackagesDir: packages_dir.to_path_buf(),
            ManifestPath: manifest,
            CurrentBinaryDir: packages_dir.to_path_buf(),
            IsPortable: true,
        }
    }

    #[test]
    fn test_run_invokes_url_association_hook_with_joined_args() {
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let files = received.clone();
        let urls = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec!["--VELOAPP-url-association".into(), "myapp://open?a=1".into(), "b".into()])
            .on_file_association(move |p| files.lock().unwrap().push(format!("file:{}", p)))
            .on_url_association(move |u| urls.lock().unwrap().push(format!("url:{}", u)))
            .run();
        assert_eq!(*received.lock().unwrap(), vec!["url:myapp://open?a=1 b".to_string()]);
    }

    #[test]
    fn test_run_invokes_file_association_hook() {
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let files = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec![HOOK_CLI_FILE_ASSOCIATION.into(), "C:\\docs\\my file.myproj".into()])
            .on_file_association(move |p| files.lock().unwrap().push(p))
            .run();
        assert_eq!(*received.lock().unwrap(), vec!["C:\\docs\\my file.myproj".to_string()]);
    }

    #[test]
    fn test_run_invokes_hook_when_app_is_not_installed() {
        // the locator cannot be created (no Update.exe), as for a development build: the installed-startup logic is
        // skipped but the association launch is still delivered
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let mut config = test_locator(tmp_dir.path());
        config.UpdateExePath = tmp_dir.path().join("does-not-exist.exe");
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let urls = received.clone();
        VelopackApp::build()
            .set_locator(config)
            .set_args(vec![HOOK_CLI_URL_ASSOCIATION.into(), "myapp://dev".into()])
            .on_url_association(move |u| urls.lock().unwrap().push(u))
            .run();
        assert_eq!(*received.lock().unwrap(), vec!["myapp://dev".to_string()]);
    }

    #[test]
    fn test_run_ignores_association_flag_without_hook_or_value() {
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let urls = received.clone();
        // file flag but only a url hook registered: nothing is invoked, args are left to the app
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec![HOOK_CLI_FILE_ASSOCIATION.into(), "C:\\x.myproj".into()])
            .on_url_association(move |u| urls.lock().unwrap().push(u))
            .run();
        // url flag without a value: ignored
        let urls = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec![HOOK_CLI_URL_ASSOCIATION.into()])
            .on_url_association(move |u| urls.lock().unwrap().push(u))
            .run();
        assert!(received.lock().unwrap().is_empty());
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
