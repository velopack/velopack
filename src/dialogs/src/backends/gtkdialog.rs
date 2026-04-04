use std::sync::mpsc::{channel, Receiver, Sender, TryRecvError};
use std::sync::Mutex;
use std::time::Duration;

use gtk::prelude::*;

use crate::backends::{DialogManager, DialogProxy, XDialogError, XDialogIcon, XDialogOptions, XDialogResult};

#[derive(Debug, PartialEq)]
enum DialogRequest {
    Close,
    SetProgress(f32),
    SetIndeterminate,
    SetText(String),
}

struct ShowRequest {
    options: XDialogOptions,
    command_rx: Receiver<DialogRequest>,
    result_tx: oneshot::Sender<XDialogResult>,
}

/// Manages GTK3 dialogs. All GTK operations happen on a single dedicated thread.
pub struct GtkDialogManager {
    show_tx: Sender<ShowRequest>,
}

impl GtkDialogManager {
    pub fn new() -> Self {
        let (show_tx, show_rx) = channel::<ShowRequest>();

        std::thread::spawn(move || {
            if gtk::init().is_err() {
                error!("Failed to initialize GTK3. Dialogs will not be shown.");
                for req in show_rx.iter() {
                    let _ = req.result_tx.send(XDialogResult::WindowClosed);
                }
                return;
            }

            for request in show_rx.iter() {
                run_dialog(request);
            }
        });

        GtkDialogManager { show_tx }
    }
}

impl DialogManager for GtkDialogManager {
    fn show(&mut self, options: XDialogOptions) -> Result<Box<dyn DialogProxy>, XDialogError> {
        let (command_tx, command_rx) = channel::<DialogRequest>();
        let (result_tx, result_rx) = oneshot::channel::<XDialogResult>();

        self.show_tx
            .send(ShowRequest {
                options,
                command_rx,
                result_tx,
            })
            .map_err(|_| XDialogError::SystemError("GTK thread has exited".to_string()))?;

        Ok(Box::new(GtkDialogProxy {
            command_tx,
            state: Mutex::new(ProxyState::Pending(result_rx)),
        }))
    }
}

fn run_dialog(request: ShowRequest) {
    let ShowRequest {
        options,
        command_rx,
        result_tx,
    } = request;

    let has_progress = options.has_progress;

    // Main window
    let window = gtk::Window::new(gtk::WindowType::Toplevel);
    window.set_title(&options.title);
    window.set_default_size(420, -1);
    window.set_resizable(false);
    window.set_position(gtk::WindowPosition::Center);
    window.set_keep_above(true);

    // Root vertical box
    let vbox = gtk::Box::new(gtk::Orientation::Vertical, 12);
    vbox.set_margin_start(18);
    vbox.set_margin_end(18);
    vbox.set_margin_top(18);
    vbox.set_margin_bottom(18);

    // Header area: icon + text side-by-side
    let hbox = gtk::Box::new(gtk::Orientation::Horizontal, 12);

    let icon_name = match options.icon {
        XDialogIcon::Error => Some("dialog-error"),
        XDialogIcon::Warning => Some("dialog-warning"),
        XDialogIcon::Information => Some("dialog-information"),
        XDialogIcon::None => None,
    };
    if let Some(name) = icon_name {
        let image = gtk::Image::from_icon_name(Some(name), gtk::IconSize::Dialog);
        image.set_valign(gtk::Align::Start);
        hbox.pack_start(&image, false, false, 0);
    }

    let text_box = gtk::Box::new(gtk::Orientation::Vertical, 6);

    // Main instruction: bold, larger text
    if !options.main_instruction.is_empty() {
        let label = gtk::Label::new(None);
        label.set_markup(&format!(
            "<span size='large' weight='bold'>{}</span>",
            glib::markup_escape_text(&options.main_instruction)
        ));
        label.set_xalign(0.0);
        label.set_line_wrap(true);
        label.set_max_width_chars(50);
        label.set_selectable(true);
        label.set_can_focus(false);
        text_box.pack_start(&label, false, false, 0);
    }

    // Body message
    let content_label = gtk::Label::new(None);
    if !options.message.is_empty() {
        content_label.set_text(&options.message);
    }
    content_label.set_xalign(0.0);
    content_label.set_line_wrap(true);
    content_label.set_max_width_chars(50);
    content_label.set_selectable(true);
    content_label.set_can_focus(false);
    text_box.pack_start(&content_label, false, false, 0);

    hbox.pack_start(&text_box, true, true, 0);
    vbox.pack_start(&hbox, true, true, 0);

    // Progress bar (optional)
    let progress_bar = if has_progress {
        let pb = gtk::ProgressBar::new();
        pb.set_show_text(false);
        vbox.pack_start(&pb, false, false, 0);
        Some(pb)
    } else {
        None
    };

    // Separator before buttons
    if !options.buttons.is_empty() {
        let sep = gtk::Separator::new(gtk::Orientation::Horizontal);
        vbox.pack_start(&sep, false, false, 0);
    }

    // Shared flag to ensure gtk::main_quit() is only called once per dialog.
    // Calling it when no main loop is running causes a panic.
    let quit_called = std::rc::Rc::new(std::cell::Cell::new(false));

    // Buttons
    let result_value = std::rc::Rc::new(std::cell::Cell::new(None::<usize>));
    if !options.buttons.is_empty() {
        let button_box = gtk::ButtonBox::new(gtk::Orientation::Horizontal);
        button_box.set_layout(gtk::ButtonBoxStyle::End);
        button_box.set_spacing(6);

        for (idx, text) in options.buttons.iter().enumerate() {
            let button = gtk::Button::with_label(text);
            if idx == 0 {
                // Style the first button as suggested/default
                button.style_context().add_class("suggested-action");
            }
            let rv = result_value.clone();
            let qc = quit_called.clone();
            button.connect_clicked(move |_| {
                rv.set(Some(idx));
                if !qc.replace(true) {
                    gtk::main_quit();
                }
            });
            button_box.pack_start(&button, false, false, 0);
        }

        vbox.pack_start(&button_box, false, false, 0);
    }

    window.add(&vbox);

    // Handle window close via X button
    let rv = result_value.clone();
    let qc = quit_called.clone();
    window.connect_delete_event(move |_, _| {
        rv.set(None);
        if !qc.replace(true) {
            gtk::main_quit();
        }
        glib::Propagation::Proceed
    });

    window.show_all();

    // Poll the command channel periodically from the GTK main loop
    let is_indeterminate = std::rc::Rc::new(std::cell::Cell::new(false));
    let is_indeterminate_poll = is_indeterminate.clone();
    let progress_bar_poll = progress_bar.clone();
    let content_label_poll = content_label.clone();
    let qc = quit_called.clone();

    glib::timeout_add_local(Duration::from_millis(50), move || {
        loop {
            match command_rx.try_recv() {
                Ok(DialogRequest::Close) => {
                    if !qc.replace(true) {
                        gtk::main_quit();
                    }
                    return glib::ControlFlow::Break;
                }
                Ok(DialogRequest::SetProgress(val)) => {
                    if let Some(ref pb) = progress_bar_poll {
                        pb.set_fraction(val as f64);
                        is_indeterminate_poll.set(false);
                    }
                }
                Ok(DialogRequest::SetIndeterminate) => {
                    is_indeterminate_poll.set(true);
                }
                Ok(DialogRequest::SetText(text)) => {
                    content_label_poll.set_text(&text);
                }
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => {
                    if !qc.replace(true) {
                        gtk::main_quit();
                    }
                    return glib::ControlFlow::Break;
                }
            }
        }
        if is_indeterminate.get() {
            if let Some(ref pb) = progress_bar_poll {
                pb.pulse();
            }
        }
        glib::ControlFlow::Continue
    });

    gtk::main();

    let xresult = match result_value.get() {
        Some(idx) => XDialogResult::ButtonPressed(idx),
        None => XDialogResult::WindowClosed,
    };

    let _ = result_tx.send(xresult);

    // Use destroy (not close) to clean up without triggering delete_event
    unsafe {
        window.destroy();
    }
    while gtk::events_pending() {
        gtk::main_iteration_do(false);
    }
}

// -- Proxy implementation (mirrors TaskDialogProxy) --

enum ProxyState {
    Pending(oneshot::Receiver<XDialogResult>),
    Resolved(XDialogResult),
}

struct GtkDialogProxy {
    command_tx: Sender<DialogRequest>,
    state: Mutex<ProxyState>,
}

impl GtkDialogProxy {
    fn resolve(&self, timeout: Option<Duration>) -> Result<XDialogResult, XDialogError> {
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        match &*state {
            ProxyState::Resolved(result) => Ok(result.clone()),
            ProxyState::Pending(_) => {
                let ProxyState::Pending(rx) = std::mem::replace(&mut *state, ProxyState::Resolved(XDialogResult::WindowClosed)) else {
                    unreachable!()
                };
                let result = match timeout {
                    Some(d) => match rx.recv_timeout(d) {
                        Ok(r) => Ok(r),
                        Err(oneshot::RecvTimeoutError::Timeout) => Ok(XDialogResult::TimeoutElapsed),
                        Err(oneshot::RecvTimeoutError::Disconnected) => Err(XDialogError::NoResult(oneshot::RecvError)),
                    },
                    None => rx.recv().map_err(XDialogError::NoResult),
                };
                match &result {
                    Ok(r) => *state = ProxyState::Resolved(r.clone()),
                    Err(_) => *state = ProxyState::Resolved(XDialogResult::WindowClosed),
                }
                result
            }
        }
    }
}

impl DialogProxy for GtkDialogProxy {
    fn close(&self) {
        let _ = self.command_tx.send(DialogRequest::Close);
    }

    fn set_progress_value(&self, progress: f32) {
        let _ = self.command_tx.send(DialogRequest::SetProgress(progress));
    }

    fn set_progress_text(&self, text: &str) {
        let _ = self.command_tx.send(DialogRequest::SetText(text.to_string()));
    }

    fn set_progress_indeterminate(&self) {
        let _ = self.command_tx.send(DialogRequest::SetIndeterminate);
    }

    fn get_result(&self, timeout: Option<Duration>) -> Result<XDialogResult, XDialogError> {
        self.resolve(timeout)
    }

    fn set_progress_value_i16(&self, progress: i16) {
        let progress_f32 = (progress as f32) / 100f32;
        self.set_progress_value(progress_f32);
    }
}

impl Drop for GtkDialogProxy {
    fn drop(&mut self) {
        let _ = self.command_tx.send(DialogRequest::Close);
    }
}
