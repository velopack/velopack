use crate::locale_constants::*;
use crate::localization::format_message;
use fluent::FluentArgs;

enum MsiFontStyle {
    Normal,
    Title,
    Bigger,
    Emphasized,
}

impl MsiFontStyle {
    fn prefix(&self) -> &'static str {
        match self {
            MsiFontStyle::Normal => "",
            MsiFontStyle::Title => "{\\WixUI_Font_Title}",
            MsiFontStyle::Bigger => "{\\WixUI_Font_Bigger}",
            MsiFontStyle::Emphasized => "{\\WixUI_Font_Emphasized}",
        }
    }
}

/// Returns all MSI locale strings as (MSI property name, localized value) pairs.
/// The `app_title` parameter is used for strings that reference the application name.
/// Font formatting prefixes ({\WixUI_Font_*}) are applied automatically to the
/// appropriate strings so that localization authors don't need to include them.
pub fn locale_strings(app_title: &str) -> Vec<(&'static str, String)> {
    use MsiFontStyle::*;

    let mut args = FluentArgs::new();
    args.set("app_title", app_title.to_string());
    let with_app = Some(&args);

    // (property_name, fluent_key, needs_app_title, font_style)
    let entries: Vec<(&str, &str, bool, MsiFontStyle)> = vec![
        // Buttons (no formatting)
        ("MsiBtnBack", MSI_BTN_BACK, false, Normal),
        ("MsiBtnNext", MSI_BTN_NEXT, false, Normal),
        ("MsiBtnCancel", MSI_BTN_CANCEL, false, Normal),
        ("MsiBtnFinish", MSI_BTN_FINISH, false, Normal),
        ("MsiBtnOk", MSI_BTN_OK, false, Normal),
        ("MsiBtnYes", MSI_BTN_YES, false, Normal),
        ("MsiBtnNo", MSI_BTN_NO, false, Normal),
        ("MsiBtnRetry", MSI_BTN_RETRY, false, Normal),
        ("MsiBtnIgnore", MSI_BTN_IGNORE, false, Normal),
        // License dialog
        ("MsiLicenseTitle", MSI_LICENSE_TITLE, false, Title),
        ("MsiLicenseDescription", MSI_LICENSE_DESCRIPTION, false, Normal),
        ("MsiLicenseCheckbox", MSI_LICENSE_CHECKBOX, false, Normal),
        // Install scope dialog
        ("MsiScopeTitle", MSI_SCOPE_TITLE, false, Title),
        ("MsiScopeDescription", MSI_SCOPE_DESCRIPTION, false, Normal),
        ("MsiScopePerUser", MSI_SCOPE_PER_USER, false, Emphasized),
        ("MsiScopePerMachine", MSI_SCOPE_PER_MACHINE, false, Emphasized),
        ("MsiScopePerUserDescription", MSI_SCOPE_PER_USER_DESCRIPTION, false, Normal),
        ("MsiScopeNoPerUserDescription", MSI_SCOPE_NO_PER_USER_DESCRIPTION, false, Normal),
        ("MsiScopePerMachineDescription", MSI_SCOPE_PER_MACHINE_DESCRIPTION, false, Normal),
        // Verify ready dialog
        ("MsiReadyInstallTitle", MSI_READY_INSTALL_TITLE, true, Title),
        ("MsiReadyInstallText", MSI_READY_INSTALL_TEXT, false, Normal),
        ("MsiReadyRepairTitle", MSI_READY_REPAIR_TITLE, true, Title),
        ("MsiReadyRepairText", MSI_READY_REPAIR_TEXT, false, Normal),
        ("MsiReadyRemoveTitle", MSI_READY_REMOVE_TITLE, true, Title),
        ("MsiReadyRemoveText", MSI_READY_REMOVE_TEXT, true, Normal),
        ("MsiReadyUpdateTitle", MSI_READY_UPDATE_TITLE, true, Title),
        ("MsiReadyUpdateText", MSI_READY_UPDATE_TEXT, false, Normal),
        ("MsiReadyBtnInstall", MSI_READY_BTN_INSTALL, false, Normal),
        ("MsiReadyBtnRepair", MSI_READY_BTN_REPAIR, false, Normal),
        ("MsiReadyBtnRemove", MSI_READY_BTN_REMOVE, false, Normal),
        ("MsiReadyBtnUpdate", MSI_READY_BTN_UPDATE, false, Normal),
        // Progress dialog
        ("MsiProgressInstallingTitle", MSI_PROGRESS_INSTALLING_TITLE, true, Title),
        ("MsiProgressInstallingText", MSI_PROGRESS_INSTALLING_TEXT, true, Normal),
        ("MsiProgressRepairingTitle", MSI_PROGRESS_REPAIRING_TITLE, true, Title),
        ("MsiProgressRepairingText", MSI_PROGRESS_REPAIRING_TEXT, true, Normal),
        ("MsiProgressRemovingTitle", MSI_PROGRESS_REMOVING_TITLE, true, Title),
        ("MsiProgressRemovingText", MSI_PROGRESS_REMOVING_TEXT, true, Normal),
        ("MsiProgressUpdatingTitle", MSI_PROGRESS_UPDATING_TITLE, true, Title),
        ("MsiProgressUpdatingText", MSI_PROGRESS_UPDATING_TEXT, true, Normal),
        ("MsiProgressStatus", MSI_PROGRESS_STATUS, false, Normal),
        // Maintenance type dialog
        ("MsiMaintTypeTitle", MSI_MAINT_TYPE_TITLE, false, Title),
        ("MsiMaintTypeDescription", MSI_MAINT_TYPE_DESCRIPTION, false, Normal),
        ("MsiMaintRepairButton", MSI_MAINT_REPAIR_BUTTON, false, Normal),
        ("MsiMaintRepairTooltip", MSI_MAINT_REPAIR_TOOLTIP, false, Normal),
        ("MsiMaintRepairText", MSI_MAINT_REPAIR_TEXT, false, Normal),
        ("MsiMaintRepairDisabled", MSI_MAINT_REPAIR_DISABLED, false, Normal),
        ("MsiMaintRemoveButton", MSI_MAINT_REMOVE_BUTTON, false, Normal),
        ("MsiMaintRemoveTooltip", MSI_MAINT_REMOVE_TOOLTIP, false, Normal),
        ("MsiMaintRemoveText", MSI_MAINT_REMOVE_TEXT, true, Normal),
        ("MsiMaintRemoveDisabled", MSI_MAINT_REMOVE_DISABLED, false, Normal),
        // Disk cost strings (used by OutOfDiskDlg)
        ("MsiDiskCostTitle", MSI_DISK_COST_TITLE, false, Title),
        ("MsiDiskCostDescription", MSI_DISK_COST_DESCRIPTION, false, Normal),
        ("MsiDiskCostText", MSI_DISK_COST_TEXT, false, Normal),
        // Welcome dialog (full-page, uses Bigger)
        ("MsiWelcomeTitle", MSI_WELCOME_TITLE, true, Bigger),
        ("MsiWelcomeDescription", MSI_WELCOME_DESCRIPTION, true, Normal),
        ("MsiWelcomeUpdateDescription", MSI_WELCOME_UPDATE_DESCRIPTION, true, Normal),
        // Exit dialog (full-page, uses Bigger)
        ("MsiExitTitle", MSI_EXIT_TITLE, true, Bigger),
        ("MsiExitDescription", MSI_EXIT_DESCRIPTION, false, Normal),
        ("WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT", MSI_EXIT_LAUNCH_CHECKBOX, true, Normal),
        // Fatal error dialog (full-page, uses Bigger)
        ("MsiFatalTitle", MSI_FATAL_TITLE, true, Bigger),
        ("MsiFatalDescription1", MSI_FATAL_DESCRIPTION1, true, Normal),
        ("MsiFatalDescription2", MSI_FATAL_DESCRIPTION2, false, Normal),
        // User exit dialog (full-page, uses Bigger)
        ("MsiUserExitTitle", MSI_USER_EXIT_TITLE, true, Bigger),
        ("MsiUserExitDescription1", MSI_USER_EXIT_DESCRIPTION1, true, Normal),
        ("MsiUserExitDescription2", MSI_USER_EXIT_DESCRIPTION2, false, Normal),
        // Files in use dialog
        ("MsiFilesInUseTitle", MSI_FILES_IN_USE_TITLE, false, Title),
        ("MsiFilesInUseDescription", MSI_FILES_IN_USE_DESCRIPTION, false, Normal),
        ("MsiFilesInUseText", MSI_FILES_IN_USE_TEXT, false, Normal),
        ("MsiFilesInUseExit", MSI_FILES_IN_USE_EXIT, false, Normal),
        ("MsiRmFilesInUseTitle", MSI_RM_FILES_IN_USE_TITLE, false, Title),
        ("MsiRmFilesInUseDescription", MSI_RM_FILES_IN_USE_DESCRIPTION, false, Normal),
        ("MsiRmFilesInUseText", MSI_RM_FILES_IN_USE_TEXT, false, Normal),
        ("MsiRmFilesInUseUseRm", MSI_RM_FILES_IN_USE_USE_RM, false, Normal),
        ("MsiRmFilesInUseDontUseRm", MSI_RM_FILES_IN_USE_DONT_USE_RM, false, Normal),
        // Cancel dialog
        ("MsiCancelText", MSI_CANCEL_TEXT, true, Normal),
        // Error dialog
        ("MsiErrorDlgTitle", MSI_ERROR_DLG_TITLE, true, Normal),
        // Dialog title bar (used in Title attribute of dialogs)
        ("MsiDlgTitle", MSI_DLG_TITLE, true, Normal),
        // Shortcuts
        ("MsiDesktopShortcutDescription", MSI_DESKTOP_SHORTCUT_DESCRIPTION, true, Normal),
        ("MsiStartMenuShortcutDescription", MSI_START_MENU_SHORTCUT_DESCRIPTION, true, Normal),
    ];

    let mut result = Vec::with_capacity(entries.len());

    for (prop, key, needs_app, font) in entries {
        let text = if needs_app {
            format_message(key, with_app)
        } else {
            format_message(key, None)
        };
        let prefix = font.prefix();
        let value = if prefix.is_empty() { text } else { format!("{}{}", prefix, text) };
        result.push((prop, value));
    }

    result
}
