use winsafe::WString;

extern "C" {
    fn TryCloseProcessesUsingPath(pszAppName: *mut u16, pszPath: *mut u16) -> bool;
}

pub fn close_processes_locking_dir(app_name: &str, path: &str) -> bool {
    unsafe { TryCloseProcessesUsingPath(WString::from_str(app_name).as_mut_ptr(), WString::from_str(path).as_mut_ptr()) }
}
