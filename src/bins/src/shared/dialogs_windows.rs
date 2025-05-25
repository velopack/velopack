use super::{dialogs_common::*, dialogs_const::*};
use anyhow::Result;
use std::path::PathBuf;
use velopack::{
    bundle::Manifest,
    locator::{auto_locate_app_manifest, LocationContext},
    wide_strings::{string_to_wide, string_to_wide_opt},
};
use windows::{
    core::HRESULT,
    Win32::{
        Foundation::{FALSE, HWND, LPARAM, S_FALSE, S_OK, WPARAM},
        UI::{
            Controls::*,
            Shell::ShellExecuteW,
            WindowsAndMessaging::{GetDesktopWindow, IDCANCEL, IDCONTINUE, IDOK, IDRETRY, IDYES, SW_SHOWDEFAULT},
        },
    },
};

pub fn show_restart_required(app: &Manifest) {
    show_warn(
        format!("{} Setup {}", app.title, app.version).as_str(),
        Some("Restart Required"),
        "A restart is required before Setup can continue. Please restart your computer and try again.",
    );
}

pub fn show_update_missing_dependencies_dialog(
    app: &Manifest,
    depedency_string: &str,
    from: &semver::Version,
    to: &semver::Version,
) -> bool {
    if get_silent() {
        // this has different behavior to show_setup_missing_dependencies_dialog,
        // if silent is true then we will bail because the app is probably exiting
        // and installing dependencies may result in a UAC prompt.
        warn!("Cancelling pre-requisite installation because silent flag is true.");
        return false;
    }

    show_ok_cancel(
        format!("{} Update", app.title).as_str(),
        Some(format!("{} would like to update from {} to {}", app.title, from, to).as_str()),
        format!(
            "{} {to} has missing dependencies which need to be installed: {}, would you like to continue?",
            app.title, depedency_string
        )
        .as_str(),
        Some("Install & Update"),
    )
}

pub fn show_setup_missing_dependencies_dialog(app: &Manifest, depedency_string: &str) -> bool {
    if get_silent() {
        return true;
    }

    show_ok_cancel(
        format!("{} Setup {}", app.title, app.version).as_str(),
        Some(format!("{} has missing system dependencies.", app.title).as_str()),
        format!("{} requires the following packages to be installed: {}, would you like to continue?", app.title, depedency_string)
            .as_str(),
        Some("Install"),
    )
}

pub fn show_uninstall_complete_with_errors_dialog(app_title: &str, log_path: Option<&PathBuf>) {
    if get_silent() {
        return;
    }

    let setup_name = string_to_wide(format!("{} Uninstall", app_title));
    let instruction = string_to_wide(format!("{} uninstall has completed with errors.", app_title));
    let content = string_to_wide(
        "There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again."
    );

    let mut config = TASKDIALOGCONFIG::default();
    config.cbSize = std::mem::size_of::<TASKDIALOGCONFIG>() as u32;
    config.dwFlags = TDF_ENABLE_HYPERLINKS | TDF_SIZE_TO_CONTENT;
    config.dwCommonButtons = TDCBF_OK_BUTTON;
    config.pszWindowTitle = setup_name.as_pcwstr();
    config.pszMainInstruction = instruction.as_pcwstr();
    config.pszContent = content.as_pcwstr();
    config.Anonymous1.pszMainIcon = TD_WARNING_ICON;

    let footer_path = log_path.map(|p| p.to_string_lossy().to_string()).unwrap_or("".to_string());
    let footer = string_to_wide(format!("Log file: '<A HREF=\"na\">{}</A>'", footer_path));
    if let Some(log_path) = log_path {
        if log_path.exists() {
            config.Anonymous2.pszFooterIcon = TD_INFORMATION_ICON;
            config.pszFooter = footer.as_pcwstr();
            config.lpCallbackData = log_path as *const PathBuf as isize;
            config.pfCallback = Some(task_dialog_callback);
        }
    }

    unsafe { TaskDialogIndirect(&config, None, None, None).ok() };
}

pub fn show_processes_locking_folder_dialog(app_title: &str, app_version: &str, process_names: &str) -> DialogResult {
    if get_silent() {
        return DialogResult::Cancel;
    }

    let mut config = TASKDIALOGCONFIG::default();
    config.cbSize = std::mem::size_of::<TASKDIALOGCONFIG>() as u32;
    config.Anonymous1.pszMainIcon = TD_INFORMATION_ICON;

    let update_name = string_to_wide(format!("{} Update {}", app_title, app_version));
    let instruction = string_to_wide(format!("{} Update", app_title));
    let content = string_to_wide(format!(
        "There are programs ({}) preventing the {} update from proceeding. \n\n\
        You can press Continue to have this updater attempt to close them automatically, or if you've closed them yourself press Retry for the updater to check again.",
        process_names, app_title));

    let btn_retry_txt = string_to_wide("Retry\nTry again if you've closed the program(s)");
    let btn_continue_txt = string_to_wide("Continue\nAttempt to close the program(s) automatically");
    let btn_cancel_txt = string_to_wide("Cancel\nThe update will not continue");
    let btn_retry = TASKDIALOG_BUTTON { nButtonID: IDRETRY.0, pszButtonText: btn_retry_txt.as_pcwstr() };
    let btn_continue = TASKDIALOG_BUTTON { nButtonID: IDCONTINUE.0, pszButtonText: btn_continue_txt.as_pcwstr() };
    let btn_cancel = TASKDIALOG_BUTTON { nButtonID: IDCANCEL.0, pszButtonText: btn_cancel_txt.as_pcwstr() };
    let custom_btns = vec![btn_retry, btn_continue, btn_cancel];

    config.dwFlags = TDF_USE_COMMAND_LINKS;
    config.cButtons = custom_btns.len() as u32;
    config.pButtons = custom_btns.as_ptr();
    config.pszWindowTitle = update_name.as_pcwstr();
    config.pszMainInstruction = instruction.as_pcwstr();
    config.pszContent = content.as_pcwstr();

    let mut pnbutton = 0;
    let mut pnradiobutton = 0;
    let mut pfverificationflagchecked = FALSE;

    unsafe { TaskDialogIndirect(&config, Some(&mut pnbutton), Some(&mut pnradiobutton), Some(&mut pfverificationflagchecked)).ok() };
    DialogResult::from_win(pnbutton)
}

pub fn show_overwrite_repair_dialog(app: &Manifest, root_path: &PathBuf, root_is_default: bool) -> bool {
    if get_silent() {
        return true;
    }

    let mut config = TASKDIALOGCONFIG::default();
    config.cbSize = std::mem::size_of::<TASKDIALOGCONFIG>() as u32;
    config.Anonymous1.pszMainIcon = TD_WARNING_ICON;

    let setup_name = string_to_wide(format!("{} Setup {}", app.title, app.version));
    let mut instruction = string_to_wide(format!("{} is already installed.", app.title));
    let mut content =
        string_to_wide("This application is installed on your computer. If it is not functioning correctly, you can attempt to repair it.");
    let mut btn_yes_txt = string_to_wide(format!("Repair\nErase the application and re-install version {}.", app.version));
    let btn_cancel_txt = string_to_wide("Cancel\nBackup or save your work first");

    // if we can detect the current app version, we call it "Update" or "Downgrade"
    let old_app = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(root_path.to_owned()));
    if let Ok(old) = old_app {
        let old_version = old.get_manifest_version();
        if old_version < app.version {
            instruction = string_to_wide(format!("An older version of {} is installed.", app.title));
            content = string_to_wide(format!("Would you like to update from {} to {}?", old_version, app.version));
            btn_yes_txt = string_to_wide(format!("Update\nTo version {}", app.version));
            config.Anonymous1.pszMainIcon = TD_INFORMATION_ICON;
        } else if old_version > app.version {
            instruction = string_to_wide(format!("A newer version of {} is installed.", app.title));
            content = string_to_wide(format!(
                "You already have {} installed. Would you like to downgrade this application to an older version?",
                old_version
            ));
            btn_yes_txt = string_to_wide(format!("Downgrade\nTo version {}", app.version));
        }
    }

    let footer = if root_is_default {
        string_to_wide(format!("The install directory is '<A HREF=\"na\">%LocalAppData%\\{}</A>'", app.id))
    } else {
        string_to_wide(format!("The install directory is '<A HREF=\"na\">{}</A>'", root_path.display()))
    };

    let btn_yes = TASKDIALOG_BUTTON { nButtonID: IDYES.0, pszButtonText: btn_yes_txt.as_pcwstr() };
    let btn_cancel = TASKDIALOG_BUTTON { nButtonID: IDCANCEL.0, pszButtonText: btn_cancel_txt.as_pcwstr() };
    let custom_btns = vec![btn_yes, btn_cancel];

    config.dwFlags = TDF_ENABLE_HYPERLINKS | TDF_USE_COMMAND_LINKS;
    config.cButtons = custom_btns.len() as u32;
    config.pButtons = custom_btns.as_ptr();
    config.pszWindowTitle = setup_name.as_pcwstr();
    config.pszMainInstruction = instruction.as_pcwstr();
    config.pszContent = content.as_pcwstr();
    config.Anonymous2.pszFooterIcon = TD_INFORMATION_ICON;
    config.pszFooter = footer.as_pcwstr();

    config.lpCallbackData = root_path as *const PathBuf as isize;
    config.pfCallback = Some(task_dialog_callback);

    let mut pnbutton = 0;
    let mut pnradiobutton = 0;
    let mut pfverificationflagchecked = FALSE;
    unsafe { TaskDialogIndirect(&config, Some(&mut pnbutton), Some(&mut pnradiobutton), Some(&mut pfverificationflagchecked)).ok() };
    pnbutton == IDYES.0
}

extern "system" fn task_dialog_callback(_hwnd: HWND, msg: TASKDIALOG_NOTIFICATIONS, _: WPARAM, _: LPARAM, lp_ref_data: isize) -> HRESULT {
    if msg == TDN_HYPERLINK_CLICKED {
        let raw = lp_ref_data as *const PathBuf;
        let path: &PathBuf = unsafe { &*raw };
        let dir = path.to_string_lossy().to_string();
        let dir = string_to_wide(dir);
        unsafe { ShellExecuteW(Some(GetDesktopWindow()), None, dir.as_pcwstr(), None, None, SW_SHOWDEFAULT) };
        return S_FALSE; // do not close dialog
    }
    return S_OK; // close dialog on button press
}

pub fn generate_confirm(
    title: &str,
    header: Option<&str>,
    body: &str,
    ok_text: Option<&str>,
    btns: DialogButton,
    ico: DialogIcon,
) -> Result<DialogResult> {
    let hparent = unsafe { GetDesktopWindow() };
    let mut ok_text_buf = string_to_wide_opt(ok_text);
    let mut custom_btns = if let Some(ok_text_buf) = ok_text_buf.as_mut() {
        let td_btn = TASKDIALOG_BUTTON { nButtonID: IDOK.0, pszButtonText: ok_text_buf.as_pcwstr() };
        vec![td_btn]
    } else {
        Vec::new()
    };

    let mut tdc = TASKDIALOGCONFIG { hwndParent: hparent, ..Default::default() };
    tdc.cbSize = std::mem::size_of::<TASKDIALOGCONFIG>() as u32;
    tdc.dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_POSITION_RELATIVE_TO_WINDOW;
    tdc.dwCommonButtons = btns.to_win();
    tdc.Anonymous1.pszMainIcon = ico.to_win();

    if !custom_btns.is_empty() {
        tdc.cButtons = custom_btns.len() as u32;
        tdc.pButtons = custom_btns.as_mut_ptr();
    }

    let title_buf = string_to_wide(title);
    tdc.pszWindowTitle = title_buf.as_pcwstr();

    let mut header_buf = string_to_wide_opt(header);
    if let Some(header_buf) = header_buf.as_mut() {
        tdc.pszMainInstruction = header_buf.as_pcwstr();
    }

    let body_buf = string_to_wide(body);
    tdc.pszContent = body_buf.as_pcwstr();

    let mut pnbutton = 0;
    unsafe { TaskDialogIndirect(&tdc, Some(&mut pnbutton), None, None).expect("didnt work") };
    Ok(DialogResult::from_win(pnbutton))
}

pub fn generate_alert(
    title: &str,
    header: Option<&str>,
    body: &str,
    ok_text: Option<&str>,
    btns: DialogButton,
    ico: DialogIcon,
) -> Result<()> {
    let _ = generate_confirm(title, header, body, ok_text, btns, ico)?;
    Ok(())
}

#[ignore]
#[test]
fn show_all_windows_dialogs() {
    use semver::Version;
    let app = Manifest {
        id: "test.app".to_string(),
        title: "Test Application".to_string(),
        version: semver::Version::new(1, 0, 0),
        description: "A test application for dialog generation.".to_string(),
        authors: "Test Author".to_string(),
        runtime_dependencies: "net8-x64".to_string(),
        ..Default::default()
    };

    show_restart_required(&app);
    show_update_missing_dependencies_dialog(&app, "net8-x64", &Version::new(1, 0, 0), &Version::new(2, 0, 0));
    show_setup_missing_dependencies_dialog(&app, "net8-x64");
    show_uninstall_complete_with_errors_dialog("Test Application", Some(&PathBuf::from("C:\\audio.log")));
    show_processes_locking_folder_dialog(&app.title, &app.version.to_string(), "TestProcess1, TestProcess2");
    show_overwrite_repair_dialog(&app, &PathBuf::from("C:\\Program Files\\TestApp"), false);
}
