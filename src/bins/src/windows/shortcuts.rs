use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::Duration;

use anyhow::{anyhow, bail, Result};
use glob::glob;
use same_file::is_same_file;
use velopack::{locator::ShortcutLocationFlags, locator::VelopackLocator};
use windows::core::{Interface, GUID, PCWSTR, PROPVARIANT};
use windows::Win32::Storage::EnhancedStorage::PKEY_AppUserModel_ID;
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, CoUninitialize, IPersistFile, StructuredStorage::InitPropVariantFromStringVector,
    CLSCTX_ALL, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE, STGM_READWRITE,
};
use windows::Win32::UI::Shell::{
    IShellItem, IShellLinkW, IStartMenuPinnedList, PropertiesSystem::IPropertyStore, SHCreateItemFromParsingName, ShellLink, StartMenuPin,
};

use crate::shared as util;
use crate::windows::{known_path as known, strings::*};

// https://github.com/vaginessa/PWAsForFirefox/blob/fba68dbcc7ca27b970dc5a278ebdad32e0ab3c83/native/src/integrations/implementation/windows.rs#L28

pub fn create_or_update_manifest_lnks(next_app: &VelopackLocator, previous_app: Option<&VelopackLocator>) {
    let next_app = next_app.clone();
    let previous_app = previous_app.cloned();
    unsafe {
        if let Err(e) = unsafe_run_delegate_in_com_context(move || {
            unsafe_update_app_manifest_lnks(&next_app, previous_app.as_ref())?;
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

fn get_shortcut_filename(app_id: &str, app_title: &str) -> String {
    let name = if app_title.is_empty() { app_id.to_owned() } else { app_title.to_owned() };
    let shortcut_file_name = name + ".lnk";
    shortcut_file_name
}

fn get_path_for_shortcut_location(app_id: &str, app_title: &str, app_author: &str, flag: ShortcutLocationFlags) -> Result<PathBuf> {
    let shortcut_file_name = get_shortcut_filename(app_id, app_title);
    match flag {
        ShortcutLocationFlags::DESKTOP => Ok(Path::new(&known::get_user_desktop()?).join(shortcut_file_name)),
        ShortcutLocationFlags::STARTUP => Ok(Path::new(&known::get_startup()?).join(shortcut_file_name)),
        ShortcutLocationFlags::START_MENU_ROOT => Ok(Path::new(&known::get_start_menu()?).join(shortcut_file_name)),
        ShortcutLocationFlags::START_MENU => {
            if app_author.is_empty() {
                warn!("No authors specified and START_MENU shortcut specified. Using START_MENU_ROOT instead.");
                Ok(Path::new(&known::get_start_menu()?).join(shortcut_file_name))
            } else {
                Ok(Path::new(&known::get_start_menu()?).join(app_author).join(shortcut_file_name))
            }
        }
        _ => bail!("Invalid shortcut location flag: {:?}", flag),
    }
}

unsafe fn unsafe_update_app_manifest_lnks(next_app: &VelopackLocator, previous_app: Option<&VelopackLocator>) -> Result<()> {
    let next_locations = next_app.get_manifest_shortcut_locations();
    let prev_locations = previous_app.map(|a| a.get_manifest_shortcut_locations()).unwrap_or(ShortcutLocationFlags::NONE);
    
    info!("Shortcut Previous Locations: {:?} ({:?})", prev_locations, previous_app.map(|a| a.get_manifest_version_full_string()));
    info!("Shortcut Next Locations: {:?} ({:?})", next_locations, next_app.get_manifest_version_full_string());

    // we must end with shortcuts which exist in the next app but not the previous app.
    // any shortcuts which exist in both are optional - they could have been deleted by the user,
    // and we do not want to re-create them.
    let mut new_locations = next_locations - prev_locations;

    if new_locations.contains(ShortcutLocationFlags::START_MENU_ROOT) && new_locations.contains(ShortcutLocationFlags::START_MENU) {
        // if both start menu locations are specified, we prefer ROOT.
        new_locations.remove(ShortcutLocationFlags::START_MENU);
    }
    let root_path = next_app.get_root_dir();
    let app_id = next_app.get_manifest_id();
    let app_title = next_app.get_manifest_title();
    let app_authors = next_app.get_manifest_authors();
    let app_model_id: Option<String> = next_app.get_manifest_shortcut_amuid();
    let app_main_exe = next_app.get_main_exe_path_as_string();
    let app_work_dir = next_app.get_current_bin_dir_as_string();

    info!("App Model ID: {:?}", app_model_id);
    let mut current_shortcuts = unsafe_get_shortcuts_for_root_dir(root_path)?;

    // update all existing shortcuts, verify target/workdir/amuid and icon is correct.
    info!("Will update all current shortcuts: {:?}", current_shortcuts);

    for (flag, lnk) in current_shortcuts.iter_mut() {
        let flag = flag.to_owned();
        info!("Updating existing shortcut '{:?}' ({:?}).", lnk.get_link_path(), flag);

        let target_option = lnk.get_target_path().ok();

        // set the target path to the main exe if it is missing or incorrect
        if target_option.is_none() || !PathBuf::from(target_option.unwrap()).exists() {
            warn!("Shortcut {} target does not exist, updating to mainExe and setting workdir to current.", lnk.get_link_path());
            if let Err(e) = lnk.set_target_path(&app_main_exe) {
                warn!("Failed to update shortcut target: {}", e);
            }
            if let Err(e) = lnk.set_working_directory(&app_work_dir) {
                warn!("Failed to update shortcut working directory: {}", e);
            }
        }

        // force icon refresh by resetting the icon location
        if let Err(e) = lnk.set_icon_location(&app_main_exe, 0) {
            warn!("Failed to update shortcut icon location: {}", e);
        }

        if let Err(e) = lnk.set_aumid(app_model_id.as_deref()) {
            warn!("Failed to update shortcut app model ID: {}", e);
        }

        if let Err(e) = lnk.save() {
            warn!("Failed to save shortcut: {}", e);
        }

        // if there is a shortcut in start menu or root, we do not want to create a new one anywhere
        if flag == ShortcutLocationFlags::START_MENU_ROOT || flag == ShortcutLocationFlags::START_MENU {
            new_locations.remove(ShortcutLocationFlags::START_MENU_ROOT);
            new_locations.remove(ShortcutLocationFlags::START_MENU);
        } else {
            new_locations.remove(flag.to_owned());
        }
    }

    // rename existing shortcuts if packTitle has changed
    let last_app_name = previous_app.map(|a| a.get_manifest_title()).unwrap_or(app_title.clone());
    let shortcuts_to_rename = unsafe_find_best_rename_candidates(&last_app_name, &app_main_exe, current_shortcuts);
    for (flag, path) in shortcuts_to_rename {
        let shortcut_file_name = get_shortcut_filename(&app_id, &app_title);

        let target_path = if let Some(parent) = path.parent() {
            parent.join(shortcut_file_name)
        } else {
            get_path_for_shortcut_location(&app_id, &app_title, &app_authors, flag)?
        };

        if path != target_path {
            info!("Renaming shortcut from '{:?}' to '{:?}'.", path, target_path);
            if let Err(e) = std::fs::rename(&path, &target_path) {
                warn!("Failed to rename shortcut: {}", e);
            }
        }
    }

    // add new (missing) shortcut locations
    for flag in new_locations.iter() {
        let path = get_path_for_shortcut_location(&app_id, &app_title, &app_authors, flag)?;
        info!("Creating new shortcut for flag '{:?}' ({:?}).", path, flag);

        match Lnk::create_new() {
            Ok(mut lnk) => {
                if let Err(e) = lnk.set_target_path(&app_main_exe) {
                    warn!("Failed to set target path: {}", e);
                    break;
                }

                if let Err(e) = lnk.set_working_directory(&app_work_dir) {
                    warn!("Failed to set working directory: {}", e);
                    break;
                }

                if let Err(e) = lnk.set_aumid(app_model_id.as_deref()) {
                    warn!("Failed to set app model ID: {}", e);
                    break;
                }

                if let Err(e) = lnk.set_icon_location(&app_main_exe, 0) {
                    warn!("Failed to set icon location: {}", e);
                    break;
                }

                if let Err(e) = lnk.save_as(&path.to_string_lossy()) {
                    warn!("Failed to save shortcut: {}", e);
                    break;
                }
            }
            Err(e) => {
                warn!("Failed to create shortcut: {}", e);
            }
        }
    }

    Ok(())
}

unsafe fn unsafe_find_best_rename_candidates<P: AsRef<Path>>(
    app_name: &str,
    target_path: P,
    current_shortcuts: Vec<(ShortcutLocationFlags, Lnk)>,
) -> HashMap<ShortcutLocationFlags, PathBuf> {
    use strsim::jaro_winkler;

    // group shortcuts by location flag
    let mut groups: HashMap<ShortcutLocationFlags, Vec<Lnk>> = HashMap::new();
    for (enum_val, link) in current_shortcuts.iter() {
        // filter out shortcuts which have custom arguments
        if let Ok(args) = link.get_arguments() {
            if !args.is_empty() {
                continue;
            }
        }

        // filter out shortcuts in user-pinned dir, we're not allowed to rename those
        if enum_val == &ShortcutLocationFlags::USER_PINNED {
            continue;
        }

        // filter out shortcuts which do not point to our main_exe
        if is_same_file(&target_path, PathBuf::from(link.get_target_path().unwrap_or_default())).unwrap_or(false) {
            groups.entry(enum_val.to_owned()).or_insert_with(Vec::new).push(link.clone());
        }
    }

    // Determine the best matching PathBuf for each group
    let mut best_matches: HashMap<ShortcutLocationFlags, PathBuf> = HashMap::new();

    for (key, lnk_arr) in groups {
        let mut best_path: Option<(PathBuf, f64)> = None;
        for lnk in lnk_arr {
            let file_path = PathBuf::from(&lnk.my_path);
            if let Some(filename) = file_path.file_name().and_then(|n| n.to_str()) {
                // we use jaro winkler distance to determine the best match because it
                // gives an advantage to strings which share the same prefix
                let score = jaro_winkler(filename, app_name);
                if best_path.is_none() || best_path.as_ref().unwrap().1 < score {
                    best_path = Some((file_path, score));
                }
            }
        }

        if let Some(best) = best_path {
            best_matches.insert(key, best.0);
        }
    }

    best_matches
}

unsafe fn unsafe_get_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<Vec<(ShortcutLocationFlags, Lnk)>> {
    let root_dir = root_dir.as_ref();
    info!("Searching for shortcuts containing root: '{}'", root_dir.to_string_lossy());
    
    let mut search_paths = Vec::new();
    
    match known::get_user_desktop() {
        Ok(user_desktop) => search_paths.push((ShortcutLocationFlags::DESKTOP, format!("{}/*.lnk", user_desktop))),
        Err(e) => warn!("Failed to get user desktop directory, it will not be searched: {}", e),
    } 
    
    match known::get_startup() {
        Ok(startup) => search_paths.push((ShortcutLocationFlags::STARTUP, format!("{}/*.lnk", startup))),
        Err(e) => warn!("Failed to get startup directory, it will not be searched: {}", e),
    }
    
    match known::get_start_menu() {
        // this handles START_MENU and START_MENU_ROOT
        Ok(start_menu) => search_paths.push((ShortcutLocationFlags::START_MENU, format!("{}/**/*.lnk", start_menu))),
        Err(e) => warn!("Failed to get start menu directory, it will not be searched: {}", e),
    }
    
    match known::get_user_pinned() {
        Ok(user_pinned) => search_paths.push((ShortcutLocationFlags::USER_PINNED, format!("{}/**/*.lnk", user_pinned))),
        Err(e) => warn!("Failed to get user pinned directory, it will not be searched: {}", e),
    }
    
    let mut paths: Vec<(ShortcutLocationFlags, Lnk)> = Vec::new();
    for (flag, search_glob) in search_paths {
        info!("Searching for shortcuts in: {:?} ({})", flag, search_glob);
        if let Ok(glob_paths) = glob(&search_glob) {
            for path in glob_paths.filter_map(Result::ok) {
                trace!("Checking shortcut: '{:?}'", path);
                match Lnk::open_write(&path) {
                    Ok(properties) => {
                        if let Ok(target) = properties.get_target_path() {
                            if super::is_sub_path(&target, root_dir).unwrap_or(false) {
                                info!("Selected shortcut for update '{}' because target '{}' matched.", path.to_string_lossy(), target);
                                paths.push((flag, properties));
                            }
                        } else if let Ok(work_dir) = properties.get_working_directory() {
                            if super::is_sub_path(&work_dir, root_dir).unwrap_or(false) {
                                info!("Selected shortcut for update '{}' because work_dir '{}' matched.", path.to_string_lossy(), work_dir);
                                paths.push((flag, properties));
                            }
                        } else {
                            warn!("Could not resolve target or work_dir for shortcut '{}'.", path.to_string_lossy());
                        }
                    }
                    Err(e) => {
                        warn!("Failed to load shortcut: {}", e);
                    }
                }
            }
        }
    }

    Ok(paths)
}

unsafe fn unsafe_remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let shortcuts = unsafe_get_shortcuts_for_root_dir(root_dir)?;
    for (flag, properties) in shortcuts {
        let path = properties.get_link_path();
        info!("Removing shortcut '{}' ({:?}).", path, flag);
        let remove_parent_if_empty = flag == ShortcutLocationFlags::START_MENU;
        if let Err(e) = unsafe_delete_lnk_file(&path, remove_parent_if_empty) {
            warn!("Failed to remove shortcut: {}", e);
        }
    }
    Ok(())
}

unsafe fn unsafe_delete_lnk_file<P: AsRef<Path>>(path: P, remove_parent_if_empty: bool) -> Result<()> {
    let path = path.as_ref();
    if !path.exists() {
        return Ok(());
    }

    if let Err(e) = unsafe_unpin_lnk_from_start(path) {
        warn!("Failed to unpin lnk from start menu: {}", e);
    }

    util::retry_io(|| std::fs::remove_file(&path))?;

    // if the parent directory is empty, remove it as well
    if remove_parent_if_empty {
        if let Some(parent_path) = path.parent() {
            if let Ok(entries) = parent_path.read_dir() {
                if entries.count() == 0 {
                    util::retry_io(|| std::fs::remove_dir(&parent_path))?;
                }
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
        unsafe {
            let hr = CoInitializeEx(None, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
            if hr.is_err() {
                return Err(anyhow!(hr));
            }
            // This sleep is necessary to ensure that the COM context is fully initialized.
            // I don't know why we need it, but if we don't have it then subsequent COM calls
            // will break intermittently.
            std::thread::sleep(Duration::from_millis(1));
            let result = delegate();
            CoUninitialize();
            result
        }
    });

    t.join().map_err(|e| {
        e.downcast_ref::<Box<dyn std::error::Error + Send>>()
            .map(|err| anyhow::anyhow!(err.to_string())) // Convert to anyhow using error string
            .unwrap_or_else(|| anyhow::anyhow!("Thread panicked or returned an unknown error type"))
    })?
}

#[derive(Clone)]
struct Lnk {
    me: IShellLinkW,
    pf: IPersistFile,
    my_path: String,
}

#[allow(dead_code)]
impl Lnk {
    pub unsafe fn get_link_path(&self) -> String {
        self.my_path.clone()
    }

    pub unsafe fn get_arguments(&self) -> Result<String> {
        let mut pszargs = [0u16; 1024];
        self.me.GetArguments(&mut pszargs)?;
        let args = u16_to_string(pszargs)?;
        Ok(args)
    }

    pub unsafe fn get_description(&self) -> Result<String> {
        let mut pszdesc = [0u16; 1024];
        self.me.GetDescription(&mut pszdesc)?;
        let desc = u16_to_string(pszdesc)?;
        Ok(desc)
    }

    pub unsafe fn get_icon_location(&self) -> Result<(String, i32)> {
        let mut pszfile = [0u16; 1024];
        let mut pindex = 0;
        self.me.GetIconLocation(&mut pszfile, &mut pindex)?;
        let icon = u16_to_string(pszfile)?;
        Ok((icon, pindex))
    }

    pub unsafe fn get_target_path(&self) -> Result<String> {
        let mut pszfile = [0u16; 1024];
        self.me.GetPath(&mut pszfile, std::ptr::null_mut(), 0)?;
        let target = u16_to_string(pszfile)?;
        Ok(target)
    }

    pub unsafe fn get_working_directory(&self) -> Result<String> {
        let mut pszdir = [0u16; 1024];
        self.me.GetWorkingDirectory(&mut pszdir)?;
        let work_dir = u16_to_string(pszdir)?;
        Ok(work_dir)
    }

    pub unsafe fn set_arguments(&mut self, path: &str) -> Result<()> {
        let args = string_to_u16(path);
        let args = PCWSTR(args.as_ptr());
        Ok(self.me.SetArguments(args)?)
    }

    pub unsafe fn set_description(&mut self, path: &str) -> Result<()> {
        let desc = string_to_u16(path);
        let desc = PCWSTR(desc.as_ptr());
        Ok(self.me.SetDescription(desc)?)
    }

    pub unsafe fn set_icon_location(&mut self, path: &str, index: i32) -> Result<()> {
        let icon = string_to_u16(path);
        let icon = PCWSTR(icon.as_ptr());
        Ok(self.me.SetIconLocation(icon, index)?)
    }

    pub unsafe fn set_target_path(&mut self, path: &str) -> Result<()> {
        let target = string_to_u16(path);
        let target = PCWSTR(target.as_ptr());
        Ok(self.me.SetPath(target)?)
    }

    pub unsafe fn set_working_directory(&mut self, path: &str) -> Result<()> {
        let work_dir = string_to_u16(path);
        let work_dir = PCWSTR(work_dir.as_ptr());
        Ok(self.me.SetWorkingDirectory(work_dir)?)
    }

    pub unsafe fn set_aumid(&mut self, app_model_id: Option<&str>) -> Result<()> {
        // Set app user model ID property
        // Docs: https://docs.microsoft.com/windows/win32/properties/props-system-appusermodel-id
        let store: IPropertyStore = self.me.cast()?;
        if let Some(app_model_id) = app_model_id {
            let id = string_to_u16(app_model_id);
            let id = PCWSTR(id.as_ptr());
            let variant = InitPropVariantFromStringVector(Some(&[id]))?;
            store.SetValue(&PKEY_AppUserModel_ID, &variant)?;
        } else {
            let prop_variant = PROPVARIANT::default(); // defaults to VT_EMPTY
            store.SetValue(&PKEY_AppUserModel_ID, &prop_variant)?;
        }
        store.Commit()?;
        Ok(())
    }

    pub unsafe fn save(&mut self) -> Result<()> {
        Ok(self.pf.Save(None, true)?)
    }

    pub unsafe fn save_as(&mut self, path: &str) -> Result<()> {
        let output = string_to_u16(path);
        let output = PCWSTR(output.as_ptr());
        self.my_path = path.to_string();
        Ok(self.pf.Save(output, true)?)
    }


    pub unsafe fn open_write<P: AsRef<Path>>(link_path: P) -> Result<Lnk> {
        let link_path = link_path.as_ref().to_string_lossy().to_string();
        let link: IShellLinkW = create_instance(&ShellLink)?;
        let persist: IPersistFile = link.cast()?;
        debug!("Loading link: {}", link_path);

        let link_pcwstr = string_to_u16(&link_path);
        let link_pcwstr = PCWSTR(link_pcwstr.as_ptr());

        persist.Load(link_pcwstr, STGM_READWRITE)?;

        // we don't really want to "resolve" the shortcut in the middle of an update operation
        // this can cause Windows to move the target path of a shortcut to one of our temp dirs etc

        // const SLR_NO_UI: u32 = 1;
        // const SLR_ANY_MATCH: u32 = 2;
        // const TIMEOUT_1MS: u32 = 1 << 16;
        // let flags = Lnk::SLR_NO_UI | Lnk::SLR_ANY_MATCH | Lnk::TIMEOUT_1MS;
        // if let Err(e) = link.Resolve(HWND(0), flags) {
        //     // this happens if the target path is missing and the link is broken
        //     warn!("Failed to resolve link {} ({:?})", link_path, e);
        // }

        Ok(Lnk { me: link, pf: persist, my_path: link_path })
    }

    pub unsafe fn create_new() -> Result<Lnk> {
        let link: IShellLinkW = create_instance(&ShellLink)?;
        let persist: IPersistFile = link.cast()?;
        Ok(Lnk { me: link, pf: persist, my_path: String::new() })
    }
}

impl std::fmt::Debug for Lnk {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Lnk({})", self.my_path)
    }
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
                let mut l = Lnk::create_new().unwrap();
                l.set_target_path(&target).unwrap();
                l.set_working_directory(&work).unwrap();
                l.save_as(&p1).unwrap();
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
            let l = Lnk::open_write(link_path).unwrap();
            let target = l.get_target_path().unwrap();
            assert_eq!(target, "C:\\Users\\Caelan\\AppData\\Local\\Discord\\Update.exe");
            Ok(())
        })
            .unwrap();
    }
}

#[test]
fn test_shortcut_full_integration() {
    unsafe {
        unsafe fn test_create_lnk(link_path: &str, target: &str, work: &str, aumid: Option<&str>) {
            let mut l = Lnk::create_new().unwrap();
            l.set_target_path(target).unwrap();
            l.set_working_directory(work).unwrap();
            l.set_aumid(aumid).unwrap();
            l.save_as(link_path).unwrap();
        }

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

            let aumid = "Some_Test_Rust_Velopack_AUMID";

            test_create_lnk(&link1_str, target, work, Some(aumid));
            test_create_lnk(&link2_str, target, work, Some(aumid));
            test_create_lnk(&link3_str, target, work, Some(aumid));

            assert!(link1.exists());
            assert!(link2.exists());
            assert!(link3.exists());

            // let ps_result = Command::new("powershell").raw_arg("Get-StartApps | Sort-Object -Property Name").output()?;
            // let ps_output = String::from_utf8_lossy(&ps_result.stdout).to_string();

            let shortcuts = unsafe_get_shortcuts_for_root_dir(&root).unwrap();
            assert_eq!(shortcuts.len(), 3);
            assert_eq!(shortcuts[0].0, ShortcutLocationFlags::DESKTOP);
            assert_eq!(PathBuf::from(&shortcuts[0].1.my_path), link1);
            assert_eq!(shortcuts[1].0, ShortcutLocationFlags::START_MENU);
            assert_eq!(PathBuf::from(&shortcuts[1].1.my_path), link3);
            assert_eq!(shortcuts[2].0, ShortcutLocationFlags::START_MENU);
            assert_eq!(PathBuf::from(&shortcuts[2].1.my_path), link2);

            assert_eq!(shortcuts[0].1.get_target_path().unwrap(), target);
            assert_eq!(shortcuts[0].1.get_working_directory().unwrap(), work);

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
