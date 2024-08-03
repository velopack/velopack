#![windows_subsystem = "windows"]
use anyhow::Result;
use iced::theme::Theme;
use iced::widget::{Button, Column, Text};
use iced::{Command, Application};

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
    update_manager: Option<UpdateManager<sources::FileSource>>,
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
    let mut set = iced::Settings::default();
    set.window.size = iced::Size { width: 400.0, height: 200.0 };
    GUI::run(set)?;
    Ok(())
}

impl Application for GUI {
    type Message = Message;
    type Theme = Theme;
    type Executor = iced::executor::Default;
    type Flags = ();

    fn new(_flags: ()) -> (Self, Command<Self::Message>) {
        let sounce = sources::FileSource::new(env!("RELEASES_DIR"));
        let um = UpdateManager::new(sounce, None);
        let mut version: Option<String> = None;
        let mut state = GUIState::NotInstalled;
        if um.is_ok() {
            state = GUIState::Idle;
            version = Some(um.as_ref().unwrap().current_version().unwrap());
        }

        let gui = Self { update_manager: um.ok(), state: state, current_version: version, update_info: None, download_progress: 0 };
        (gui, iced::Command::none())
    }

    fn title(&self) -> String {
        String::from("Velopack Rust Demo")
    }

    fn update(&mut self, message: Self::Message) -> iced::Command<Self::Message> {
        match message {
            Message::CheckForUpdates => {
                self.state = GUIState::Checking;
                iced::Command::perform(self.update_manager.as_ref().unwrap().check_for_updates_async(), |result| match result {
                    Ok(update_info) => Message::UpdatesFound(update_info),
                    Err(_) => {
                        // Handle the error case, perhaps by logging or setting an error state
                        // For simplicity, we're sending a None update here, but you should handle errors appropriately
                        Message::UpdatesFound(None)
                    }
                })
            }
            Message::UpdatesFound(update) => {
                self.update_info = update;
                self.state = match self.update_info {
                    Some(_) => GUIState::UpdatesAvailable,
                    None => GUIState::Idle,
                };
                iced::Command::none()
            }
            Message::DownloadUpdates => {
                self.state = GUIState::Downloading;
                let update_info = self.update_info.clone().unwrap(); // Ensure you handle this safely in your actual code
                iced::Command::perform(self.update_manager.as_ref().unwrap().download_updates_async(&update_info, None), |_| Message::DownloadComplete)
            }
            Message::DownloadProgress(progress) => {
                self.download_progress = progress;
                iced::Command::none()
            }
            Message::DownloadComplete => {
                self.state = GUIState::ReadyToRestart;
                iced::Command::none()
            }
            Message::Restart => {
                let update_info = self.update_info.clone().unwrap(); // Ensure you handle this safely in your actual code
                self.update_manager.as_ref().unwrap().apply_updates_and_restart(update_info, RestartArgs::None).unwrap();
                iced::Command::none()
            }
        }
    }

    fn view(&self) -> iced::Element<Self::Message> {
        let content = match self.state {
            GUIState::NotInstalled => Column::new()
                .push(Text::new("Can't check for updates if not installed")),
            GUIState::Idle => Column::new()
                .push(Text::new(format!("Current version: {}", self.current_version.as_ref().unwrap_or(&"Unknown".to_string()))))
                .push(Button::new(Text::new("Check for updates")).on_press(Message::CheckForUpdates)),
            GUIState::Checking => Column::new()
                .push(Text::new("Checking for updates...")),
            GUIState::UpdatesAvailable => {
                let update_version = self.update_info.as_ref().map_or("Unknown", |info| &info.TargetFullRelease.Version);
                Column::new()
                    .push(Text::new(format!("Update available: {}", update_version)))
                    .push(Button::new(Text::new("Download updates")).on_press(Message::DownloadUpdates))
            },
            GUIState::Downloading => Column::new()
                .push(Text::new(format!("Downloading updates... Progress: {}%", self.download_progress))),
            GUIState::ReadyToRestart => Column::new()
                .push(Text::new("Updates downloaded. Ready to restart."))
                .push(Button::new(Text::new("Restart")).on_press(Message::Restart)),
        };
    
        content.into()
    }
}
