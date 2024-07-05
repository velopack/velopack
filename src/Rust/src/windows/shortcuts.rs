use crate::bundle::Manifest;
use crate::shared as util;
use crate::windows::known_path as known;
use crate::windows::strings::*;
use anyhow::{anyhow, bail, Result};
use bitflags::bitflags;
use glob::glob;
use std::path;
use std::path::{Path, PathBuf};
use std::time::Duration;

use windows::core::{Interface, Result as WindowsResult, GUID, PCWSTR};
use windows::Win32::Foundation::HWND;
use windows::Win32::Storage::EnhancedStorage::PKEY_AppUserModel_ID;
use windows::Win32::System::Com::StructuredStorage::InitPropVariantFromStringVector;
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, IPersistFile, CLSCTX_ALL, CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE,
    STGM_READ,
};
use windows::Win32::UI::Shell::PropertiesSystem::IPropertyStore;
use windows::Win32::UI::Shell::{IShellItem, IShellLinkW, IStartMenuPinnedList, SHCreateItemFromParsingName, ShellLink, StartMenuPin};

// https://github.com/vaginessa/PWAsForFirefox/blob/fba68dbcc7ca27b970dc5a278ebdad32e0ab3c83/native/src/integrations/implementation/windows.rs#L28

bitflags! {
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
    struct ShortcutLocationFlags: u32 {
        const NONE = 0;
        const START_MENU = 1 << 0;
        const DESKTOP = 1 << 1;
        const STARTUP = 1 << 2;
        const START_MENU_ROOT = 1 << 4;
    }
}

impl ShortcutLocationFlags {
    fn from_string(input: &str) -> ShortcutLocationFlags {
        let mut flags = ShortcutLocationFlags::NONE;
        for part in input.split(|c| c == ',' || c == ';') {
            match part.trim().to_lowercase().as_str() {
                "none" => flags |= ShortcutLocationFlags::NONE,
                "startmenu" => flags |= ShortcutLocationFlags::START_MENU,
                "desktop" => flags |= ShortcutLocationFlags::DESKTOP,
                "startup" => flags |= ShortcutLocationFlags::STARTUP,
                "startmenuroot" => flags |= ShortcutLocationFlags::START_MENU_ROOT,
                _ => warn!("Warning: Unrecognized shortcut flag `{}`", part.trim()),
            }
        }
        flags
    }

    fn is_flag_set(&self, flag: ShortcutLocationFlags) -> bool {
        self.contains(flag)
    }
}

pub fn create_or_update_manifest_lnks(root_path: &PathBuf, next_app: &Manifest, previous_app: Option<&Manifest>) {
    todo!();
}

pub fn remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    todo!();
}

#[inline]
unsafe fn create_instance<T: Interface>(clsid: &GUID) -> WindowsResult<T> {
    CoCreateInstance(clsid, None, CLSCTX_ALL)
}

unsafe fn unsafe_update_app_manifest_lnks(root_path: &PathBuf, next_app: &Manifest, previous_app: Option<&Manifest>) {
    // let app = next_app.clone();
    // let current_path = app.get_current_path(root_path);
    // let main_exe_path = app.get_main_exe_path(root_path);

    let next_locations = ShortcutLocationFlags::from_string(&next_app.shortcut_locations);
    let prev_locations = if let Some(prev) = previous_app {
        ShortcutLocationFlags::from_string(&prev.shortcut_locations)
    } else {
        ShortcutLocationFlags::NONE
    };

    let next_locations = ShortcutLocationFlags::STARTUP | ShortcutLocationFlags::DESKTOP | ShortcutLocationFlags::START_MENU;
    let prev_locations = ShortcutLocationFlags::DESKTOP | ShortcutLocationFlags::START_MENU_ROOT;

    // get lnks that were deleted from the previous app
    let deleted_locations = prev_locations - next_locations;
    let same_locations = prev_locations & next_locations;
    let new_locations = next_locations - prev_locations;

    // let mut was_error = false;
    //
    // info!("Creating desktop shortcut...");
    // let desktop = known::get_user_desktop()?;
    // let desktop_lnk = Path::new(&desktop).join(format!("{}.lnk", &app.title));
    // if let Err(e) = _create_lnk(&desktop_lnk.to_string_lossy(), &main_exe_path, &current_path, None) {
    //     warn!("Failed to create start menu shortcut: {}", e);
    //     was_error = true;
    // }
    //
    // std::thread::sleep(Duration::from_millis(1));
    //
    // info!("Creating start menu shortcut...");
    // let startmenu = known::get_start_menu()?;
    // let start_lnk = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", &app.title));
    // if let Err(e) = _create_lnk(&start_lnk.to_string_lossy(), &main_exe_path, &current_path, None) {
    //     warn!("Failed to create start menu shortcut: {}", e);
    //     was_error = true;
    // }
    //
    // if was_error {
    //     bail!("Failed to create one or both default shortcuts");
    // }
    //
    // Ok(())
}

unsafe fn unsafe_create_new_lnk(output: &str, target: &str, work_dir: &str, app_model_id: Option<String>) -> WindowsResult<()> {
    let link: IShellLinkW = create_instance(&ShellLink)?;

    let target = string_to_u16(target);
    let target = PCWSTR(target.as_ptr());
    let work_dir = string_to_u16(work_dir);
    let work_dir = PCWSTR(work_dir.as_ptr());
    let output = string_to_u16(output);
    let output = PCWSTR(output.as_ptr());

    unsafe {
        link.SetPath(target)?;
        link.SetWorkingDirectory(work_dir)?;

        // Set app user model ID property
        // Docs: https://docs.microsoft.com/windows/win32/properties/props-system-appusermodel-id
        if let Some(app_model_id) = app_model_id {
            let store: IPropertyStore = link.cast()?;
            let id = string_to_u16(app_model_id);
            let id = PCWSTR(id.as_ptr());
            let variant = InitPropVariantFromStringVector(Some(&[id]))?;
            store.SetValue(&PKEY_AppUserModel_ID, &variant)?;
            store.Commit()?;
        }

        // Save shortcut to file
        let persist: IPersistFile = link.cast()?;

        persist.Save(output, true)?;
    }

    Ok(())
}

unsafe fn unsafe_resolve_lnk(link_path: &str) -> Result<(String, String)> {
    let link: IShellLinkW = create_instance(&ShellLink)?;
    let persist: IPersistFile = link.cast()?;

    debug!("Loading link: {}", link_path);

    let link_pcwstr = string_to_u16(link_path);
    let link_pcwstr = PCWSTR(link_pcwstr.as_ptr());

    persist.Load(link_pcwstr, STGM_READ)?;
    let flags = 1 | 2 | 1 << 16;
    if let Err(e) = link.Resolve(HWND(0), flags) {
        // this happens if the target path is missing and the link is broken
        warn!("Failed to resolve link {} ({:?})", link_path, e);
    }

    let mut pszfile = [0u16; 260];
    let mut pszdir = [0u16; 260];
    link.GetPath(&mut pszfile, std::ptr::null_mut(), 0)?;
    link.GetWorkingDirectory(&mut pszdir)?;
    Ok((u16_to_string(pszfile)?, u16_to_string(pszdir)?))
}

unsafe fn unsafe_remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref();
    info!("Searching for shortcuts containing root: '{}'", root_dir.to_string_lossy());

    let search_paths = vec![
        format!("{}/*.lnk", known::get_user_desktop()?),
        format!("{}/*.lnk", known::get_startup()?),
        format!("{}/**/*.lnk", known::get_start_menu()?),
        format!("{}/**/*.lnk", known::get_user_pinned()?),
    ];

    for search_glob in search_paths {
        info!("Searching for shortcuts in: '{}'", search_glob);
        if let Ok(paths) = glob(&search_glob) {
            for path in paths {
                if let Ok(path) = path {
                    trace!("Checking shortcut: '{}'", path.to_string_lossy());
                    let res = unsafe_resolve_lnk(&path.to_string_lossy());
                    trace!("    Shortcut resolved: '{:?}'", res);
                    if let Ok((target, work_dir)) = res {
                        let target_match = super::is_sub_path(&target, root_dir).unwrap_or(false);
                        let work_dir_match = super::is_sub_path(&work_dir, root_dir).unwrap_or(false);
                        if target_match || work_dir_match {
                            let mstr = if target_match && work_dir_match {
                                format!("both target ({}) and work dir ({})", target, work_dir)
                            } else if target_match {
                                format!("target ({})", target)
                            } else {
                                format!("work dir ({})", work_dir)
                            };
                            warn!("Removing shortcut '{}' because {} matched.", path.to_string_lossy(), mstr);
                            if let Err(e) = unsafe_delete_lnk_file(&path) {
                                warn!("Failed to remove shortcut: {}", e);
                            }
                        }
                    }
                }
            }
        }
    }

    Ok(())
}

unsafe fn unsafe_delete_lnk_file<P: AsRef<Path>>(path: P) -> Result<()> {
    let path = path.as_ref();
    if !path.exists() {
        return Ok(());
    }

    if let Err(e) = unsafe_unpin_lnk_from_start(path) {
        warn!("Failed to unpin lnk from start menu: {}", e);
    }

    util::retry_io(|| std::fs::remove_file(&path))?;

    // if the parent directory is empty, remove it as well
    if let Some(parent_path) = path.parent() {
        if let Ok(entries) = parent_path.read_dir() {
            if entries.count() == 0 {
                util::retry_io(|| std::fs::remove_dir(&parent_path))?;
            }
        }
    }

    Ok(())
}

// https://github.com/velopack/velopack/issues/100
// https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nf-shobjidl-istartmenupinnedlist-removefromlist
unsafe fn unsafe_unpin_lnk_from_start<P: AsRef<Path>>(path: P) -> Result<()> {
    let path = path.as_ref();
    if !path.exists() {
        return Ok(());
    }

    let path = string_to_u16(path.to_string_lossy());
    let item_result: IShellItem = SHCreateItemFromParsingName(PCWSTR(path.as_ptr()), None)?;
    let pinned_list: IStartMenuPinnedList = create_instance(&StartMenuPin)?;
    pinned_list.RemoveFromList(&item_result)?;
    Ok(())
}

unsafe fn unsafe_run_delegate_in_com_context<T, F>(delegate: F) -> Result<T>
where
    T: Send + 'static,
    F: FnOnce() -> Result<T> + Send + 'static,
{
    let t = std::thread::spawn(move || {
        let hr = unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE) };
        if hr.is_err() {
            return Err(anyhow!(hr));
        }
        std::thread::sleep(Duration::from_millis(1));
        delegate()
    });
    t.join().unwrap()
}

#[test]
#[ignore]
fn test_unpin_shortcut() {
    unsafe {
        unsafe_run_delegate_in_com_context(|| {
            let path = r"C:\Users\Caelan\Desktop\Discord.lnk";
            unsafe_unpin_lnk_from_start(path)?;
            Ok(())
        })
        .unwrap();
    }
}

#[test]
#[ignore]
fn test_shortcut_intense_intermittent() {
    let startmenu = known::get_start_menu().unwrap();
    let lnk_path = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", "veloshortcuttest"));
    let target = "C:\\Users\\Caelan\\AppData\\Local\\Discord\\Update.exe";
    let work = "C:\\Users\\Caelan\\AppData\\Local\\Discord";

    let mut i = 0;
    while i < 100 {
        unsafe {
            let p1 = lnk_path.to_string_lossy().to_string().clone();
            unsafe_run_delegate_in_com_context(move || {
                unsafe_create_new_lnk(&p1, &target, &work, None)?;
                Ok(())
            })
            .unwrap();
        }
        assert!(lnk_path.exists());
        util::retry_io(|| std::fs::remove_file(&lnk_path)).unwrap();
        assert!(!lnk_path.exists());
        i += 1;
    }
}

#[test]
fn bitwise_flags_add_subtract_correctly() {
    let prev_locations = ShortcutLocationFlags::DESKTOP | ShortcutLocationFlags::START_MENU_ROOT;
    let next_locations = ShortcutLocationFlags::STARTUP | ShortcutLocationFlags::DESKTOP | ShortcutLocationFlags::START_MENU;

    let deleted_locations = prev_locations - next_locations;
    let same_locations = prev_locations & next_locations;
    let new_locations = next_locations - prev_locations;

    assert_eq!(deleted_locations, ShortcutLocationFlags::START_MENU_ROOT);
    assert_eq!(same_locations, ShortcutLocationFlags::DESKTOP);
    assert_eq!(new_locations, ShortcutLocationFlags::STARTUP | ShortcutLocationFlags::START_MENU);
}

#[test]
#[ignore]
fn test_can_resolve_existing_shortcut() {
    let link_path = r"C:\Users\Caelan\Desktop\Discord.lnk";

    unsafe {
        unsafe_run_delegate_in_com_context(move || {
            let (target, _workdir) = unsafe_resolve_lnk(link_path).unwrap();
            assert_eq!(target, "C:\\Users\\Caelan\\AppData\\Local\\Discord\\Update.exe");
            Ok(())
        })
        .unwrap();
    }
}

#[test]
fn shortcut_full_integration_test() {
    let desktop = known::get_user_desktop().unwrap();
    let link_location = Path::new(&desktop).join("testclowd123hi.lnk");
    let target = r"C:\Users\Caelan\AppData\Local\NonExistingAppHello123\current\HelloWorld.exe";
    let work = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123\current";
    let root = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123";

    unsafe {
        unsafe_run_delegate_in_com_context(move || {
            unsafe_create_new_lnk(&link_location.to_string_lossy(), target, work, None).unwrap();
            assert!(link_location.exists());

            let (target_out, work_out) = unsafe_resolve_lnk(&link_location.to_string_lossy()).unwrap();
            assert_eq!(target_out, target);
            assert_eq!(work_out, work);

            unsafe_remove_all_shortcuts_for_root_dir(root).unwrap();
            assert!(!link_location.exists());
            Ok(())
        })
        .unwrap();
    }
}
