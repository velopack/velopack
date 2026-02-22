#![allow(dead_code)]
use velopack::wide_strings::*;
use windows::{
    core::PWSTR,
    Win32::{Foundation::ERROR_SUCCESS, System::ApplicationInstallationAndServicing::*, UI::WindowsAndMessaging::*},
};

pub fn msi_get_property<S: AsRef<str>>(h_install: MSIHANDLE, name: S) -> Option<String> {
    let name = string_to_wide(name.as_ref());
    let mut empty = string_to_wide("");
    let mut size = 0u32;

    unsafe {
        let _ = MsiGetPropertyW(h_install, name.as_pcwstr(), Some(empty.as_pwstr()), Some(&mut size));
        // show_error(h_install, format!("prop1: {ret} size1: {size}")); //234

        if size == 0 {
            return None; // No data found
        }

        size += 1; // +1 for null terminator

        let mut buf = vec![0u16; size as usize];
        let ret2 = MsiGetPropertyW(h_install, name.as_pcwstr(), Some(PWSTR(buf.as_mut_ptr())), Some(&mut size));
        // show_error(h_install, format!("prop2: {ret2} size2: {size}")); //234

        if ret2 == ERROR_SUCCESS.0 {
            Some(wide_to_string_lossy(buf))
        } else {
            None // Failed to get property
        }
    }
}

pub fn msi_set_property_string<S1: AsRef<str>, S2: AsRef<str>>(h_install: MSIHANDLE, name: S1, value: S2) {
    let name = string_to_wide(name.as_ref());
    let value = string_to_wide(value.as_ref());
    unsafe {
        let _ = MsiSetPropertyW(h_install, name.as_pcwstr(), value.as_pcwstr());
    }
}

pub fn msi_set_property_bool<S1: AsRef<str>>(h_install: MSIHANDLE, name: S1, value: bool) {
    let name = string_to_wide(name.as_ref());
    let value = string_to_wide(if value { "1" } else { "" });
    unsafe {
        let _ = MsiSetPropertyW(h_install, name.as_pcwstr(), value.as_pcwstr());
    }
}

pub fn msi_set_property_i32<S1: AsRef<str>>(h_install: MSIHANDLE, name: S1, value: i32) {
    let name = string_to_wide(name.as_ref());
    let value = string_to_wide(value.to_string());
    unsafe {
        let _ = MsiSetPropertyW(h_install, name.as_pcwstr(), value.as_pcwstr());
    }
}

pub fn msi_show_question<S: AsRef<str>>(h_install: MSIHANDLE, message: S) -> bool {
    let isnt_message = INSTALLMESSAGE_USER.0 | MB_OKCANCEL.0 as i32 | MB_ICONQUESTION.0 as i32;
    let res = unsafe { show_dialog_impl(h_install, message, isnt_message) };
    res == IDOK.0
}

pub fn msi_show_info<S: AsRef<str>>(h_install: MSIHANDLE, message: S) {
    let isnt_message = INSTALLMESSAGE_USER.0 | MB_OK.0 as i32 | MB_ICONINFORMATION.0 as i32;
    unsafe { show_dialog_impl(h_install, message, isnt_message) };
}

pub fn msi_show_warn<S: AsRef<str>>(h_install: MSIHANDLE, message: S) {
    let isnt_message = INSTALLMESSAGE_USER.0 | MB_OK.0 as i32 | MB_ICONWARNING.0 as i32;
    unsafe { show_dialog_impl(h_install, message, isnt_message) };
}

pub fn msi_show_error<S: AsRef<str>>(h_install: MSIHANDLE, message: S) {
    let isnt_message = INSTALLMESSAGE_ERROR.0 | MB_OK.0 as i32 | MB_ICONERROR.0 as i32;
    unsafe { show_dialog_impl(h_install, message, isnt_message) };
}

unsafe fn show_dialog_impl<S: AsRef<str>>(h_install: MSIHANDLE, message: S, flags: i32) -> i32 {
    let message = string_to_wide(message.as_ref());
    let rec = MsiCreateRecord(1);
    MsiRecordSetStringW(rec, 0, message.as_pcwstr());
    let ret = MsiProcessMessage(h_install, INSTALLMESSAGE(flags), rec);
    MsiCloseHandle(rec);
    ret
}

// https://learn.microsoft.com/en-us/windows/win32/api/msiquery/nf-msiquery-msiprocessmessage#record-fields-for-progress-bar-messages
// https://learn.microsoft.com/en-us/windows/win32/msi/adding-custom-actions-to-the-progressbar
pub struct ProgressContext {
    h_install: MSIHANDLE,
    current_ticks: i32,
    total_ticks: i32,
}

impl ProgressContext {
    pub fn new(h_install: MSIHANDLE, jobs: usize) -> Self {
        let jobs = jobs as i32; // Convert job index to i32 for calculations
        Self { h_install, current_ticks: 0, total_ticks: 100 * jobs }
    }

    pub fn reset(&mut self) {
        unsafe {
            progress_reset(self.h_install, self.total_ticks);
            self.current_ticks = 0;
        }
    }

    pub fn set_progress(&mut self, progress: i32, job: usize) {
        let job = job as i32; // Convert job index to i32 for calculations
        let progress = progress + (job * 100); // Adjust progress based on the job index
        unsafe {
            if progress > self.current_ticks {
                // If the progress is greater than the current ticks, we increment the progress bar
                let diff = progress - self.current_ticks;
                progress_increment(self.h_install, diff);
                self.current_ticks += diff;
            }
            // else {
            //     // If the progress is less than the current ticks, we reset the progress bar
            //     progress_reset(self.h_install, self.total_ticks);
            //     progress_increment(self.h_install, progress);
            //     self.current_ticks = progress;
            // }
        };
    }
}

unsafe fn progress_reset(h_install: MSIHANDLE, ticks: i32) {
    let rec = MsiCreateRecord(3);
    MsiRecordSetInteger(rec, 1, 0); // reset command
    MsiRecordSetInteger(rec, 2, ticks); // expected number of ticks
    MsiRecordSetInteger(rec, 3, 0); // forward progress bar (left to right)
    MsiProcessMessage(h_install, INSTALLMESSAGE_PROGRESS, rec);
    MsiCloseHandle(rec);
}

// unsafe fn progress_set_explicit_progress(h_install: MSIHANDLE) {
//     let rec = MsiCreateRecord(3);
//     MsiRecordSetInteger(rec, 1, 1); // information command
//     MsiRecordSetInteger(rec, 2, 1); // explicit progress
//     MsiRecordSetInteger(rec, 3, 0); // unused
//     MsiProcessMessage(h_install, INSTALLMESSAGE_PROGRESS, rec);
//     MsiCloseHandle(rec);
// }

unsafe fn progress_increment(h_install: MSIHANDLE, ticks: i32) {
    let rec = MsiCreateRecord(3);
    MsiRecordSetInteger(rec, 1, 2); // increment command
    MsiRecordSetInteger(rec, 2, ticks); // ticks to increment
    MsiRecordSetInteger(rec, 3, 0); // unused
    MsiProcessMessage(h_install, INSTALLMESSAGE_PROGRESS, rec);
    MsiCloseHandle(rec);
}

// unsafe fn progress_add_extra_ticks(h_install: MSIHANDLE, ticks: i32) {
//     let rec = MsiCreateRecord(3);
//     MsiRecordSetInteger(rec, 1, 3); // add ticks command
//     MsiRecordSetInteger(rec, 2, ticks); // ticks to add to the total progress
//     MsiRecordSetInteger(rec, 3, 0); // unused
//     MsiProcessMessage(h_install, INSTALLMESSAGE_PROGRESS, rec);
//     MsiCloseHandle(rec);
// }
