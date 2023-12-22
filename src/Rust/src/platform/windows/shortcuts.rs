use anyhow::{anyhow, Result};
use glob::glob;
use std::path::Path;
use winsafe::{self as w, co, prelude::*};

use super::ishelllink::*;
use crate::platform;
use crate::util;

// type PCSTR = *const u16;
// extern "C" {
//     fn CreateLink(lpszPathObj: PCSTR, lpszPathLink: PCSTR, lpszWorkDir: PCSTR) -> u32;
//     fn ResolveLink(lpszLinkFile: PCSTR, lpszPath: PCSTR, iPathBufferSize: i32, lpszWorkDir: PCSTR, iWorkDirBufferSize: i32) -> u32;
// }

pub fn resolve_lnk(link_path: &str) -> Result<(String, String)> {
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED | co::COINIT::DISABLE_OLE1DDE)?;
    _resolve_lnk(link_path)
}

pub fn create_lnk(output: &str, target: &str, work_dir: &str) -> w::HrResult<()> {
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED | co::COINIT::DISABLE_OLE1DDE)?;
    _create_lnk(output, target, work_dir)
}

fn _create_lnk(output: &str, target: &str, work_dir: &str) -> w::HrResult<()> {
    let me = w::CoCreateInstance::<w::IShellLink>(&co::CLSID::ShellLink, None, co::CLSCTX::INPROC_SERVER)?;
    me.SetPath(target)?;
    me.SetWorkingDirectory(work_dir)?;
    let pf = me.QueryInterface::<w::IPersistFile>()?;
    pf.Save(Some(output), true)?;
    Ok(())

    // let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED)?;
    // let hr = unsafe { CreateLink(WString::from_str(target).as_ptr(), WString::from_str(output).as_ptr(), WString::from_str(work_dir).as_ptr()) };
    // match unsafe { co::HRESULT::from_raw(hr) } {
    //     co::HRESULT::S_OK => Ok(()),
    //     hr => Err(hr),
    // }
}

fn _resolve_lnk(link_path: &str) -> Result<(String, String)> {
    let lnk = ShellLink::new().map_err(|e| anyhow!("Failed to create ShellLink: {:?}", e))?;
    lnk.load(link_path).map_err(|e| anyhow!("Failed to load link: {:?}", e))?;
    let path = lnk.get_path().map_err(|e| anyhow!("Failed to get link path: {:?}", e))?;
    let work = lnk.get_working_directory().map_err(|e| anyhow!("Failed to get link working directory: {:?}", e))?;
    Ok((path.to_string_lossy().to_string(), work.to_string_lossy().to_string()))

    // let me = w::CoCreateInstance::<w::IShellLink>(&co::CLSID::ShellLink, None, co::CLSCTX::INPROC_SERVER)?;
    // let pf = me.QueryInterface::<w::IPersistFile>()?;
    // pf.Load(link_path, co::STGM::READ)?;
    // info!("5");
    // // let flags_with_timeout = unsafe { co::SLR::from_raw(65539) };
    // info!("{:?}", w::HWND::NULL.ptr());
    // me.Resolve(&w::HWND::NULL, co::SLR::NO_UI);

    // info!("6");
    // let path = me.GetPath(None, co::SLGP::UNCPRIORITY)?;

    // let workdir = me.GetWorkingDirectory()?;
    // info!("7");
    // Ok((path, workdir))

    // let me = w::CoCreateInstance::<w::IShellLink>(&co::CLSID::ShellLink, None, co::CLSCTX::INPROC_SERVER)?;
    // let pf = me.QueryInterface::<w::IPersistFile>()?;
    // pf.Load(file_path, co::STGM::READ)?;
    // let flags = (co::SLR::NO_UI | co::SLR::ANY_MATCH).raw() | (1 << 16);
    // me.Resolve(&w::HWND::NULL, flags_with_timeout)?;
    // Ok(Box::new(Lnk { me, pf }))

    // let mut path_buf = WString::new_alloc_buf(1024);
    // let mut work_dir_buf = WString::new_alloc_buf(1024);
    // let hr = unsafe {
    //     ResolveLink(
    //         WString::from_str(link_path).as_ptr(),
    //         path_buf.as_mut_ptr(),
    //         path_buf.buf_len() as _,
    //         work_dir_buf.as_mut_ptr(),
    //         work_dir_buf.buf_len() as _,
    //     )
    // };
    // match unsafe { co::HRESULT::from_raw(hr) } {
    //     co::HRESULT::S_OK => Ok((path_buf.to_string(), work_dir_buf.to_string())),
    //     hr => Err(hr),
    // }
}

pub fn remove_all_for_root_dir<P: AsRef<Path>>(root_dir: P) -> Result<()> {
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED)?;
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
                    if let Ok((target, work_dir)) = res {
                        let target_match = platform::is_sub_path(&target, root_dir).unwrap_or(false);
                        let work_dir_match = platform::is_sub_path(&work_dir, root_dir).unwrap_or(false);
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
fn shortcut_full_integration_test() {
    let desktop = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Desktop, co::KF::DONT_UNEXPAND, None).unwrap();
    let link_location = Path::new(&desktop).join("testclowd123hi.lnk");
    let target = r"C:\Users\Caelan\AppData\Local\NonExistingAppHello123\current\HelloWorld.exe";
    let work = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123\current";
    let root = r"C:\Users\Caelan/appData\Local/NonExistingAppHello123";

    create_lnk(&link_location.to_string_lossy(), target, work).unwrap();
    assert!(link_location.exists());

    let (target_out, work_out) = resolve_lnk(&link_location.to_string_lossy()).unwrap();
    assert_eq!(target_out, target);
    assert_eq!(work_out, work);

    remove_all_for_root_dir(root).unwrap();
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
