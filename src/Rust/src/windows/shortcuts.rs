use crate::bundle::Manifest;
use crate::shared as util;
use anyhow::{bail, Result};
use glob::glob;
use std::path::{Path, PathBuf};
use std::time::Duration;
use winsafe::{self as w, co};

use windows::core::{ComInterface, IntoParam, Param, Result as WindowsResult, GUID, HSTRING, PCWSTR};
use windows::Win32::Foundation::HWND;
use windows::Win32::Storage::EnhancedStorage::PKEY_AppUserModel_ID;
use windows::Win32::System::Com::StructuredStorage::InitPropVariantFromStringVector;
use windows::Win32::System::Com::{CoCreateInstance, CoInitializeEx, IPersistFile, CLSCTX_ALL, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE, STGM_READ};
use windows::Win32::UI::Shell::PropertiesSystem::IPropertyStore;
use windows::Win32::UI::Shell::{IShellLinkW, ShellLink};

// https://github.com/vaginessa/PWAsForFirefox/blob/fba68dbcc7ca27b970dc5a278ebdad32e0ab3c83/native/src/integrations/implementation/windows.rs#L28

#[inline]
fn init_com() -> WindowsResult<()> {
    unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE) }?;
    std::thread::sleep(Duration::from_millis(1));
    Ok(())
}

#[inline]
fn create_instance<T: ComInterface>(clsid: &GUID) -> WindowsResult<T> {
    unsafe { CoCreateInstance(clsid, None, CLSCTX_ALL) }
}

pub fn create_default_lnks(root_path: &PathBuf, app: &Manifest) -> Result<()> {
    let app = app.clone();
    let current_path = app.get_current_path(root_path);
    let main_exe_path = app.get_main_exe_path(root_path);
    let t = std::thread::spawn(move || {
        init_com()?;
        let mut was_error = false;

        info!("Creating desktop shortcut...");
        let desktop = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Desktop, co::KF::DONT_UNEXPAND, None)?;
        let desktop_lnk = Path::new(&desktop).join(format!("{}.lnk", &app.title));
        if let Err(e) = _create_lnk(&desktop_lnk.to_string_lossy(), &main_exe_path, &current_path, None) {
            warn!("Failed to create start menu shortcut: {}", e);
            was_error = true;
        }

        std::thread::sleep(Duration::from_millis(1));

        info!("Creating start menu shortcut...");
        let startmenu = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None)?;
        let start_lnk = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", &app.title));
        if let Err(e) = _create_lnk(&start_lnk.to_string_lossy(), &main_exe_path, &current_path, None) {
            warn!("Failed to create start menu shortcut: {}", e);
            was_error = true;
        }

        if was_error {
            bail!("Failed to create one or both default shortcuts");
        }

        Ok(())
    });
    t.join().unwrap()
}

#[allow(dead_code)]
fn create_lnk(output: &str, target: &str, work_dir: &str, app_model_id: Option<&str>) -> WindowsResult<()> {
    let output = output.to_string();
    let target = target.to_string();
    let work_dir = work_dir.to_string();
    let app_model_id = app_model_id.map(|f| f.to_string());
    let t = std::thread::spawn(move || {
        init_com()?;
        _create_lnk(&output, &target, &work_dir, app_model_id)?;
        Ok(())
    });
    t.join().unwrap()
}

fn _create_lnk(output: &str, target: &str, work_dir: &str, app_model_id: Option<String>) -> WindowsResult<()> {
    let link: IShellLinkW = create_instance(&ShellLink)?;

    unsafe {
        link.SetPath(&HSTRING::from(target))?;
        link.SetWorkingDirectory(&HSTRING::from(work_dir))?;

        // Set app user model ID property
        // Docs: https://docs.microsoft.com/windows/win32/properties/props-system-appusermodel-id
        if let Some(app_model_id) = app_model_id {
            let store: IPropertyStore = link.cast()?;
            let id: Param<PCWSTR> = HSTRING::from(app_model_id).into_param();
            let id: PCWSTR = id.abi();
            let variant = InitPropVariantFromStringVector(Some(&[id]))?;
            store.SetValue(&PKEY_AppUserModel_ID, &variant)?;
            store.Commit()?;
        }

        // Save shortcut to file
        let persist: IPersistFile = link.cast()?;
        persist.Save(&HSTRING::from(output), true)?;
    }

    Ok(())
}

pub fn resolve_lnk(link_path: &str) -> WindowsResult<(String, String)> {
    let link_path = link_path.to_string();
    let t = std::thread::spawn(move || {
        init_com()?;
        Ok(_resolve_lnk(&link_path)?)
    });
    t.join().unwrap()
}

fn _resolve_lnk(link_path: &str) -> WindowsResult<(String, String)> {
    let link: IShellLinkW = create_instance(&ShellLink)?;
    let persist: IPersistFile = link.cast()?;

    unsafe {
        debug!("Loading link: {}", link_path);
        persist.Load(&HSTRING::from(link_path), STGM_READ)?;
        let flags = 1 | 2 | 1 << 16;
        if let Err(e) = link.Resolve(HWND(0), flags) {
            // this happens if the target path is missing and the link is broken
            warn!("Failed to resolve link {} ({:?})", link_path, e);
        }

        let mut pszfile = [0u16; 260];
        let mut pszdir = [0u16; 260];
        link.GetPath(&mut pszfile, std::ptr::null_mut(), 0)?;
        link.GetWorkingDirectory(&mut pszdir)?;

        let target_len = pszfile.iter().position(|&c| c == 0).unwrap_or(pszfile.len());
        let target = HSTRING::from_wide(&pszfile[..target_len])?;
        let work_len = pszdir.iter().position(|&c| c == 0).unwrap_or(pszdir.len());
        let work_dir = HSTRING::from_wide(&pszdir[..work_len])?;
        Ok((target.to_string(), work_dir.to_string()))
    }
}

pub fn remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref().to_owned();
    let t = std::thread::spawn(move || {
        init_com()?;
        _remove_all_shortcuts_for_root_dir(&root_dir)?;
        Ok(())
    });
    t.join().unwrap()
}

fn _remove_all_shortcuts_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let root_dir = root_dir.as_ref();
    info!("Searching for shortcuts containing root: '{}'", root_dir.to_string_lossy());

    let mut search_paths = vec![
        w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Desktop, co::KF::DONT_UNEXPAND, None)?,
        w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Startup, co::KF::DONT_UNEXPAND, None)?,
        w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None)?,
    ];

    let pinned_str = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::RoamingAppData, co::KF::DONT_UNEXPAND, None)?;
    let pinned_path = Path::new(&pinned_str).join("Microsoft\\Internet Explorer\\Quick Launch\\User Pinned");
    search_paths.push(pinned_path.to_string_lossy().to_string());

    for search_root in search_paths {
        let g = format!("{}/**/*.lnk", search_root);
        info!("Searching for shortcuts in: '{}'", g);
        if let Ok(paths) = glob(&g) {
            for path in paths {
                if let Ok(path) = path {
                    trace!("Checking shortcut: '{}'", path.to_string_lossy());
                    let res = _resolve_lnk(&path.to_string_lossy());
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
                            util::retry_io(|| std::fs::remove_file(&path))?;
                        }
                    }
                }
            }
        }
    }
    Ok(())
}

#[test]
#[ignore]
fn test_shortcut_intense_intermittent() {
    let startmenu = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None).unwrap();
    let lnk_path = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", "veloshortcuttest"));
    let target = "C:\\Users\\Caelan\\AppData\\Local\\Discord\\Update.exe";
    let work = "C:\\Users\\Caelan\\AppData\\Local\\Discord";

    let mut i = 0;
    while i < 100 {
        create_lnk(&lnk_path.to_string_lossy(), &target, &work, None).unwrap();
        assert!(lnk_path.exists());
        util::retry_io(|| std::fs::remove_file(&lnk_path)).unwrap();
        assert!(!lnk_path.exists());
        i += 1;
    }
}

#[test]
#[ignore]
fn test_can_resolve_existing_shortcut() {
    let link_path = r"C:\Users\Caelan\Desktop\Discord.lnk";
    let (target, _workdir) = resolve_lnk(link_path).unwrap();
    assert_eq!(target, "C:\\Users\\Caelan\\AppData\\Local\\Discord\\Update.exe");
}

#[test]
fn shortcut_full_integration_test() {
    let desktop = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Desktop, co::KF::DONT_UNEXPAND, None).unwrap();
    let link_location = Path::new(&desktop).join("testclowd123hi.lnk");
    let target = r"C:\Users\Caelan\AppData\Local\NonExistingAppHello123\current\HelloWorld.exe";
    let work = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123\current";
    let root = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123";

    create_lnk(&link_location.to_string_lossy(), target, work, None).unwrap();
    assert!(link_location.exists());

    let (target_out, work_out) = resolve_lnk(&link_location.to_string_lossy()).unwrap();
    assert_eq!(target_out, target);
    assert_eq!(work_out, work);

    remove_all_shortcuts_for_root_dir(root).unwrap();
    assert!(!link_location.exists());
}

// pub struct Lnk {
//     me: w::IShellLink,
//     pf: w::IPersistFile,
// }

// pub trait ShellLinkReadOnly {
//     fn get_arguments(&self) -> w::HrResult<String>;
//     fn get_description(&self) -> w::HrResult<String>;
//     fn get_icon_location(&self) -> w::HrResult<(String, i32)>;
//     fn get_path(&self) -> w::HrResult<String>;
//     fn get_show_cmd(&self) -> w::HrResult<co::SW>;
//     fn get_working_directory(&self) -> w::HrResult<String>;
// }

// pub trait ShellLinkWriteOnly {
//     fn set_arguments(&mut self, path: &str) -> w::HrResult<()>;
//     fn set_description(&mut self, path: &str) -> w::HrResult<()>;
//     fn set_icon_location(&mut self, path: &str, index: i32) -> w::HrResult<()>;
//     fn set_path(&mut self, path: &str) -> w::HrResult<()>;
//     fn set_show_cmd(&mut self, show_cmd: co::SW) -> w::HrResult<()>;
//     fn set_working_directory(&mut self, path: &str) -> w::HrResult<()>;
//     fn save(&self) -> w::HrResult<()>;
//     fn save_as(&self, path: &str) -> w::HrResult<()>;
// }

// impl ShellLinkReadOnly for Lnk {
//     fn get_arguments(&self) -> w::HrResult<String> {
//         Ok(self.me.GetArguments()?)
//     }

//     fn get_description(&self) -> w::HrResult<String> {
//         Ok(self.me.GetDescription()?)
//     }

//     fn get_icon_location(&self) -> w::HrResult<(String, i32)> {
//         Ok(self.me.GetIconLocation()?)
//     }

//     fn get_path(&self) -> w::HrResult<String> {
//         Ok(self.me.GetPath(None, co::SLGP::UNCPRIORITY)?)
//     }

//     fn get_show_cmd(&self) -> w::HrResult<co::SW> {
//         Ok(self.me.GetShowCmd()?)
//     }

//     fn get_working_directory(&self) -> w::HrResult<String> {
//         Ok(self.me.GetWorkingDirectory()?)
//     }
// }

// impl ShellLinkWriteOnly for Lnk {
//     fn set_arguments(&mut self, path: &str) -> w::HrResult<()> {
//         Ok(self.me.SetArguments(path)?)
//     }

//     fn set_description(&mut self, path: &str) -> w::HrResult<()> {
//         Ok(self.me.SetDescription(path)?)
//     }

//     fn set_icon_location(&mut self, path: &str, index: i32) -> w::HrResult<()> {
//         Ok(self.me.SetIconLocation(path, index)?)
//     }

//     fn set_path(&mut self, path: &str) -> w::HrResult<()> {
//         Ok(self.me.SetPath(path)?)
//     }

//     fn set_show_cmd(&mut self, show_cmd: co::SW) -> w::HrResult<()> {
//         Ok(self.me.SetShowCmd(show_cmd)?)
//     }

//     fn set_working_directory(&mut self, path: &str) -> w::HrResult<()> {
//         Ok(self.me.SetWorkingDirectory(path)?)
//     }

//     fn save(&self) -> w::HrResult<()> {
//         Ok(self.pf.Save(None, true)?)
//     }

//     fn save_as(&self, path: &str) -> w::HrResult<()> {
//         Ok(self.pf.Save(Some(path), true)?)
//     }
// }

// pub trait ShellLinkReadWrite: ShellLinkReadOnly + ShellLinkWriteOnly {}
// impl ShellLinkReadWrite for Lnk {}

// impl Lnk {
//     pub fn open_read(file_path: &str) -> w::HrResult<Box<dyn ShellLinkReadOnly>> {
//         let me = w::CoCreateInstance::<w::IShellLink>(&co::CLSID::ShellLink, None, co::CLSCTX::INPROC_SERVER)?;
//         let pf = me.QueryInterface::<w::IPersistFile>()?;
//         pf.Load(file_path, co::STGM::READ)?;
//         let flags = (co::SLR::NO_UI | co::SLR::ANY_MATCH).raw() | (1 << 16);
//         let flags_with_timeout = unsafe { co::SLR::from_raw(flags) };
//         me.Resolve(&w::HWND::NULL, flags_with_timeout)?;
//         Ok(Box::new(Lnk { me, pf }))
//     }

//     pub fn open_write(file_path: &str) -> w::HrResult<Box<dyn ShellLinkReadWrite>> {
//         let me = w::CoCreateInstance::<w::IShellLink>(&co::CLSID::ShellLink, None, co::CLSCTX::INPROC_SERVER)?;
//         let pf = me.QueryInterface::<w::IPersistFile>()?;
//         pf.Load(file_path, co::STGM::READWRITE)?;
//         let flags = (co::SLR::NO_UI | co::SLR::ANY_MATCH).raw() | (1 << 16);
//         let flags_with_timeout = unsafe { co::SLR::from_raw(flags) };
//         me.Resolve(&w::HWND::NULL, flags_with_timeout)?;
//         Ok(Box::new(Lnk { me, pf }))
//     }

//     pub fn create_new() -> w::HrResult<Box<dyn ShellLinkReadWrite>> {
//         let me = w::CoCreateInstance::<w::IShellLink>(&co::CLSID::ShellLink, None, co::CLSCTX::INPROC_SERVER)?;
//         let pf = me.QueryInterface::<w::IPersistFile>()?;
//         Ok(Box::new(Lnk { me, pf }))
//     }
// }
