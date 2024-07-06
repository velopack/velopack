use std::path::{Path, PathBuf};
use std::time::Duration;

use anyhow::{anyhow, bail, Result};
use bitflags::bitflags;
use glob::glob;
use windows::core::{Interface, GUID, PCWSTR};
use windows::Win32::Foundation::HWND;
use windows::Win32::Storage::EnhancedStorage::PKEY_AppUserModel_ID;
use windows::Win32::System::Com::StructuredStorage::InitPropVariantFromStringVector;
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, IPersistFile, CLSCTX_ALL, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE, STGM_READ, STGM_READWRITE,
};
use windows::Win32::UI::Shell::PropertiesSystem::IPropertyStore;
use windows::Win32::UI::Shell::{IShellItem, IShellLinkW, IStartMenuPinnedList, SHCreateItemFromParsingName, ShellLink, StartMenuPin};

use crate::bundle::Manifest;
use crate::shared as util;
use crate::windows::{known_path as known, strings::*};

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
}

// Get-StartApps | sort-object -property name

pub fn create_or_update_manifest_lnks<P: AsRef<Path>>(root_path: P, next_app: &Manifest, previous_app: Option<&Manifest>) {
    let root_path = root_path.as_ref().to_owned().to_path_buf();
    let next_app = next_app.clone();
    let previous_app = previous_app.cloned();

    unsafe {
        if let Err(e) = unsafe_run_delegate_in_com_context(move || {
            unsafe_update_app_manifest_lnks(&root_path, &next_app, previous_app.as_ref())?;
            Ok(())
        }) {
            warn!("Failed to update shortcuts: {}", e);
        }
    }
}

pub fn remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) {
    let root_dir = root_dir.as_ref().to_owned().to_path_buf();
    unsafe {
        if let Err(e) = unsafe_run_delegate_in_com_context(move || {
            unsafe_remove_all_shortcuts_for_root_dir(&root_dir)?;
            Ok(())
        }) {
            warn!("Failed to remove shortcuts: {}", e);
        }
    }
}

#[inline]
unsafe fn create_instance<T: Interface>(clsid: &GUID) -> Result<T> {
    Ok(CoCreateInstance(clsid, None, CLSCTX_ALL)?)
}

fn get_path_for_shortcut_location(app: &Manifest, flag: ShortcutLocationFlags) -> Result<PathBuf> {
    let shortcut_file_name = if app.title.is_empty() { app.id.clone() } else { app.title.clone() };
    let shortcut_file_name = shortcut_file_name + ".lnk";
    match flag {
        ShortcutLocationFlags::DESKTOP => Ok(Path::new(&known::get_user_desktop()?).join(shortcut_file_name)),
        ShortcutLocationFlags::STARTUP => Ok(Path::new(&known::get_startup()?).join(shortcut_file_name)),
        ShortcutLocationFlags::START_MENU_ROOT => Ok(Path::new(&known::get_start_menu()?).join(shortcut_file_name)),
        ShortcutLocationFlags::START_MENU => {
            if app.authors.is_empty() {
                warn!("No authors specified and START_MENU shortcut specified. Using START_MENU_ROOT instead.");
                Ok(Path::new(&known::get_start_menu()?).join(shortcut_file_name))
            } else {
                Ok(Path::new(&known::get_start_menu()?).join(&app.authors).join(shortcut_file_name))
            }
        }
        _ => bail!("Invalid shortcut location flag: {:?}", flag),
    }
}

unsafe fn unsafe_update_app_manifest_lnks(root_path: &PathBuf, next_app: &Manifest, previous_app: Option<&Manifest>) -> Result<()> {
    let next_locations = ShortcutLocationFlags::from_string(&next_app.shortcut_locations);
    let prev_locations = if let Some(prev) = previous_app {
        ShortcutLocationFlags::from_string(&prev.shortcut_locations)
    } else {
        ShortcutLocationFlags::NONE
    };

    info!("Shortcut Previous locations: {:?} ({:?})", prev_locations, previous_app.map(|a| a.version.clone()));
    info!("Shortcut Next locations: {:?} ({:?})", next_locations, next_app.version);

    // get lnks that were deleted from the previous app
    let deleted_locations = prev_locations - next_locations;
    let same_locations = prev_locations & next_locations;
    let new_locations = next_locations - prev_locations;

    info!("Shortcut Deleted locations: {:?}", deleted_locations);
    info!("Shortcut Same locations: {:?}", same_locations);
    info!("Shortcut New locations: {:?}", new_locations);

    let current_shortcuts = unsafe_get_shortcuts_for_root_dir(next_app.get_current_path(root_path))?;
    info!("Current shortcuts: {:?}", current_shortcuts);

    let mut app_model_id: Option<String> = None;
    if !next_app.shortcut_amuid.is_empty() {
        app_model_id = Some(next_app.shortcut_amuid.clone());
    }

    let mut visited = vec![];

    // delete removed shortcut locations
    for (flag, path) in current_shortcuts.iter() {
        if deleted_locations.contains(flag.to_owned()) {
            info!("Removing shortcut for deleted flag '{:?}' ({:?}).", path, flag);
            if let Err(e) = unsafe_delete_lnk_file(&path) {
                warn!("Failed to remove shortcut: {}", e);
            }
        }
        visited.push(path.to_owned());
    }

    // add new shortcut locations
    for flag in new_locations.iter() {
        let path = get_path_for_shortcut_location(next_app, flag)?;
        let target = next_app.get_main_exe_path(root_path);
        let work_dir = next_app.get_current_path(root_path);
        info!("Creating new shortcut for flag '{:?}' ({:?}).", path, flag);
        if let Err(e) = unsafe_create_new_lnk(&path.to_string_lossy(), &target, &work_dir, app_model_id.as_deref()) {
            warn!("Failed to create shortcut: {}", e);
        }
        visited.push(path);
    }

    // for shortcut locations in both previous and next app, we should compare the file path of the shortcuts
    // given previous_app and next_app properties, potentially renaming shortcuts if this path has changed3
    for flag in same_locations.iter() {
        let resolved_previous_app = previous_app.ok_or(anyhow!("Invalid state - no previous app metadata available"))?;
        let old_path = get_path_for_shortcut_location(resolved_previous_app, flag)?;
        let new_path = get_path_for_shortcut_location(next_app, flag)?;

        if old_path != new_path {
            info!("Renaming shortcut from '{}' to '{}'", old_path.to_string_lossy(), new_path.to_string_lossy());
            std::fs::rename(&old_path, &new_path)?;
        }

        info!("Updating existing shortcut for flag '{:?}' ({:?}).", new_path, flag);

        unsafe_update_existing_lnk(
            &new_path.to_string_lossy(),
            &next_app.get_main_exe_path(root_path),
            &next_app.get_current_path(root_path),
            app_model_id.as_deref(),
        )?;

        visited.push(old_path);
        visited.push(new_path);
    }

    // remove any remaining shortcuts that were not visited AND resolve to an invalid target
    for (_, path) in current_shortcuts.iter() {
        if !visited.contains(path) {
            info!("Checking shortcut for valid paths: '{}'", path.to_string_lossy());
            if let Ok((target, work_dir)) = unsafe_resolve_lnk(&path.to_string_lossy()) {
                if !Path::new(&target).exists() || !Path::new(&work_dir).exists() {
                    warn!("Removing invalid/broken shortcut: '{}'", path.to_string_lossy());
                    if let Err(e) = unsafe_delete_lnk_file(&path) {
                        warn!("Failed to remove shortcut: {}", e);
                    }
                }
            }
        }
    }

    Ok(())
}

unsafe fn unsafe_create_new_lnk(output_path: &str, target: &str, work_dir: &str, app_model_id: Option<&str>) -> Result<()> {
    let link: IShellLinkW = create_instance(&ShellLink)?;

    let target = string_to_u16(target);
    let target = PCWSTR(target.as_ptr());
    let work_dir = string_to_u16(work_dir);
    let work_dir = PCWSTR(work_dir.as_ptr());
    let output = string_to_u16(output_path);
    let output = PCWSTR(output.as_ptr());

    link.SetPath(target)?;
    link.SetIconLocation(target, 0)?;
    link.SetWorkingDirectory(work_dir)?;
    unsafe_set_app_model_id(app_model_id, &link)?;

    // Save shortcut to file
    let persist: IPersistFile = link.cast()?;
    persist.Save(output, true)?;

    Ok(())
}

unsafe fn unsafe_set_app_model_id(app_model_id: Option<&str>, link: &IShellLinkW) -> Result<()> {
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
    Ok(())
}

const SLR_NO_UI: u32 = 1;
const SLR_ANY_MATCH: u32 = 2;
const TIMEOUT_1MS: u32 = 1 << 16;

unsafe fn unsafe_update_existing_lnk(link_path: &str, target: &str, work_dir: &str, app_model_id: Option<&str>) -> Result<()> {
    let link: IShellLinkW = create_instance(&ShellLink)?;
    let persist: IPersistFile = link.cast()?;
    debug!("Loading link: {}", link_path);

    let link_pcwstr = string_to_u16(link_path);
    let link_pcwstr = PCWSTR(link_pcwstr.as_ptr());

    persist.Load(link_pcwstr, STGM_READWRITE)?;

    let flags = SLR_NO_UI | SLR_ANY_MATCH | TIMEOUT_1MS;
    if let Err(e) = link.Resolve(HWND(0), flags) {
        // this happens if the target path is missing and the link is broken
        warn!("Failed to resolve link {} ({:?})", link_path, e);
    }

    let target = string_to_u16(target);
    let target = PCWSTR(target.as_ptr());
    let work_dir = string_to_u16(work_dir);
    let work_dir = PCWSTR(work_dir.as_ptr());

    link.SetPath(target)?;
    link.SetIconLocation(target, 0)?;
    link.SetWorkingDirectory(work_dir)?;
    unsafe_set_app_model_id(app_model_id, &link)?;

    // Save shortcut to file
    let persist: IPersistFile = link.cast()?;
    persist.Save(None, None)?;

    Ok(())
}

unsafe fn unsafe_resolve_lnk(link_path: &str) -> Result<(String, String)> {
    let link: IShellLinkW = create_instance(&ShellLink)?;
    let persist: IPersistFile = link.cast()?;
    debug!("Loading link: {}", link_path);

    let link_pcwstr = string_to_u16(link_path);
    let link_pcwstr = PCWSTR(link_pcwstr.as_ptr());

    persist.Load(link_pcwstr, STGM_READ)?;

    let flags = SLR_NO_UI | SLR_ANY_MATCH | TIMEOUT_1MS;
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

unsafe fn unsafe_get_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<Vec<(ShortcutLocationFlags, PathBuf)>> {
    let root_dir = root_dir.as_ref();
    info!("Searching for shortcuts containing root: '{}'", root_dir.to_string_lossy());

    let search_paths = vec![
        (ShortcutLocationFlags::DESKTOP, format!("{}/*.lnk", known::get_user_desktop()?)),
        (ShortcutLocationFlags::STARTUP, format!("{}/*.lnk", known::get_startup()?)),
        (ShortcutLocationFlags::START_MENU_ROOT, format!("{}/*.lnk", known::get_start_menu()?)),
        (ShortcutLocationFlags::START_MENU, format!("{}/**/*.lnk", known::get_start_menu()?)),
    ];

    let mut paths: Vec<(ShortcutLocationFlags, PathBuf)> = Vec::new();
    for (flag, search_glob) in search_paths {
        info!("Searching for shortcuts in: {:?} ({})", flag, search_glob);
        if let Ok(glob_paths) = glob(&search_glob) {
            for path in glob_paths.filter_map(Result::ok) {
                trace!("Checking shortcut: '{}'", path.to_string_lossy());
                let res = unsafe_resolve_lnk(&path.to_string_lossy());
                trace!("    Shortcut resolved: '{:?}'", res);
                if let Ok((target, work_dir)) = res {
                    let target_match = super::is_sub_path(&target, root_dir).unwrap_or(false);
                    let work_dir_match = super::is_sub_path(&work_dir, root_dir).unwrap_or(false);
                    if target_match || work_dir_match {
                        let match_str = if target_match && work_dir_match {
                            format!("both target ({}) and work dir ({})", target, work_dir)
                        } else if target_match {
                            format!("target ({})", target)
                        } else {
                            format!("work dir ({})", work_dir)
                        };

                        if flag == ShortcutLocationFlags::START_MENU {
                            // if there is already a matching 'path' in the list (e.g. in Start_Menu_Root)
                            // then we should continue/not add it again here
                            if paths.iter().any(|(_, p)| p == &path) {
                                continue;
                            }
                        }

                        warn!("Selected shortcut '{}' because {} matched.", path.to_string_lossy(), match_str);
                        paths.push((flag, path));
                    }
                }
            }
        }
    }

    Ok(paths)
}

unsafe fn unsafe_remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let shortcuts = unsafe_get_shortcuts_for_root_dir(root_dir)?;
    for (flag, path) in shortcuts {
        warn!("Removing shortcut '{}' ({:?}).", path.to_string_lossy(), flag);
        if let Err(e) = unsafe_delete_lnk_file(&path) {
            warn!("Failed to remove shortcut: {}", e);
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
fn test_bitwise_flags_add_subtract_correctly() {
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
fn test_shortcut_full_integration() {
    unsafe {
        unsafe_run_delegate_in_com_context(move || {
            let desktop = known::get_user_desktop().unwrap();
            let start_menu = known::get_start_menu().unwrap();
            let start_menu_subfolder = Path::new(&start_menu).join("TestVelopackTest1234");
            let target = r"C:\Users\Caelan\AppData\Local\NonExistingAppHello123\current\HelloWorld.exe";
            let work = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123\current";
            let root = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123";

            let link1 = Path::new(&desktop).join("testclowd123hi.lnk");
            let link2 = Path::new(&start_menu).join("testclowd123hi.lnk");
            let link3 = start_menu_subfolder.join("testclowd123hi.lnk");

            std::fs::remove_dir_all(&start_menu_subfolder).unwrap_or_default();
            std::fs::remove_file(&link1).unwrap_or_default();
            std::fs::remove_file(&link2).unwrap_or_default();
            std::fs::remove_file(&link3).unwrap_or_default();

            assert!(!link1.exists());
            assert!(!link2.exists());
            assert!(!link3.exists());
            assert!(!start_menu_subfolder.exists());

            std::fs::create_dir_all(&start_menu_subfolder).unwrap();
            assert!(start_menu_subfolder.exists());

            let link1_str = link1.to_string_lossy();
            let link2_str = link2.to_string_lossy();
            let link3_str = link3.to_string_lossy();

            unsafe_create_new_lnk(&link1_str, target, work, None).unwrap();
            unsafe_create_new_lnk(&link2_str, target, work, None).unwrap();
            unsafe_create_new_lnk(&link3_str, target, work, None).unwrap();

            assert!(link1.exists());
            assert!(link2.exists());
            assert!(link3.exists());

            let shortcuts = unsafe_get_shortcuts_for_root_dir(&root).unwrap();
            assert_eq!(shortcuts.len(), 3);
            assert_eq!(shortcuts[0].0, ShortcutLocationFlags::DESKTOP);
            assert_eq!(shortcuts[0].1, link1);
            assert_eq!(shortcuts[1].0, ShortcutLocationFlags::START_MENU_ROOT);
            assert_eq!(shortcuts[1].1, link2);
            assert_eq!(shortcuts[2].0, ShortcutLocationFlags::START_MENU);
            assert_eq!(shortcuts[2].1, link3);

            let (target_out, work_out) = unsafe_resolve_lnk(&link1_str).unwrap();
            assert_eq!(target_out, target);
            assert_eq!(work_out, work);

            unsafe_remove_all_shortcuts_for_root_dir(root).unwrap();
            assert!(!link1.exists());
            assert!(!link2.exists());
            assert!(!link3.exists());
            assert!(!start_menu_subfolder.exists());
            Ok(())
        })
        .unwrap();
    }
}
