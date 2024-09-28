#![windows_subsystem = "windows"]
use anyhow::Result;
use iced::widget::{Button, Column, Text};

use velopack::*;

#[derive(Debug, Clone)]
pub enum Message {
    CheckForUpdates,
    UpdatesFound(Option<UpdateInfo>),
    DownloadUpdates,
    DownloadProgress(i16),
    DownloadComplete,
    Restart,
}

pub struct GUI {
    update_manager: Option<UpdateManager>,
    state: GUIState,
    current_version: Option<String>,
    update_info: Option<UpdateInfo>,
    download_progress: i16,
}

#[derive(Debug, Clone)]
pub enum GUIState {
    NotInstalled,
    Idle,
    Checking,
    UpdatesAvailable,
    Downloading,
    ReadyToRestart,
}

fn main() -> Result<()> {
    VelopackApp::build().run();

    let source = sources::FileSource::new(env!("RELEASES_DIR"));
    let um = UpdateManager::new(source, None, None);
    let mut version: Option<String> = None;
    let mut state = GUIState::NotInstalled;
    if um.is_ok() {
        state = GUIState::Idle;
        version = Some(um.as_ref().unwrap().current_version().unwrap());
    }

    let gui = GUI { update_manager: um.ok(), state, current_version: version, update_info: None, download_progress: 0 };
    
    iced::application("A cool application", update, view)
        .window_size(iced::Size::new(400.0, 200.0))
        .run_with(move || (gui, iced::Task::none()))?;

    Ok(())
}

fn update(gui: &mut GUI, message: Message) -> iced::Task<Message> {
    match message {
        Message::CheckForUpdates => {
            gui.state = GUIState::Checking;
            iced::Task::perform(gui.update_manager.as_ref().unwrap().check_for_updates_async(), |result| match result {
                Ok(update_info) => {
                    match update_info {
                        UpdateCheck::RemoteIsEmpty => Message::UpdatesFound(None),
                        UpdateCheck::NoUpdateAvailable => Message::UpdatesFound(None),
                        UpdateCheck::UpdateAvailable(updates) => Message::UpdatesFound(Some(updates)),
                    }
                },
                Err(_) => {
                    // Handle the error case, perhaps by logging or setting an error state
                    // For simplicity, we're sending a None update here, but you should handle errors appropriately
                    Message::UpdatesFound(None)
                }
            })
        }
        Message::UpdatesFound(update) => {
            gui.update_info = update;
            gui.state = match gui.update_info {
                Some(_) => GUIState::UpdatesAvailable,
                None => GUIState::Idle,
            };
            iced::Task::none()
        }
        Message::DownloadUpdates => {
            gui.state = GUIState::Downloading;
            let update_info = gui.update_info.clone().unwrap(); // Ensure you handle this safely in your actual code
            iced::Task::perform(gui.update_manager.as_ref().unwrap().download_updates_async(&update_info, None), |_| Message::DownloadComplete)
        }
        Message::DownloadProgress(progress) => {
            gui.download_progress = progress;
            iced::Task::none()
        }
        Message::DownloadComplete => {
            gui.state = GUIState::ReadyToRestart;
            iced::Task::none()
        }
        Message::Restart => {
            let update_info = gui.update_info.clone().unwrap(); // Ensure you handle this safely in your actual code
            gui.update_manager.as_ref().unwrap().apply_updates_and_restart(update_info).unwrap();
            iced::Task::none()
        }
    }
}

fn view(gui: &GUI) -> iced::Element<Message> {
    let content = match gui.state {
        GUIState::NotInstalled => Column::new()
            .push(Text::new("Can't check for updates if not installed")),
        GUIState::Idle => Column::new()
            .push(Text::new(format!("Current version: {}", gui.current_version.as_ref().unwrap_or(&"Unknown".to_string()))))
            .push(Button::new(Text::new("Check for updates")).on_press(Message::CheckForUpdates)),
        GUIState::Checking => Column::new()
            .push(Text::new("Checking for updates...")),
        GUIState::UpdatesAvailable => {
            let update_version = gui.update_info.as_ref().map_or("Unknown", |info| &info.TargetFullRelease.Version);
            Column::new()
                .push(Text::new(format!("Update available: {}", update_version)))
                .push(Button::new(Text::new("Download updates")).on_press(Message::DownloadUpdates))
        }
        GUIState::Downloading => Column::new()
            .push(Text::new(format!("Downloading updates... Progress: {}%", gui.download_progress))),
        GUIState::ReadyToRestart => Column::new()
            .push(Text::new("Updates downloaded. Ready to restart."))
            .push(Button::new(Text::new("Restart")).on_press(Message::Restart)),
    };

    content.into()
}
