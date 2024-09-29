#![windows_subsystem = "windows"]

mod logger;

use anyhow::Result;
use iced::widget::{button, column, container, scrollable, text, vertical_space};
use iced::Task;
use velopack::*;

#[derive(Debug, Clone)]
pub enum Message {
    CheckForUpdates,
    UpdatesFound(Option<UpdateInfo>),
    DownloadUpdates,
    DownloadProgress(i16),
    DownloadComplete,
    Restart,
    LogReceived(String),
}

pub struct AppState {
    update_manager: Option<UpdateManager>,
    status: AppStatus,
    current_version: Option<String>,
    update_info: Option<UpdateInfo>,
    download_progress: i16,
    logs: Vec<String>,
}

impl AppState {
    pub fn new() -> (Self, Task<Message>) {
        let source = sources::FileSource::new(env!("RELEASES_DIR"));
        let um = UpdateManager::new(source, None, None);
        let mut version: Option<String> = None;
        let mut state = AppStatus::NotInstalled;
        if um.is_ok() {
            state = AppStatus::Idle;
            version = Some(um.as_ref().unwrap().current_version().unwrap());
        }

        (
            AppState {
                logs: Vec::new(),
                update_manager: um.ok(),
                status: state,
                current_version: version,
                update_info: None,
                download_progress: 0,
            },
            Task::none(),
        )
    }
}

#[derive(Debug, Clone)]
pub enum AppStatus {
    NotInstalled,
    Idle,
    Checking,
    UpdatesAvailable,
    Downloading,
    ReadyToRestart,
}

fn main() -> Result<()> {
    logger::IcedLogger::init();
    VelopackApp::build().run();

    iced::application("Velopack Rust Sample", update, view)
        .window_size(iced::Size::new(600.0, 400.0))
        .centered()
        .subscription(logger::IcedLogger::subscription)
        .run_with(|| AppState::new())?;
    
    Ok(())
}

fn update(state: &mut AppState, message: Message) -> Task<Message> {
    match message {
        Message::CheckForUpdates => {
            state.status = AppStatus::Checking;
            Task::perform(state.update_manager.as_ref().unwrap().check_for_updates_async(), |result| match result {
                Ok(update_info) => {
                    match update_info {
                        UpdateCheck::RemoteIsEmpty => Message::UpdatesFound(None),
                        UpdateCheck::NoUpdateAvailable => Message::UpdatesFound(None),
                        UpdateCheck::UpdateAvailable(updates) => Message::UpdatesFound(Some(updates)),
                    }
                }
                Err(_) => {
                    // Handle the error case, perhaps by logging or setting an error state
                    // For simplicity, we're sending a None update here, but you should handle errors appropriately
                    Message::UpdatesFound(None)
                }
            })
        }
        Message::UpdatesFound(update) => {
            state.update_info = update;
            state.status = match state.update_info {
                Some(_) => AppStatus::UpdatesAvailable,
                None => AppStatus::Idle,
            };
            Task::none()
        }
        Message::DownloadUpdates => {
            state.status = AppStatus::Downloading;
            let update_info = state.update_info.clone().unwrap(); // Ensure you handle this safely in your actual code
            Task::perform(state.update_manager.as_ref().unwrap().download_updates_async(&update_info, None), |_| Message::DownloadComplete)
        }
        Message::DownloadProgress(progress) => {
            state.download_progress = progress;
            Task::none()
        }
        Message::DownloadComplete => {
            state.status = AppStatus::ReadyToRestart;
            Task::none()
        }
        Message::Restart => {
            let update_info = state.update_info.clone().unwrap(); // Ensure you handle this safely in your actual code
            state.update_manager.as_ref().unwrap().apply_updates_and_restart(update_info).unwrap();
            Task::none()
        }
        Message::LogReceived(log) => {
            state.logs.push(log);
            Task::none()
        }
    }
}

fn view(state: &AppState) -> iced::Element<Message> {
    let content = match state.status {
        AppStatus::NotInstalled =>
            column![text("Can't check for updates if not installed")],
        AppStatus::Idle =>
            column![
                text(format!("Current version: {}", state.current_version.as_ref().unwrap_or(&"Unknown".to_string()))),
                button("Check for updates").on_press(Message::CheckForUpdates),
            ],
        AppStatus::Checking =>
            column![text("Checking for updates...")],
        AppStatus::UpdatesAvailable => {
            let update_version = state.update_info.as_ref().map_or("Unknown", |info| &info.TargetFullRelease.Version);
            column![
                text(format!("Update available: {}", update_version)),
                button("Download updates").on_press(Message::DownloadUpdates),
            ]
        }
        AppStatus::Downloading =>
            column![text(format!("Downloading updates... Progress: {}%", state.download_progress))],
        AppStatus::ReadyToRestart =>
            column![
                text("Updates downloaded. Ready to restart."),
                button("Restart").on_press(Message::Restart),
            ],
    };
    
    let log_area = scrollable(text(state.logs.join("\n")))
        .height(iced::Length::Fill)
        .width(iced::Length::Fill);
    
    let log_container = container(log_area)
        .padding(10)
        .width(iced::Length::Fill)
        .height(iced::Length::Fill)
        .style(|_| {
            container::Style {
                background: Some(iced::Color::from_rgb8(0x77, 0x1d, 0x1d).into()),
                text_color: Some(iced::Color::WHITE),
                ..Default::default()
            }
        });

    column![
        content,
        vertical_space().height(20),
        log_container,
    ].into()
}
