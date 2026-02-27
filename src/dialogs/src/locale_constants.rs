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
    EN_US_FTL = "en-US" => "../../../locales/en-US.ftl",
    RU_FTL    = "ru"    => "../../../locales/ru.ftl",
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
}

#[cfg(test)]
mod tests {
    use super::*;
    use fluent_bundle::concurrent::FluentBundle;
    use fluent_bundle::FluentResource;

    #[test]
    fn all_locale_keys_present_in_all_locales() {
        for (lang_tag, ftl_source) in LOCALE_SOURCES {
            let resource = FluentResource::try_new(ftl_source.to_string()).unwrap_or_else(|_| panic!("Failed to parse {lang_tag} FTL"));
            let lang_id: unic_langid::LanguageIdentifier = lang_tag.parse().unwrap();
            let mut bundle = FluentBundle::new_concurrent(vec![lang_id]);
            bundle.add_resource(resource).unwrap();

            for key in ALL_KEYS {
                assert!(bundle.get_message(key).is_some(), "Locale '{lang_tag}' is missing key '{key}'");
            }
        }
    }
}
