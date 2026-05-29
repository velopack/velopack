macro_rules! define_locale_keys {
    ($($const_name:ident = $key:literal),* $(,)?) => {
        $(pub const $const_name: &str = $key;)*
        #[allow(dead_code)]
        pub const ALL_KEYS: &[&str] = &[$($key),*];
    }
}

macro_rules! define_locales {
    ($($const_name:ident = $tag:literal => $path:literal),* $(,)?) => {
        $(pub(crate) const $const_name: &str = include_str!($path);)*
        #[allow(dead_code)]
        pub(crate) const LOCALE_SOURCES: &[(&str, &str)] = &[$(($tag, $const_name)),*];
    }
}

define_locales! {
    EN_US_FTL   = "en-US"    => "../../../locales/en-US.ftl",
    RU_FTL      = "ru"       => "../../../locales/ru.ftl",
    AR_FTL      = "ar"       => "../../../locales/ar.ftl",
    BG_FTL      = "bg"       => "../../../locales/bg.ftl",
    CA_FTL      = "ca"       => "../../../locales/ca.ftl",
    CS_FTL      = "cs"       => "../../../locales/cs.ftl",
    DA_FTL      = "da"       => "../../../locales/da.ftl",
    DE_FTL      = "de"       => "../../../locales/de.ftl",
    EL_FTL      = "el"       => "../../../locales/el.ftl",
    ES_FTL      = "es"       => "../../../locales/es.ftl",
    ET_FTL      = "et"       => "../../../locales/et.ftl",
    FI_FTL      = "fi"       => "../../../locales/fi.ftl",
    FR_FTL      = "fr"       => "../../../locales/fr.ftl",
    HE_FTL      = "he"       => "../../../locales/he.ftl",
    HI_FTL      = "hi"       => "../../../locales/hi.ftl",
    HR_FTL      = "hr"       => "../../../locales/hr.ftl",
    HU_FTL      = "hu"       => "../../../locales/hu.ftl",
    IT_FTL      = "it"       => "../../../locales/it.ftl",
    JA_FTL      = "ja"       => "../../../locales/ja.ftl",
    KK_FTL      = "kk"       => "../../../locales/kk.ftl",
    KO_FTL      = "ko"       => "../../../locales/ko.ftl",
    LT_FTL      = "lt"       => "../../../locales/lt.ftl",
    LV_FTL      = "lv"       => "../../../locales/lv.ftl",
    NB_FTL      = "nb"       => "../../../locales/nb.ftl",
    NL_FTL      = "nl"       => "../../../locales/nl.ftl",
    PL_FTL      = "pl"       => "../../../locales/pl.ftl",
    PT_BR_FTL   = "pt-BR"    => "../../../locales/pt-BR.ftl",
    PT_PT_FTL   = "pt-PT"    => "../../../locales/pt-PT.ftl",
    RO_FTL      = "ro"       => "../../../locales/ro.ftl",
    SK_FTL      = "sk"       => "../../../locales/sk.ftl",
    SL_FTL      = "sl"       => "../../../locales/sl.ftl",
    SQ_FTL      = "sq"       => "../../../locales/sq.ftl",
    SR_LATN_FTL = "sr-Latn"  => "../../../locales/sr-Latn.ftl",
    SV_FTL      = "sv"       => "../../../locales/sv.ftl",
    TH_FTL      = "th"       => "../../../locales/th.ftl",
    TR_FTL      = "tr"       => "../../../locales/tr.ftl",
    UK_FTL      = "uk"       => "../../../locales/uk.ftl",
    ZH_CN_FTL   = "zh-CN"    => "../../../locales/zh-CN.ftl",
    ZH_TW_FTL   = "zh-TW"    => "../../../locales/zh-TW.ftl",
}

define_locale_keys! {
    // Titles
    TITLE_UPDATE = "title-update",
    TITLE_SETUP = "title-setup",
    TITLE_UNINSTALL = "title-uninstall",
    ERROR_TITLE = "error-title",
    // Buttons
    BTN_CANCEL = "btn-cancel",
    BTN_INSTALL_UPDATE = "btn-install-update",
    BTN_INSTALL = "btn-install",
    BTN_UPDATE = "btn-update",
    BTN_DOWNGRADE = "btn-downgrade",
    BTN_REPAIR = "btn-repair",
    BTN_OPEN_LOG = "btn-open-log",
    BTN_OPEN_INSTALL_DIR = "btn-open-install-dir",
    BTN_OK = "btn-ok",
    BTN_HIDE = "btn-hide",
    // Bodies / messages (headers grouped with their body keys)
    ELEVATE_HEADER = "elevate-header",
    ELEVATE_BODY = "elevate-body",
    RESTART_HEADER = "restart-header",
    RESTART_BODY = "restart-body",
    MISSING_DEPS_HEADER = "missing-deps-header",
    MISSING_DEPS_BODY = "missing-deps-body",
    UNINSTALL_ERRORS_HEADER = "uninstall-errors-header",
    UNINSTALL_ERRORS_BODY = "uninstall-errors-body",
    UNINSTALL_ERRORS_LOG = "uninstall-errors-log",
    OVERWRITE_ALREADY_INSTALLED = "overwrite-already-installed",
    OVERWRITE_REPAIR_BODY = "overwrite-repair-body",
    OVERWRITE_OLDER_INSTALLED = "overwrite-older-installed",
    OVERWRITE_UPDATE_BODY = "overwrite-update-body",
    OVERWRITE_NEWER_INSTALLED = "overwrite-newer-installed",
    OVERWRITE_DOWNGRADE_BODY = "overwrite-downgrade-body",
    OVERWRITE_FOOTER = "overwrite-footer",
    UNINSTALL_HEADER = "uninstall-header",
    UNINSTALL_BODY = "uninstall-body",
    INSTALL_HOOK_HEADER = "install-hook-header",
    INSTALL_HOOK_BODY = "install-hook-body",
    SPLASH_HEADER = "splash-header",
    SPLASH_BODY = "splash-body",
    DEPS_DOWNLOAD_HEADER = "deps-download-header",
    DEPS_DOWNLOAD_BODY = "deps-download-body",
    APPLY_HEADER = "apply-header",
    APPLY_BODY = "apply-body",
    START_CORRUPT_HEADER = "start-corrupt-header",
    START_CORRUPT_BODY = "start-corrupt-body",
    ERROR_HEADER = "error-header",
    SETUP_ERROR_HEADER = "setup-error-header",
    // MSI Installer UI - Common
    MSI_DLG_TITLE = "msi-dlg-title",
    MSI_BTN_BACK = "msi-btn-back",
    MSI_BTN_NEXT = "msi-btn-next",
    MSI_BTN_CANCEL = "msi-btn-cancel",
    MSI_BTN_FINISH = "msi-btn-finish",
    MSI_BTN_OK = "msi-btn-ok",
    MSI_BTN_YES = "msi-btn-yes",
    MSI_BTN_NO = "msi-btn-no",
    MSI_BTN_RETRY = "msi-btn-retry",
    MSI_BTN_IGNORE = "msi-btn-ignore",
    // MSI Installer UI - Welcome
    MSI_WELCOME_TITLE = "msi-welcome-title",
    MSI_WELCOME_DESCRIPTION = "msi-welcome-description",
    MSI_WELCOME_UPDATE_DESCRIPTION = "msi-welcome-update-description",
    // MSI Installer UI - Exit
    MSI_EXIT_TITLE = "msi-exit-title",
    MSI_EXIT_DESCRIPTION = "msi-exit-description",
    MSI_EXIT_LAUNCH_CHECKBOX = "msi-exit-launch-checkbox",
    // MSI Installer UI - Prepare
    MSI_PREPARE_TITLE = "msi-prepare-title",
    MSI_PREPARE_DESCRIPTION = "msi-prepare-description",
    // MSI Installer UI - License
    MSI_LICENSE_TITLE = "msi-license-title",
    MSI_LICENSE_DESCRIPTION = "msi-license-description",
    MSI_LICENSE_CHECKBOX = "msi-license-checkbox",
    // MSI Installer UI - Readme
    MSI_README_TITLE = "msi-readme-title",
    MSI_README_DESCRIPTION = "msi-readme-description",
    // MSI Installer UI - Scope
    MSI_SCOPE_TITLE = "msi-scope-title",
    MSI_SCOPE_DESCRIPTION = "msi-scope-description",
    MSI_SCOPE_PER_USER = "msi-scope-per-user",
    MSI_SCOPE_PER_MACHINE = "msi-scope-per-machine",
    MSI_SCOPE_PER_USER_DESCRIPTION = "msi-scope-per-user-description",
    MSI_SCOPE_NO_PER_USER_DESCRIPTION = "msi-scope-no-per-user-description",
    MSI_SCOPE_PER_MACHINE_DESCRIPTION = "msi-scope-per-machine-description",
    // MSI Installer UI - Verify Ready
    MSI_READY_INSTALL_TITLE = "msi-ready-install-title",
    MSI_READY_INSTALL_TEXT = "msi-ready-install-text",
    MSI_READY_CHANGE_TITLE = "msi-ready-change-title",
    MSI_READY_CHANGE_TEXT = "msi-ready-change-text",
    MSI_READY_REPAIR_TITLE = "msi-ready-repair-title",
    MSI_READY_REPAIR_TEXT = "msi-ready-repair-text",
    MSI_READY_REMOVE_TITLE = "msi-ready-remove-title",
    MSI_READY_REMOVE_TEXT = "msi-ready-remove-text",
    MSI_READY_UPDATE_TITLE = "msi-ready-update-title",
    MSI_READY_UPDATE_TEXT = "msi-ready-update-text",
    MSI_READY_BTN_INSTALL = "msi-ready-btn-install",
    MSI_READY_BTN_CHANGE = "msi-ready-btn-change",
    MSI_READY_BTN_REPAIR = "msi-ready-btn-repair",
    MSI_READY_BTN_REMOVE = "msi-ready-btn-remove",
    MSI_READY_BTN_UPDATE = "msi-ready-btn-update",
    // MSI Installer UI - Progress
    MSI_PROGRESS_INSTALLING_TITLE = "msi-progress-installing-title",
    MSI_PROGRESS_INSTALLING_TEXT = "msi-progress-installing-text",
    MSI_PROGRESS_CHANGING_TITLE = "msi-progress-changing-title",
    MSI_PROGRESS_CHANGING_TEXT = "msi-progress-changing-text",
    MSI_PROGRESS_REPAIRING_TITLE = "msi-progress-repairing-title",
    MSI_PROGRESS_REPAIRING_TEXT = "msi-progress-repairing-text",
    MSI_PROGRESS_REMOVING_TITLE = "msi-progress-removing-title",
    MSI_PROGRESS_REMOVING_TEXT = "msi-progress-removing-text",
    MSI_PROGRESS_UPDATING_TITLE = "msi-progress-updating-title",
    MSI_PROGRESS_UPDATING_TEXT = "msi-progress-updating-text",
    MSI_PROGRESS_STATUS = "msi-progress-status",
    // MSI Installer UI - Maintenance Welcome
    MSI_MAINT_WELCOME_TITLE = "msi-maint-welcome-title",
    MSI_MAINT_WELCOME_DESCRIPTION = "msi-maint-welcome-description",
    // MSI Installer UI - Maintenance Type
    MSI_MAINT_TYPE_TITLE = "msi-maint-type-title",
    MSI_MAINT_TYPE_DESCRIPTION = "msi-maint-type-description",
    MSI_MAINT_CHANGE_BUTTON = "msi-maint-change-button",
    MSI_MAINT_CHANGE_TOOLTIP = "msi-maint-change-tooltip",
    MSI_MAINT_CHANGE_TEXT = "msi-maint-change-text",
    MSI_MAINT_CHANGE_DISABLED = "msi-maint-change-disabled",
    MSI_MAINT_REPAIR_BUTTON = "msi-maint-repair-button",
    MSI_MAINT_REPAIR_TOOLTIP = "msi-maint-repair-tooltip",
    MSI_MAINT_REPAIR_TEXT = "msi-maint-repair-text",
    MSI_MAINT_REPAIR_DISABLED = "msi-maint-repair-disabled",
    MSI_MAINT_REMOVE_BUTTON = "msi-maint-remove-button",
    MSI_MAINT_REMOVE_TOOLTIP = "msi-maint-remove-tooltip",
    MSI_MAINT_REMOVE_TEXT = "msi-maint-remove-text",
    MSI_MAINT_REMOVE_DISABLED = "msi-maint-remove-disabled",
    // MSI Installer UI - Cancel
    MSI_CANCEL_TEXT = "msi-cancel-text",
    // MSI Installer UI - Browse
    MSI_BROWSE_TITLE = "msi-browse-title",
    MSI_BROWSE_DESCRIPTION = "msi-browse-description",
    MSI_BROWSE_COMBO_LABEL = "msi-browse-combo-label",
    MSI_BROWSE_PATH_LABEL = "msi-browse-path-label",
    MSI_BROWSE_UP_TOOLTIP = "msi-browse-up-tooltip",
    MSI_BROWSE_NEW_FOLDER_TOOLTIP = "msi-browse-new-folder-tooltip",
    // MSI Installer UI - Invalid Directory
    MSI_INVALID_DIR_TEXT = "msi-invalid-dir-text",
    // MSI Installer UI - Disk Cost
    MSI_DISK_COST_TITLE = "msi-disk-cost-title",
    MSI_DISK_COST_DESCRIPTION = "msi-disk-cost-description",
    MSI_DISK_COST_TEXT = "msi-disk-cost-text",
    // MSI Installer UI - Error Dialog
    MSI_ERROR_DLG_TITLE = "msi-error-dlg-title",
    // MSI Installer UI - Fatal Error
    MSI_FATAL_TITLE = "msi-fatal-title",
    MSI_FATAL_DESCRIPTION1 = "msi-fatal-description1",
    MSI_FATAL_DESCRIPTION2 = "msi-fatal-description2",
    // MSI Installer UI - User Exit
    MSI_USER_EXIT_TITLE = "msi-user-exit-title",
    MSI_USER_EXIT_DESCRIPTION1 = "msi-user-exit-description1",
    MSI_USER_EXIT_DESCRIPTION2 = "msi-user-exit-description2",
    // MSI Installer UI - Files In Use
    MSI_FILES_IN_USE_TITLE = "msi-files-in-use-title",
    MSI_FILES_IN_USE_DESCRIPTION = "msi-files-in-use-description",
    MSI_FILES_IN_USE_TEXT = "msi-files-in-use-text",
    MSI_FILES_IN_USE_EXIT = "msi-files-in-use-exit",
    // MSI Installer UI - Restart Manager Files In Use
    MSI_RM_FILES_IN_USE_TITLE = "msi-rm-files-in-use-title",
    MSI_RM_FILES_IN_USE_DESCRIPTION = "msi-rm-files-in-use-description",
    MSI_RM_FILES_IN_USE_TEXT = "msi-rm-files-in-use-text",
    MSI_RM_FILES_IN_USE_USE_RM = "msi-rm-files-in-use-use-rm",
    MSI_RM_FILES_IN_USE_DONT_USE_RM = "msi-rm-files-in-use-dont-use-rm",
    // MSI Installer UI - Resume
    MSI_RESUME_TITLE = "msi-resume-title",
    MSI_RESUME_DESCRIPTION = "msi-resume-description",
    MSI_RESUME_BTN_INSTALL = "msi-resume-btn-install",
    // MSI Installer UI - Shortcut Descriptions
    MSI_DESKTOP_SHORTCUT_DESCRIPTION = "msi-desktop-shortcut-description",
    MSI_START_MENU_SHORTCUT_DESCRIPTION = "msi-start-menu-shortcut-description",
}

#[cfg(test)]
mod tests {
    use super::*;
    use fluent_bundle::concurrent::FluentBundle;
    use fluent_bundle::FluentResource;
    use unic_langid::LanguageIdentifier as UnicLanguageIdentifier;

    #[test]
    fn all_locale_keys_present_in_all_locales() {
        for (lang_tag, ftl_source) in LOCALE_SOURCES {
            let resource = FluentResource::try_new(ftl_source.to_string()).unwrap_or_else(|_| panic!("Failed to parse {lang_tag} FTL"));
            let lang_id: UnicLanguageIdentifier = lang_tag.parse().unwrap();
            let mut bundle = FluentBundle::new_concurrent(vec![lang_id]);
            bundle.add_resource(resource).unwrap();

            for key in ALL_KEYS {
                assert!(bundle.get_message(key).is_some(), "Locale '{lang_tag}' is missing key '{key}'");
            }
        }
    }
}
