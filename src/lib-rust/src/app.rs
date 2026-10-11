use semver::Version;
use std::env;
use std::process::exit;
use std::sync::Arc;

use crate::{
    constants::*,
    ipc::IpcEndpoint,
    locator::{self, VelopackLocatorConfig},
    lockfile::LockFile,
    manager, single_instance, sources,
};

/// A hook invoked with the file path or URL the operating system asked this app to open. `Send + Sync + 'static`
/// because in `SingleInstance` mode it is kept for the life of the process and invoked from the IPC server thread.
pub(crate) type AssociationHook = Arc<dyn Fn(String) + Send + Sync + 'static>;

/// One association activation: the canonical CLI flag (`--veloapp-fileassoc` / `--veloapp-urlproto`) and its value.
pub(crate) type Association = (&'static str, String);

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
    file_assoc_hook: Option<AssociationHook>,
    url_proto_hook: Option<AssociationHook>,
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
            file_assoc_hook: None,
            url_proto_hook: None,
            auto_apply: true, // Default to true
            custom_aumid: None,
            // lossy rather than `env::args()`, which panics on non-Unicode arguments: file association launches
            // pass arbitrary file names (NTFS permits unpaired surrogates) and must never abort the process here.
            args: env::args_os().skip(1).map(|a| a.to_string_lossy().into_owned()).collect(),
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

    /// This hook is triggered when the operating system opens this app to handle a file whose extension was
    /// registered with `vpk pack --fileAssociations`. The argument is the full path of the file to open.
    ///
    /// On Windows and macOS the path arrives as `--veloapp-fileassoc <path>` on the command line (on macOS the
    /// association helper bundle receives the open-file event and launches the app with it). With the default
    /// `NewInstance` launch mode the hook is invoked from `run()`, on the calling thread, after the regular startup
    /// logic (OnFirstRun/OnRestarted); the app then continues starting normally. The launch is never lost to an
    /// update: when `run()` restarts the app to apply a pending update, the file(s) are forwarded to the new
    /// version, which invokes this hook once per file.
    ///
    /// With `vpk pack --associationLaunchMode SingleInstance`, a launch while an instance of the app is already
    /// running hands the path to that instance and exits with code 0 before any update is applied. In the
    /// receiving instance the hook runs **on a background thread** (invocations are serialized; possibly while the
    /// main thread is still inside `run()`, even before the first-run/restarted hooks, or at any later time), so
    /// it must be thread-safe and marshal to the UI thread itself. Return promptly (post the work to the UI thread
    /// rather than blocking on it): the launching process waits at most 5 seconds for the hook to return, after
    /// which it handles the activation itself as a second instance. The running instance is not brought to the
    /// foreground by the OS, so activate your main window from the hook. If no instance answers within about 5
    /// seconds, the launching process handles the activation itself, as in `NewInstance` mode.
    ///
    /// macOS: the open-file consent (TCC) lands on the helper, not on this process. The first access to a file in
    /// a protected location (Desktop, Documents, Downloads, removable volumes) triggers the "Files and Folders"
    /// prompt, or fails with EPERM if the user denied it earlier; add the `NS*FolderUsageDescription` strings to
    /// your Info.plist. Associations require macOS 10.13+. Not supported on Linux.
    pub fn on_file_assoc<F: Fn(String) + Send + Sync + 'static>(mut self, hook: F) -> Self {
        self.file_assoc_hook = Some(Arc::new(hook));
        self
    }

    /// This hook is triggered when the operating system opens this app to handle a URL whose scheme was
    /// registered with `vpk pack --urlProtocols`. The argument is the full URL (eg. `myapp://open?id=1`).
    ///
    /// On Windows and macOS the URL arrives as `--veloapp-urlproto <url>` on the command line (on macOS the
    /// association helper bundle receives the open-URL event and launches the app with it). With the default
    /// `NewInstance` launch mode the hook is invoked from `run()`, on the calling thread, after the regular startup
    /// logic (OnFirstRun/OnRestarted); the app then continues starting normally. The launch is never lost to an
    /// update: when `run()` restarts the app to apply a pending update, the URL is forwarded to the new version,
    /// which invokes this hook.
    ///
    /// With `vpk pack --associationLaunchMode SingleInstance`, a launch while an instance of the app is already
    /// running hands the URL to that instance and exits with code 0 before any update is applied. In the receiving
    /// instance the hook runs **on a background thread** (invocations are serialized; possibly while the main
    /// thread is still inside `run()`, even before the first-run/restarted hooks, or at any later time), so it
    /// must be thread-safe and marshal to the UI thread itself. Return promptly (post the work to the UI thread
    /// rather than blocking on it): the launching process waits at most 5 seconds for the hook to return, after
    /// which it handles the activation itself as a second instance. The running instance is not brought to the
    /// foreground by the OS, so activate your main window from the hook. If no instance answers within about 5
    /// seconds, the launching process handles the activation itself, as in `NewInstance` mode.
    ///
    /// Associations require macOS 10.13+. Not supported on Linux.
    pub fn on_url_proto<F: Fn(String) + Send + Sync + 'static>(mut self, hook: F) -> Self {
        self.url_proto_hook = Some(Arc::new(hook));
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

        if args.first().is_some_and(|a| canonical_association_flag(a).is_some()) {
            // the values (file paths or URLs, which may carry tokens) are kept out of the persistent log
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

        // An OS file/URL association activation. Only recognised when a matching hook is registered,
        // otherwise the arguments are left untouched for the application to interpret itself.
        let associations = self.detect_associations(&args);

        match manager::UpdateManager::new(sources::NoneSource {}, None, self.locator.clone()) {
            Ok(manager) => {
                // SingleInstance launch mode: the first instance serves association activations, a later launch
                // carrying one forwards it to that instance and exits (before any update is applied).
                let mut server = None;
                if self.has_association_hook() && manager.get_locator().get_manifest_association_single_instance() {
                    match single_instance::elect(manager.get_locator(), &associations) {
                        single_instance::Election::Forwarded => {
                            info!(
                                "VelopackApp: forwarded {} association(s) to the running instance, exiting.",
                                associations.len()
                            );
                            exit(0);
                        }
                        single_instance::Election::Server(ep, lock) => server = Some((ep, lock)),
                        single_instance::Election::Standalone => {}
                    }
                }
                self.run_installed_startup(&manager, &args, &associations, server)
            }
            // not installed (eg. a development build): nothing to apply or clean up, but an association launch is
            // still delivered below, as the C# library does.
            Err(e) => error!("VelopackApp: Error loading manager/locator: {:?}", e),
        }

        for (flag, value) in associations {
            if let Some(hook) = self.association_hook(flag) {
                info!("VelopackApp: Association activated ({}), {} chars.", flag, value.len());
                hook(value);
            }
        }
    }

    /// The part of startup which only applies when the app is installed: AUMID, auto-applying a pending update
    /// (may exit the process), starting the SingleInstance server (if this process was elected), package cleanup
    /// and the first-run / restarted hooks.
    fn run_installed_startup(
        &mut self,
        manager: &manager::UpdateManager,
        args: &[String],
        associations: &[Association],
        server: Option<(IpcEndpoint, LockFile)>,
    ) {
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

                // Hand the associations on as repeated [flag, value] pairs (leading the arguments) so the recovered
                // values survive the restart intact; without any, the arguments are forwarded as they are.
                let mut restart_args: Vec<String> = Vec::new();
                for (flag, value) in associations.iter() {
                    restart_args.push(flag.to_string());
                    restart_args.push(value.clone());
                }
                if associations.is_empty() {
                    restart_args.extend(args.iter().cloned());
                }

                // on success this exits the process (releasing the SingleInstance lock); on failure the
                // activation is still delivered in this process
                if let Err(e) = manager.apply_updates_and_restart_with_args(&asset, &restart_args) {
                    error!("VelopackApp: Error applying pending updates on startup: {:?}", e);
                }
            }
            Some(pending_ver)
        } else {
            None
        };

        // The process is now committed to staying alive: serve forwarded activations from here on, before the
        // (arbitrarily slow) first-run / restarted hooks, so that association launches made meanwhile are not
        // left waiting for the whole connect window and then duplicated.
        if let Some((ep, lock)) = server {
            single_instance::start(ep, lock, self.file_assoc_hook.clone(), self.url_proto_hook.clone());
        }

        // clean up old packages
        cleanup_old_packages_from_list(local_packages, &my_version, pending_version.as_ref());

        if firstrun {
            Self::call_hook(&mut self.firstrun_hook, &my_version);
        }

        if restarted {
            Self::call_hook(&mut self.restarted_hook, &my_version);
        }
    }

    fn has_association_hook(&self) -> bool {
        self.file_assoc_hook.is_some() || self.url_proto_hook.is_some()
    }

    fn association_hook(&self, flag: &str) -> Option<&AssociationHook> {
        if flag == HOOK_CLI_FILE_ASSOC {
            self.file_assoc_hook.as_ref()
        } else {
            self.url_proto_hook.as_ref()
        }
    }

    /// Returns the association activations carried by `args` (`--veloapp-fileassoc <path>` /
    /// `--veloapp-urlproto <url>` pairs), or nothing when `args` are not an association launch or no hook is
    /// registered for any of them (the arguments are then left to the application).
    fn detect_associations(&self, args: &[String]) -> Vec<Association> {
        if !args.first().is_some_and(|a| canonical_association_flag(a).is_some()) {
            return Vec::new();
        }
        let associations = self.recover_associations(args);
        if associations.iter().any(|(flag, _)| self.association_hook(flag).is_some()) {
            associations
        } else {
            info!(
                "VelopackApp: No hook registered for {}, leaving the arguments to the application.",
                args[0]
            );
            Vec::new()
        }
    }

    /// Recovers the association values from `args` (whose first element is an association flag). A launch by the OS
    /// shell on Windows (no `VELOPACK_RESTART`) carries exactly one value: it is taken from the raw command line
    /// whenever the flag is found there — also when `set_args` was used, as apps commonly pass their own argv
    /// through — so a value containing quotes is not mangled by argv splitting. Without the flag on the raw command
    /// line (synthetic `set_args`) at most one `[flag, value]` pair is read from `args`, since the OS never passes
    /// more. Restarted launches (Update.exe re-quotes each argument) and other platforms read `args` as repeated
    /// pairs, as a Velopack restart forwards them.
    fn recover_associations(&self, args: &[String]) -> Vec<Association> {
        #[cfg(target_os = "windows")]
        if env::var(HOOK_ENV_RESTART).is_err() {
            let flag = canonical_association_flag(&args[0]).expect("checked by the caller");
            let command_line = unsafe { windows::Win32::System::Environment::GetCommandLineW() };
            let command_line = crate::wide_strings::wide_to_string_lossy(command_line);
            if let Some(value) = association_value_from_command_line(&command_line, flag) {
                return vec![(flag, value)];
            }
            return association_pairs_from_args(args).into_iter().take(1).collect();
        }
        association_pairs_from_args(args)
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

/// Returns the canonical (lowercase) association flag constant `arg` matches, if any. Flags are matched
/// case-insensitively, like the other `--veloapp-*` hooks.
pub(crate) fn canonical_association_flag(arg: &str) -> Option<&'static str> {
    match arg.to_ascii_lowercase().as_str() {
        HOOK_CLI_FILE_ASSOC => Some(HOOK_CLI_FILE_ASSOC),
        HOOK_CLI_URL_PROTO => Some(HOOK_CLI_URL_PROTO),
        _ => None,
    }
}

/// Reads the leading `[flag, value]` pairs of `args` — the form in which a Velopack restart (or `set_args`) hands
/// association activations over. Parsing stops at the first element which is not an association flag; a flag with
/// nothing or an empty string after it is dropped with a warning.
pub(crate) fn association_pairs_from_args(args: &[String]) -> Vec<Association> {
    let mut pairs = Vec::new();
    let mut i = 0;
    while i < args.len() {
        let Some(flag) = canonical_association_flag(&args[i]) else { break };
        match args.get(i + 1) {
            Some(value) if !value.is_empty() => pairs.push((flag, value.clone())),
            _ => warn!("VelopackApp: {} was passed without a value, ignoring.", flag),
        }
        i += 2;
    }
    pairs
}

/// Recovers the value that follows `flag` in a raw (unparsed) command line, as the shell hands it over when a
/// registered association such as `"app.exe" --veloapp-urlproto "%1"` is activated. The program path is
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
    use std::sync::atomic::Ordering;
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
        let cmd = r#""C:\Apps\My App\current\MyApp.exe" --veloapp-urlproto "myapp://open?title="hi there"&x=1""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_URL_PROTO).as_deref(),
            Some(r#"myapp://open?title="hi there"&x=1"#)
        );

        let cmd = r#""C:\Apps\MyApp.exe" --veloapp-urlproto ""myapp://x"""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_URL_PROTO).as_deref(),
            Some(r#""myapp://x""#)
        );
    }

    #[test]
    fn test_association_value_from_command_line_unquoted_and_whitespace() {
        let cmd = "  MyApp.exe   --veloapp-fileassoc   C:\\docs\\my file.myproj   ";
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_FILE_ASSOC).as_deref(),
            Some("C:\\docs\\my file.myproj")
        );
        // a trailing backslash (which argv parsing would eat together with a closing quote) is preserved
        let cmd = r#""C:\MyApp.exe" --veloapp-fileassoc "C:\docs\dir\""#;
        assert_eq!(
            association_value_from_command_line(cmd, HOOK_CLI_FILE_ASSOC).as_deref(),
            Some(r"C:\docs\dir\")
        );
    }

    #[test]
    fn test_association_value_from_command_line_flag_matching() {
        // case-insensitive flag
        let cmd = r#""C:\MyApp.exe" --VeloApp-URLProto "myapp://x""#;
        assert_eq!(association_value_from_command_line(cmd, HOOK_CLI_URL_PROTO).as_deref(), Some("myapp://x"));
        // the flag inside the program path, or as a prefix of a longer token, is not the flag
        let cmd = r#""C:\dir --veloapp-urlproto \MyApp.exe" --veloapp-urlproto-extra 1 --veloapp-urlproto "myapp://y""#;
        assert_eq!(association_value_from_command_line(cmd, HOOK_CLI_URL_PROTO).as_deref(), Some("myapp://y"));
        // absent flag, flag with nothing after it, or only empty quotes
        assert_eq!(
            association_value_from_command_line(r#""C:\MyApp.exe" myapp://x"#, HOOK_CLI_URL_PROTO),
            None
        );
        assert_eq!(
            association_value_from_command_line(r#""C:\MyApp.exe" --veloapp-urlproto"#, HOOK_CLI_URL_PROTO),
            None
        );
        assert_eq!(
            association_value_from_command_line(r#""C:\MyApp.exe" --veloapp-urlproto  "" "#, HOOK_CLI_URL_PROTO),
            None
        );
        assert_eq!(association_value_from_command_line("", HOOK_CLI_URL_PROTO), None);
    }

    fn strings(args: &[&str]) -> Vec<String> {
        args.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn test_association_pairs_from_args() {
        // one pair; flag casing is not significant and the value is taken verbatim (argv is exact after a restart)
        assert_eq!(
            association_pairs_from_args(&strings(&["--VeloApp-UrlProto", r#"myapp://open?q="quoted" value"#])),
            vec![(HOOK_CLI_URL_PROTO, r#"myapp://open?q="quoted" value"#.to_string())]
        );
        // several pairs of mixed kinds, eg. a macOS launch forwarded through an update
        assert_eq!(
            association_pairs_from_args(&strings(&[
                "--veloapp-fileassoc",
                "/Users/me/a.myproj",
                "--veloapp-fileassoc",
                "/Users/me/b c.myproj",
                "--veloapp-urlproto",
                "myapp://x",
            ])),
            vec![
                (HOOK_CLI_FILE_ASSOC, "/Users/me/a.myproj".to_string()),
                (HOOK_CLI_FILE_ASSOC, "/Users/me/b c.myproj".to_string()),
                (HOOK_CLI_URL_PROTO, "myapp://x".to_string()),
            ]
        );
        // parsing stops at the first element which is not a flag; the rest is left to the app
        assert_eq!(
            association_pairs_from_args(&strings(&["--veloapp-urlproto", "myapp://x", "other", "--veloapp-urlproto", "myapp://y"])),
            vec![(HOOK_CLI_URL_PROTO, "myapp://x".to_string())]
        );
        // a flag with nothing, or an empty string, after it is dropped
        assert!(association_pairs_from_args(&strings(&["--veloapp-urlproto"])).is_empty());
        assert_eq!(
            association_pairs_from_args(&strings(&["--veloapp-urlproto", "", "--veloapp-fileassoc", "C:\\x.myproj"])),
            vec![(HOOK_CLI_FILE_ASSOC, "C:\\x.myproj".to_string())]
        );
        // not an association launch at all
        assert!(association_pairs_from_args(&strings(&["myapp://x", "--veloapp-urlproto", "myapp://y"])).is_empty());
        assert!(association_pairs_from_args(&[]).is_empty());
    }

    /// The `run()` tests read and clear the `VELOPACK_RESTART` / `VELOPACK_FIRSTRUN` environment of this process,
    /// so they must not overlap.
    static RUN_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

    fn run_lock() -> std::sync::MutexGuard<'static, ()> {
        RUN_LOCK.lock().unwrap_or_else(|e| e.into_inner())
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
    fn test_run_invokes_url_proto_hook_and_leaves_trailing_args() {
        let _guard = run_lock();
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let files = received.clone();
        let urls = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec!["--VELOAPP-urlproto".into(), "myapp://open?a=1".into(), "b".into()])
            .on_file_assoc(move |p| files.lock().unwrap().push(format!("file:{}", p)))
            .on_url_proto(move |u| urls.lock().unwrap().push(format!("url:{}", u)))
            .run();
        assert_eq!(*received.lock().unwrap(), vec!["url:myapp://open?a=1".to_string()]);
    }

    fn two_file_pairs() -> Vec<String> {
        strings(&[
            HOOK_CLI_FILE_ASSOC,
            "C:\\docs\\my file.myproj",
            HOOK_CLI_FILE_ASSOC,
            "C:\\docs\\other.myproj",
        ])
    }

    #[test]
    fn test_run_invokes_file_assoc_hook_once_per_pair_when_restarted() {
        let _guard = run_lock();
        // several pairs are only ever forwarded by a Velopack restart (run() clears the variable again)
        env::set_var(HOOK_ENV_RESTART, "1");
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let files = received.clone();
        let restarted = Arc::new(std::sync::atomic::AtomicBool::new(false));
        let restarted_flag = restarted.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(two_file_pairs())
            .on_restarted(move |_| restarted_flag.store(true, Ordering::SeqCst))
            .on_file_assoc(move |p| files.lock().unwrap().push(p))
            .run();
        env::remove_var(HOOK_ENV_RESTART);
        assert!(restarted.load(Ordering::SeqCst));
        assert_eq!(
            *received.lock().unwrap(),
            vec!["C:\\docs\\my file.myproj".to_string(), "C:\\docs\\other.myproj".to_string()]
        );
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn test_run_direct_windows_launch_takes_at_most_one_pair() {
        let _guard = run_lock();
        // not restarted and the flag is not on this process's real command line: the OS never passes more than one
        // value, so only the first pair of the (synthetic) args is an association
        env::remove_var(HOOK_ENV_RESTART);
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let files = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(two_file_pairs())
            .on_file_assoc(move |p| files.lock().unwrap().push(p))
            .run();
        assert_eq!(*received.lock().unwrap(), vec!["C:\\docs\\my file.myproj".to_string()]);
    }

    #[cfg(not(target_os = "windows"))]
    #[test]
    fn test_run_invokes_file_assoc_hook_once_per_pair_from_argv() {
        let _guard = run_lock();
        // outside Windows argv is always exact, so the pairs are read whether or not this is a restart
        env::remove_var(HOOK_ENV_RESTART);
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let files = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(two_file_pairs())
            .on_file_assoc(move |p| files.lock().unwrap().push(p))
            .run();
        assert_eq!(
            *received.lock().unwrap(),
            vec!["C:\\docs\\my file.myproj".to_string(), "C:\\docs\\other.myproj".to_string()]
        );
    }

    #[test]
    fn test_run_invokes_hook_when_app_is_not_installed() {
        let _guard = run_lock();
        // the locator cannot be created (no Update.exe), as for a development build: the installed-startup logic is
        // skipped but the association launch is still delivered
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let mut config = test_locator(tmp_dir.path());
        config.UpdateExePath = tmp_dir.path().join("does-not-exist.exe");
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let urls = received.clone();
        VelopackApp::build()
            .set_locator(config)
            .set_args(vec![HOOK_CLI_URL_PROTO.into(), "myapp://dev".into()])
            .on_url_proto(move |u| urls.lock().unwrap().push(u))
            .run();
        assert_eq!(*received.lock().unwrap(), vec!["myapp://dev".to_string()]);
    }

    #[test]
    fn test_run_ignores_association_flag_without_hook_or_value() {
        let _guard = run_lock();
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let urls = received.clone();
        // file flag but only a url hook registered: nothing is invoked, args are left to the app
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec![HOOK_CLI_FILE_ASSOC.into(), "C:\\x.myproj".into()])
            .on_url_proto(move |u| urls.lock().unwrap().push(u))
            .run();
        // url flag without a value: ignored
        let urls = received.clone();
        VelopackApp::build()
            .set_locator(test_locator(tmp_dir.path()))
            .set_args(vec![HOOK_CLI_URL_PROTO.into()])
            .on_url_proto(move |u| urls.lock().unwrap().push(u))
            .run();
        assert!(received.lock().unwrap().is_empty());
    }

    /// A locator whose manifest (written into `dir`) selects the SingleInstance launch mode.
    fn single_instance_locator(dir: &std::path::Path) -> VelopackLocatorConfig {
        let fixture = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test/fixtures/Test.Squirrel-App.nuspec");
        let xml = std::fs::read_to_string(fixture)
            .unwrap()
            .replace("</metadata>", "<associationLaunchMode>SingleInstance</associationLaunchMode></metadata>");
        let manifest = dir.join("sq.version");
        std::fs::write(&manifest, xml).unwrap();
        let mut config = test_locator(dir);
        config.UpdateExePath = manifest.clone();
        config.ManifestPath = manifest;
        config
    }

    fn connect_with_retries(ep: &crate::ipc::IpcEndpoint) -> crate::ipc::IpcConnection {
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        loop {
            match crate::ipc::connect(ep).unwrap() {
                Some(c) => return c,
                None => {
                    assert!(std::time::Instant::now() < deadline, "no server at {:?}", ep.address);
                    std::thread::sleep(std::time::Duration::from_millis(20));
                }
            }
        }
    }

    fn forward(ep: &crate::ipc::IpcEndpoint, value: &str) -> bool {
        let mut msg = crate::ipc::IpcMessage::new(crate::ipc::TYPE_ASSOCIATION);
        msg.items.push(crate::ipc::IpcItem {
            flag: HOOK_CLI_URL_PROTO.into(),
            value: value.into(),
        });
        crate::ipc::request(connect_with_retries(ep), &msg, crate::ipc::EXCHANGE_TIMEOUT)
            .unwrap()
            .is_ack()
    }

    #[test]
    fn test_run_single_instance_serves_forwarded_activations() {
        let _guard = run_lock();
        env::remove_var(HOOK_ENV_RESTART);
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<(String, std::thread::ThreadId)>::new()));
        let urls = received.clone();
        // no other instance: the first launch becomes the server and handles its own activation in-process
        VelopackApp::build()
            .set_locator(single_instance_locator(tmp_dir.path()))
            .set_args(vec![HOOK_CLI_URL_PROTO.into(), "myapp://first".into()])
            .on_url_proto(move |u| urls.lock().unwrap().push((u, std::thread::current().id())))
            .run();
        assert_eq!(
            *received.lock().unwrap(),
            vec![("myapp://first".to_string(), std::thread::current().id())]
        );

        // a forwarded activation reaches the hook on the IPC thread and is acked
        let ep = crate::ipc::IpcEndpoint::for_app("Test.Squirrel-App", tmp_dir.path()).unwrap();
        assert!(forward(&ep, "myapp://second"));
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while received.lock().unwrap().len() < 2 && std::time::Instant::now() < deadline {
            std::thread::sleep(std::time::Duration::from_millis(20));
        }
        let received = received.lock().unwrap();
        assert_eq!(received.len(), 2);
        assert_eq!(received[1].0, "myapp://second");
        assert_ne!(received[1].1, std::thread::current().id());
    }

    #[test]
    fn test_run_single_instance_server_is_up_before_firstrun_hook() {
        let _guard = run_lock();
        env::remove_var(HOOK_ENV_RESTART);
        env::set_var(HOOK_ENV_FIRSTRUN, "1");
        let tmp_dir = tempfile::TempDir::new().unwrap();
        let received = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let urls = received.clone();
        let (acked_tx, acked_rx) = std::sync::mpsc::channel();
        let ep = crate::ipc::IpcEndpoint::for_app("Test.Squirrel-App", tmp_dir.path()).unwrap();
        let client = std::thread::spawn(move || {
            let acked = forward(&ep, "myapp://x");
            acked_tx.send(acked).unwrap();
        });
        VelopackApp::build()
            .set_locator(single_instance_locator(tmp_dir.path()))
            .on_first_run(move |_| {
                // the server must already answer while this (arbitrarily slow) hook is still running
                assert!(acked_rx
                    .recv_timeout(std::time::Duration::from_secs(10))
                    .expect("server not up before the first-run hook"));
            })
            .on_url_proto(move |u| urls.lock().unwrap().push(u))
            .run();
        env::remove_var(HOOK_ENV_FIRSTRUN);
        client.join().unwrap();
        assert_eq!(*received.lock().unwrap(), vec!["myapp://x".to_string()]);
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
