use anyhow::Result;
use glob::glob;
use std::path::Path;
use winsafe::{self as w, co, HrResult, WString};

use crate::platform;
use crate::util;

type PCSTR = *const u16;
extern "C" {
    fn CreateLink(lpszPathObj: PCSTR, lpszPathLink: PCSTR, lpszWorkDir: PCSTR) -> u32;
    fn ResolveLink(lpszLinkFile: PCSTR, lpszPath: PCSTR, iPathBufferSize: i32, lpszWorkDir: PCSTR, iWorkDirBufferSize: i32) -> u32;
}

pub fn resolve_lnk(link_path: &str) -> HrResult<(String, String)> {
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED)?;
    _resolve_lnk(link_path)
}

pub fn create_lnk(output: &str, target: &str, work_dir: &str) -> HrResult<()> {
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED)?;
    _create_lnk(output, target, work_dir)
}

fn _create_lnk(output: &str, target: &str, work_dir: &str) -> HrResult<()> {
    let _comguard = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED)?;
    let hr = unsafe { CreateLink(WString::from_str(target).as_ptr(), WString::from_str(output).as_ptr(), WString::from_str(work_dir).as_ptr()) };
    match unsafe { co::HRESULT::from_raw(hr) } {
        co::HRESULT::S_OK => Ok(()),
        hr => Err(hr),
    }
}

fn _resolve_lnk(link_path: &str) -> HrResult<(String, String)> {
    let mut path_buf = WString::new_alloc_buf(1024);
    let mut work_dir_buf = WString::new_alloc_buf(1024);
    let hr = unsafe {
        ResolveLink(
            WString::from_str(link_path).as_ptr(),
            path_buf.as_mut_ptr(),
            path_buf.buf_len() as _,
            work_dir_buf.as_mut_ptr(),
            work_dir_buf.buf_len() as _,
        )
    };
    match unsafe { co::HRESULT::from_raw(hr) } {
        co::HRESULT::S_OK => Ok((path_buf.to_string(), work_dir_buf.to_string())),
        hr => Err(hr),
    }
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
