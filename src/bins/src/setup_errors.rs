use crate::dialogs::locale_strings;

/// Known user-facing setup failures. `Display` is always English so log files stay
/// readable for supportability; [`SetupError::localized_body`] resolves the system-locale
/// translation for the error dialog shown at the setup.exe catch-point.
#[derive(Debug, thiserror::Error)]
pub enum SetupError {
    #[error("This installer requires Windows 7 SP1 or later and cannot run.")]
    WindowsVersionUnsupported,
    #[error("Could not find embedded zip file. Please contact the application author.")]
    EmbeddedZipMissing,
    #[error("{app_title} requires at least {required_space} disk space to be installed. There is only {available_space} available.")]
    InsufficientDiskSpace {
        app_title: String,
        required_space: String,
        available_space: String,
    },
    #[error("This application requires Windows {os_version} or later.")]
    OsVersionRequired { app_title: String, os_version: String },
    #[error("This application ({machine_arch}) does not support your CPU architecture.")]
    CpuArchUnsupported { app_title: String, machine_arch: String },
    #[error("Failed to stop application ({error}), please close the application and try running the installer again.")]
    StopApplicationFailed { app_title: String, error: String },
    #[error(
        "Failed to remove existing application directory, please close the application and try running the installer again. \
        If the issue persists, try uninstalling first via Programs & Features, or restarting your computer."
    )]
    RemoveExistingDirFailed { app_title: String },
    #[error("This installer is missing a critical binary (Update.exe). Please contact the application author.")]
    UpdateExeMissing { app_title: String },
    #[error("The main executable could not be found in the package. Please contact the application author.")]
    MainExeMissing { app_title: String },
}

impl SetupError {
    /// The app title from the package manifest, or `None` for errors raised before the manifest is available.
    pub fn app_title(&self) -> Option<&str> {
        match self {
            SetupError::WindowsVersionUnsupported | SetupError::EmbeddedZipMissing => None,
            SetupError::InsufficientDiskSpace { app_title, .. }
            | SetupError::OsVersionRequired { app_title, .. }
            | SetupError::CpuArchUnsupported { app_title, .. }
            | SetupError::StopApplicationFailed { app_title, .. }
            | SetupError::RemoveExistingDirFailed { app_title }
            | SetupError::UpdateExeMissing { app_title }
            | SetupError::MainExeMissing { app_title } => Some(app_title),
        }
    }

    /// The localized (system locale) message to show in the setup error dialog.
    pub fn localized_body(&self) -> String {
        match self {
            SetupError::WindowsVersionUnsupported => locale_strings::setup_windows_version_unsupported(),
            SetupError::EmbeddedZipMissing => locale_strings::setup_embedded_zip_missing(),
            SetupError::InsufficientDiskSpace {
                app_title,
                required_space,
                available_space,
            } => locale_strings::setup_disk_space_insufficient(app_title, required_space, available_space),
            SetupError::OsVersionRequired { os_version, .. } => locale_strings::setup_os_version_required(os_version),
            SetupError::CpuArchUnsupported { machine_arch, .. } => locale_strings::setup_cpu_arch_unsupported(machine_arch),
            SetupError::StopApplicationFailed { error, .. } => locale_strings::setup_stop_app_failed(error),
            SetupError::RemoveExistingDirFailed { .. } => locale_strings::setup_remove_dir_failed(),
            SetupError::UpdateExeMissing { .. } => locale_strings::setup_update_exe_missing(),
            SetupError::MainExeMissing { .. } => locale_strings::setup_main_exe_missing(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn all_variants() -> Vec<SetupError> {
        vec![
            SetupError::WindowsVersionUnsupported,
            SetupError::EmbeddedZipMissing,
            SetupError::InsufficientDiskSpace {
                app_title: "MyApp".to_string(),
                required_space: "832.98 MB".to_string(),
                available_space: "1.00 MB".to_string(),
            },
            SetupError::OsVersionRequired {
                app_title: "MyApp".to_string(),
                os_version: "10.0.19041".to_string(),
            },
            SetupError::CpuArchUnsupported {
                app_title: "MyApp".to_string(),
                machine_arch: "arm64".to_string(),
            },
            SetupError::StopApplicationFailed {
                app_title: "MyApp".to_string(),
                error: "access denied".to_string(),
            },
            SetupError::RemoveExistingDirFailed {
                app_title: "MyApp".to_string(),
            },
            SetupError::UpdateExeMissing {
                app_title: "MyApp".to_string(),
            },
            SetupError::MainExeMissing {
                app_title: "MyApp".to_string(),
            },
        ]
    }

    #[test]
    fn display_strings_are_english() {
        assert_eq!(
            SetupError::WindowsVersionUnsupported.to_string(),
            "This installer requires Windows 7 SP1 or later and cannot run."
        );
        assert_eq!(
            SetupError::InsufficientDiskSpace {
                app_title: "MyApp".to_string(),
                required_space: "832.98 MB".to_string(),
                available_space: "1.00 MB".to_string(),
            }
            .to_string(),
            "MyApp requires at least 832.98 MB disk space to be installed. There is only 1.00 MB available."
        );
        assert_eq!(
            SetupError::OsVersionRequired {
                app_title: "MyApp".to_string(),
                os_version: "10.0.19041".to_string()
            }
            .to_string(),
            "This application requires Windows 10.0.19041 or later."
        );
        assert_eq!(
            SetupError::RemoveExistingDirFailed {
                app_title: "MyApp".to_string()
            }
            .to_string(),
            "Failed to remove existing application directory, please close the application and try running the installer again. \
            If the issue persists, try uninstalling first via Programs & Features, or restarting your computer."
        );
    }

    #[test]
    fn localized_body_resolves_for_all_variants() {
        // The bundle follows the machine's system locale, so only assert locale-independent
        // properties: non-empty, not the raw fluent key id, and args are interpolated.
        crate::dialogs::init();
        for err in all_variants() {
            let body = err.localized_body();
            assert!(!body.is_empty(), "empty body for {err:?}");
            assert!(!body.starts_with("setup-"), "unresolved fluent key for {err:?}: {body}");
        }

        let disk = SetupError::InsufficientDiskSpace {
            app_title: "MyApp".to_string(),
            required_space: "832.98 MB".to_string(),
            available_space: "1.00 MB".to_string(),
        }
        .localized_body();
        assert!(disk.contains("MyApp"), "{disk}");
        assert!(disk.contains("832.98 MB"), "{disk}");
        assert!(disk.contains("1.00 MB"), "{disk}");

        let os = SetupError::OsVersionRequired {
            app_title: "MyApp".to_string(),
            os_version: "10.0.19041".to_string(),
        }
        .localized_body();
        assert!(os.contains("10.0.19041"), "{os}");

        let arch = SetupError::CpuArchUnsupported {
            app_title: "MyApp".to_string(),
            machine_arch: "arm64".to_string(),
        }
        .localized_body();
        assert!(arch.contains("arm64"), "{arch}");

        let stop = SetupError::StopApplicationFailed {
            app_title: "MyApp".to_string(),
            error: "access denied".to_string(),
        }
        .localized_body();
        assert!(stop.contains("access denied"), "{stop}");
    }

    #[test]
    fn app_title_is_none_only_for_pre_manifest_errors() {
        for err in all_variants() {
            match err {
                SetupError::WindowsVersionUnsupported | SetupError::EmbeddedZipMissing => {
                    assert_eq!(err.app_title(), None, "{err:?}")
                }
                _ => assert_eq!(err.app_title(), Some("MyApp"), "{err:?}"),
            }
        }
    }

    #[test]
    fn downcast_roundtrip() {
        use anyhow::Context;
        let err: anyhow::Error = SetupError::MainExeMissing {
            app_title: "MyApp".to_string(),
        }
        .into();
        let err = err.context("installation failed");
        let setup_err = err
            .downcast_ref::<SetupError>()
            .expect("SetupError should survive anyhow conversion and context");
        assert_eq!(setup_err.app_title(), Some("MyApp"));
    }
}
