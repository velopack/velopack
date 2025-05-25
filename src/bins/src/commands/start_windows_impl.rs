use crate::{
    dialogs,
    shared::{self, OperationWait},
    windows as win,
};
use anyhow::{anyhow, bail, Result};
use std::{
    ffi::{OsStr, OsString},
    os::windows::process::CommandExt,
};
use std::{fs, path::Path, path::PathBuf, process::Command as Process};
use velopack::locator::{self, LocationContext, VelopackLocator};
use velopack::{bundle::Manifest, constants};
use windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow;

enum LocatorResult {
    Normal(VelopackLocator),
    Legacy(PathBuf, Manifest),
}

impl LocatorResult {
    pub fn get_root_dir(&self) -> PathBuf {
        match self {
            LocatorResult::Normal(locator) => locator.get_root_dir(),
            LocatorResult::Legacy(path, _) => path.clone(),
        }
    }
    pub fn get_manifest(&self) -> Manifest {
        match self {
            LocatorResult::Normal(locator) => locator.get_manifest(),
            LocatorResult::Legacy(_, manifest) => manifest.clone(),
        }
    }

    pub fn get_current_dir(&self) -> PathBuf {
        match self {
            LocatorResult::Normal(locator) => locator.get_current_bin_dir(),
            LocatorResult::Legacy(path, _) => path.join("current"),
        }
    }

    pub fn get_exe_to_start<P: AsRef<OsStr>>(&self, name: Option<P>) -> Result<PathBuf> {
        let current_dir = self.get_current_dir();
        if let Some(name) = name {
            Ok(Path::new(&current_dir).join(name.as_ref()))
        } else {
            match self {
                LocatorResult::Normal(locator) => Ok(locator.get_main_exe_path()),
                LocatorResult::Legacy(_, manifest) => {
                    if manifest.main_exe.is_empty() {
                        bail!("No main exe specified in manifest and exe name argument was not provided.");
                    } else {
                        Ok(Path::new(&current_dir).join(&manifest.main_exe))
                    }
                }
            }
        }
    }
}

fn legacy_locator(context: LocationContext) -> Result<LocatorResult> {
    let locator = locator::auto_locate_app_manifest(context);
    match locator {
        Ok(locator) => Ok(LocatorResult::Normal(locator)),
        Err(e) => {
            warn!("Failed to locate app manifest ({}), trying legacy package resolution...", e);
            let my_exe = std::env::current_exe()?;
            let parent_dir = my_exe.parent().expect("Unable to determine parent directory");
            let packages_dir = parent_dir.join("packages");
            if let Some((path, manifest)) = locator::find_latest_full_package(&packages_dir) {
                info!("Found full package to read: {:?}", path);
                Ok(LocatorResult::Legacy(parent_dir.to_path_buf(), manifest))
            } else {
                bail!("Unable to locate app manifest or full package in {:?}.", packages_dir);
            }
        }
    }
}

pub fn start_impl(
    context: LocationContext,
    exe_name: Option<&OsString>,
    exe_args: Option<Vec<OsString>>,
    legacy_args: Option<&OsString>,
) -> Result<()> {
    if legacy_args.is_some() && exe_args.is_some() {
        anyhow::bail!("Cannot use both legacy args and new args format at the same time.");
    }

    let locator = legacy_locator(context)?;
    let root_dir = locator.get_root_dir();
    let manifest = locator.get_manifest();
    if shared::has_app_prefixed_folder(&root_dir) {
        match try_legacy_migration(&root_dir, &manifest) {
            Ok(new_locator) => {
                shared::start_package(&new_locator, exe_args, Some(constants::HOOK_ENV_RESTART))?;
                Ok(())
            }
            Err(e) => {
                warn!("Failed to migrate legacy app ({}).", e);
                dialogs::show_error(
                    &manifest.title,
                    Some("Unable to start app"),
                    "This app installation has been corrupted and cannot be started. Please re-install the app.",
                );
                Err(e)
            }
        }
    } else {
        start_regular(locator, exe_name, exe_args, legacy_args)?;
        Ok(())
    }
}

fn start_regular(
    locator: LocatorResult,
    exe_name: Option<&OsString>,
    exe_args: Option<Vec<OsString>>,
    legacy_args: Option<&OsString>,
) -> Result<()> {
    // we can't just run the normal start_package command, because legacy squirrel might provide
    // an "exe name" to restart which no longer exists in the package
    let exe_to_execute = locator.get_exe_to_start(exe_name)?;
    if !exe_to_execute.exists() {
        bail!("Unable to find executable to start: '{:?}'", exe_to_execute);
    }

    let current = locator.get_current_dir();
    info!("About to launch: '{:?}' in dir '{:?}'", exe_to_execute, current);

    let mut cmd = Process::new(&exe_to_execute);
    cmd.current_dir(&current);

    if let Some(args) = exe_args {
        cmd.args(args);
    } else if let Some(args) = legacy_args {
        cmd.raw_arg(args);
    }

    let cmd = cmd.spawn()?;
    let _ = unsafe { AllowSetForegroundWindow(cmd.id()) };
    Ok(())
}

fn try_legacy_migration(root_dir: &PathBuf, manifest: &Manifest) -> Result<VelopackLocator> {
    info!("This is a legacy app. Will try and upgrade it now.");

    // if started by legacy Squirrel, the working dir of Update.exe may be inside the app-* folder,
    // meaning we can not clean up properly.
    std::env::set_current_dir(&root_dir)?;
    let path_config = locator::create_config_from_root_dir(root_dir);
    let package =
        locator::find_latest_full_package(&path_config.PackagesDir).ok_or_else(|| anyhow!("Unable to find latest full package."))?;

    warn!("This application is installed in a folder prefixed with 'app-'. Attempting to migrate...");
    let _ = shared::force_stop_package(&root_dir);

    // reset current manifest shortcuts, so when the new manifest is being read
    // new shortcuts will be force-created
    let mut modified_manifest = manifest.clone();
    modified_manifest.shortcut_locations = String::new();
    let locator = VelopackLocator::new_with_manifest(path_config, modified_manifest);
    let _mutex = locator.try_get_exclusive_lock()?;

    if !locator.get_current_bin_dir().exists() {
        info!("Renaming latest app-* folder to current.");
        if let Some((latest_app_dir, _latest_ver)) = shared::get_latest_app_version_folder(&root_dir)? {
            fs::rename(latest_app_dir, locator.get_current_bin_dir())?;
        }
    }

    info!("Removing old shortcuts...");
    win::remove_all_shortcuts_for_root_dir(&root_dir);

    info!("Applying latest full package...");
    let buf = Path::new(&package.0).to_path_buf();
    let new_locator = super::apply(&locator, false, OperationWait::NoWait, Some(&buf), None, false)?;

    info!("Removing old app-* folders...");
    shared::delete_app_prefixed_folders(&root_dir);
    let _ = remove_dir_all::remove_dir_all(root_dir.join("staging"));
    Ok(new_locator)
}
