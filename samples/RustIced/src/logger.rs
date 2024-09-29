use crate::{AppState, Message};
use iced::futures::{channel::mpsc, sink::SinkExt, StreamExt};
use iced::{stream, Subscription};
use log::{Level, Log, Metadata, Record};
use std::sync::Mutex;

static LOG_RECEIVER: Mutex<Option<mpsc::UnboundedReceiver<String>>> = Mutex::new(None);

pub struct IcedLogger {
    sender: mpsc::UnboundedSender<String>,
}

impl IcedLogger {
    pub fn init()
    {
        let (sender, receiver) = mpsc::unbounded();
        log::set_boxed_logger(Box::new(IcedLogger { sender })).unwrap();
        log::set_max_level(log::LevelFilter::Info);

        let mut log_receiver = LOG_RECEIVER.lock().unwrap();
        *log_receiver = Some(receiver);
    }

    fn take_receiver() -> mpsc::UnboundedReceiver<String> {
        let mut log_receiver = LOG_RECEIVER.lock().unwrap();
        log_receiver.take().unwrap()
    }

    pub fn subscription(_: &AppState) -> Subscription<Message> {
        Subscription::run(|| {
            let mut log_receiver = Self::take_receiver();
            stream::channel(100, |mut output| async move {
                loop {
                    let message = log_receiver.select_next_some().await;
                    let _ = output.send(Message::LogReceived(message)).await;
                }
            })
        })
    }
}

impl Log for IcedLogger {
    fn enabled(&self, metadata: &Metadata) -> bool {
        metadata.level() <= Level::Info
    }

    fn log(&self, record: &Record) {
        if self.enabled(record.metadata()) {
            let log_msg = format!("{}", record.args());
            let _ = self.sender.unbounded_send(log_msg);
        }
    }

    fn flush(&self) {}
}