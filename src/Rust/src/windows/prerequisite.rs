use super::{runtimes, splash};
use crate::shared::{bundle, dialogs, download};

use anyhow::Result;
use std::path::Path;
use winsafe::{self as w, co};

pub fn prompt_and_install_all_missing(app: &bundle::Manifest, updating_from: Option<&semver::Version>) -> Result<bool> {
    info!("Checking application pre-requisites...");
    let dependencies = super::runtimes::parse_dependency_list(&app.runtime_dependencies);
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
            if !dialogs::show_update_missing_dependencies_dialog(&app, &missing_str, &from_version, &app.version) {
                error!("User cancelled pre-requisite installation.");
                return Ok(false);
            }
        } else {
            if !dialogs::show_setup_missing_dependencies_dialog(&app, &missing_str) {
                error!("User cancelled pre-requisite installation.");
                return Ok(false);
            }
        }

        let downloads = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Downloads, co::KF::DONT_UNEXPAND, None)?;
        let downloads = Path::new(downloads.as_str());

        info!("Downloading {} missing pre-requisites...", missing.len());
        let quiet = dialogs::get_silent();

        for i in 0..missing.len() {
            let dep = &missing[i];
            let url = dep.get_download_url()?;
            let exe_name = downloads.join(dep.get_exe_name());

            if !exe_name.exists() {
                let window_title = if updating_from.is_some() { format!("{} Update", dep.display_name()) } else { format!("{} Setup", dep.display_name()) };
                let content = format!("Downloading {}...", dep.display_name());
                info!("    {}", content);

                let tx = splash::show_progress_dialog(window_title, content);
                let result = download::download_url_to_file(&url, &exe_name.to_str().unwrap(), |p| {
                    let _ = tx.send(p);
                });

                let _ = tx.send(splash::MSG_CLOSE);
                result?;
            }

            info!("    Installing {}...", dep.display_name());
            let result = dep.install(exe_name.to_str().unwrap(), quiet)?;
            if result == runtimes::RuntimeInstallResult::RestartRequired {
                warn!("A restart is required to complete the installation of {}.", dep.display_name());
                dialogs::show_restart_required(&app);
                return Ok(false);
            }
        }
    }

    Ok(true)
}
