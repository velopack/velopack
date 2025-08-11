use super::{runtimes, splash};
use crate::shared::dialogs;
use anyhow::Result;
use velopack::download;

pub fn prompt_and_install_all_missing(
    app_name: &str,
    app_version: &str,
    dependencies: &str,
    updating_from: Option<&semver::Version>,
) -> Result<bool> {
    info!("Checking application pre-requisites...");
    let dependencies = super::runtimes::parse_dependency_list(dependencies);
    let mut missing: Vec<&Box<dyn runtimes::RuntimeInfo>> = Vec::new();
    let mut missing_str = String::new();

    for i in 0..dependencies.len() {
        let dep = &dependencies[i];
        if dep.is_installed() {
            info!("    {} is already installed.", dep.display_name());
            continue;
        }
        info!("    {} is missing.", dep.display_name());
        if !missing.is_empty() {
            missing_str += ", ";
        }
        missing.push(dep);
        missing_str += dep.display_name();
    }

    if !missing.is_empty() {
        if let Some(from_version) = updating_from {
            if !dialogs::show_update_missing_dependencies_dialog(app_name, &missing_str, &from_version.to_string(), app_version) {
                error!("User cancelled pre-requisite installation.");
                return Ok(false);
            }
        } else {
            if !dialogs::show_setup_missing_dependencies_dialog(app_name, app_version, &missing_str) {
                error!("User cancelled pre-requisite installation.");
                return Ok(false);
            }
        }

        let downloads = super::known_path::get_downloads()?;

        info!("Downloading {} missing pre-requisites...", missing.len());
        let quiet = dialogs::get_silent();

        for i in 0..missing.len() {
            let dep = &missing[i];
            let url = dep.get_download_url()?;
            let exe_path = downloads.join(dep.get_exe_name());

            if !exe_path.exists() {
                let window_title = if updating_from.is_some() {
                    format!("{} Update", dep.display_name())
                } else {
                    format!("{} Setup", dep.display_name())
                };
                let content = format!("Downloading {}...", dep.display_name());
                info!("    {}", content);

                let tx = splash::show_progress_dialog(window_title, content);
                let result = download::download_url_to_file(&url, &exe_path, |p| {
                    let _ = tx.send(p);
                });

                let _ = tx.send(splash::MSG_CLOSE);
                result?;
            }

            info!("    Installing {}...", dep.display_name());
            let result = dep.install(&exe_path, quiet)?;
            if result == runtimes::RuntimeInstallResult::RestartRequired {
                warn!("A restart is required to complete the installation of {}.", dep.display_name());
                dialogs::show_restart_required(&app_name, app_version);
                return Ok(false);
            }
        }
    }

    Ok(true)
}
