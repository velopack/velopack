use anyhow::{bail, Result};
use image::{codecs::gif::GifDecoder, io::Reader as ImageReader, AnimationDecoder, DynamicImage, ImageFormat};
use std::{
    cell::RefCell,
    io::Cursor,
    rc::Rc,
    sync::mpsc::{self, Receiver, Sender},
    thread,
    time::Duration,
};
use w::WString;
use winsafe::{self as w, co, guard::DeleteObjectGuard, gui, prelude::*};

const TMR_GIF: usize = 1;
const MSG_NOMESSAGE: i16 = -99;

pub const MSG_CLOSE: i16 = -1;
// pub const MSG_INDEFINITE: i16 = -2;

pub fn show_progress_dialog<T1: AsRef<str>, T2: AsRef<str>>(window_title: T1, content: T2) -> Sender<i16> {
    let window_title = window_title.as_ref().to_string();
    let content = content.as_ref().to_string();
    let (tx, rx) = mpsc::channel::<i16>();
    thread::spawn(move || {
        show_com_ctl_progress_dialog(rx, &window_title, &content);
    });
    tx
}

pub fn show_splash_dialog(app_name: String, imgstream: Option<Vec<u8>>, delay: bool) -> Sender<i16> {
    let (tx, rx) = mpsc::channel::<i16>();
    let tx2 = tx.clone();
    thread::spawn(move || {
        if delay {
            info!("Showing splash screen with 3 second delay...");
            // wait a bit, if the MSG_CLOSE message is sent within 3 seconds, we won't bother showing it.
            thread::sleep(Duration::from_millis(3000));

            // read all messages, checking for MSG_CLOSE, and then send the last progress message we received.
            let mut last_progress = 0;
            let mut closed = false;
            loop {
                let msg = rx.try_recv().unwrap_or(MSG_NOMESSAGE);
                if msg == MSG_CLOSE {
                    closed = true;
                    break;
                } else if msg >= 0 {
                    last_progress = msg;
                } else if msg == MSG_NOMESSAGE {
                    break;
                }
            }

            if closed {
                info!("Splash screen received MSG_CLOSE before delay ended, so it wasn't shown.");
                return;
            }

            tx2.send(last_progress).unwrap();

            info!("Splash screen delay ended, showing splash window...");
        } else {
            info!("Showing splash screen immediately...");
        }

        if imgstream.is_some() {
            let _ = SplashWindow::new(app_name, imgstream.unwrap(), rx).and_then(|w| {
                w.run()?;
                Ok(())
            });
        } else {
            let setup_name = format!("{} Setup", app_name);
            let content = "Please Wait...";
            show_com_ctl_progress_dialog(rx, setup_name.as_str(), content);
        }
    });
    tx
}

#[derive(Clone)]
pub struct SplashWindow {
    wnd: gui::WindowMain,
    frames: Rc<Vec<DeleteObjectGuard<w::HBITMAP>>>,
    rx: Rc<Receiver<i16>>,
    delay: u16,
    progress: Rc<RefCell<i16>>,
    frame_idx: Rc<RefCell<usize>>,
    w: u16,
    h: u16,
}

fn average(numbers: &[u16]) -> u16 {
    let sum: u16 = numbers.iter().sum();
    let count = numbers.len() as u16;
    sum / count
}

fn convert_rgba_to_bgra(image_data: &mut Vec<u8>) {
    for chunk in image_data.chunks_mut(4) {
        // Swap red and blue channels
        let tmp = chunk[0];
        chunk[0] = chunk[2];
        chunk[2] = tmp;
    }
}

impl SplashWindow {
    pub fn new(app_name: String, img_stream: Vec<u8>, rx: Receiver<i16>) -> Result<Self> {
        let mut delays = Vec::new();
        let mut frames = Vec::new();
        let fmt_cursor = Cursor::new(&img_stream);
        let fmt_reader = ImageReader::new(fmt_cursor).with_guessed_format()?;
        let fmt = fmt_reader.format();

        let dims = &fmt_reader.into_dimensions()?;
        let w: u16 = u16::try_from(dims.0)?;
        let h: u16 = u16::try_from(dims.1)?;

        if Some(ImageFormat::Gif) == fmt {
            info!("Image is animated GIF ({}x{}), loading frames...", w, h);
            let gif_cursor = Cursor::new(&img_stream);
            let decoder = GifDecoder::new(gif_cursor)?;
            let dec_frames = decoder.into_frames();
            for frame in dec_frames.into_iter() {
                let frame = frame?;
                let (num, dem) = frame.delay().numer_denom_ms();
                delays.push((num / dem) as u16);
                let dynamic = DynamicImage::from(frame.buffer().to_owned());
                let mut vec = dynamic.to_rgba8().to_vec();
                convert_rgba_to_bgra(&mut vec);
                let bitmap = w::HBITMAP::CreateBitmap(winsafe::SIZE { cx: w.into(), cy: h.into() }, 1, 32, vec.as_mut_ptr() as *mut u8)?;
                frames.push(bitmap);
            }
            info!("Successfully loaded {} frames.", frames.len());
        } else {
            info!("Loading static image (detected {:?})...", fmt);
            delays.push(16); // 60 fps
            let img_cursor = Cursor::new(&img_stream);
            let img_decoder = ImageReader::new(img_cursor).with_guessed_format()?.decode()?;
            let mut vec = img_decoder.to_rgba8().to_vec();
            convert_rgba_to_bgra(&mut vec);
            let bitmap = w::HBITMAP::CreateBitmap(winsafe::SIZE { cx: w.into(), cy: h.into() }, 1, 32, vec.as_mut_ptr() as *mut u8)?;
            frames.push(bitmap);
            info!("Successfully loaded.");
        }

        // TODO: only support a fixed frame delay for now. Maybe should
        // support a variable frame delay in the future.
        let delay = average(&delays);

        let wnd = gui::WindowMain::new(gui::WindowMainOpts {
            class_icon: gui::Icon::Idi(co::IDI::APPLICATION),
            class_cursor: gui::Cursor::Idc(co::IDC::APPSTARTING),
            class_style: co::CS::HREDRAW | co::CS::VREDRAW,
            class_name: "VelopackSetupSplashWindow".to_owned(),
            title: app_name,
            size: (w.into(), h.into()),
            ex_style: co::WS_EX::NoValue,
            style: co::WS::POPUP,
            ..Default::default()
        });

        let frames = Rc::new(frames);
        let rx = Rc::new(rx);
        let progress = Rc::new(RefCell::new(0));
        let frame_idx = Rc::new(RefCell::new(0));
        let mut new_self = Self { wnd, frames, delay, frame_idx, w, h, rx, progress };
        new_self.events();
        Ok(new_self)
    }

    pub fn run(&self) -> Result<i32> {
        let res = self.wnd.run_main(Some(co::SW::SHOWNOACTIVATE));
        if res.is_err() {
            error!("Error Showing Splash Window: {:?}", res);
            bail!("Error Showing Splash Window: {:?}", res);
        } else {
            info!("Splash Window Closed");
        }
        Ok(res.unwrap())
    }

    fn events(&mut self) {
        let self2 = self.clone();
        self.wnd.on().wm_create(move |_m| {
            // will ask Windows to give us a WM_TIMER every `delay` milliseconds
            self2.wnd.hwnd().SetTimer(TMR_GIF, self2.delay.into(), None)?;
            Ok(0)
        });

        self.wnd.on().wm_nc_hit_test(|_m| {
            Ok(co::HT::CAPTION) // make the window draggable
        });

        let self2 = self.clone();
        self.wnd.on().wm_timer(TMR_GIF, move || {
            // handle any incoming messages before painting
            loop {
                let msg = self2.rx.try_recv().unwrap_or(MSG_NOMESSAGE);
                if msg == MSG_NOMESSAGE {
                    break;
                } else if msg == MSG_CLOSE {
                    self2.wnd.hwnd().SendMessage(w::msg::wm::Close {});
                    return Ok(());
                } else if msg >= 0 {
                    let mut p = self2.progress.borrow_mut();
                    *p = msg;
                }
            }

            // trigger a new WM_PAINT
            self2.wnd.hwnd().InvalidateRect(None, true)?;
            Ok(())
        });

        let self2 = self.clone();
        self.wnd.on().wm_paint(move || {
            let mut idx = self2.frame_idx.borrow_mut();
            let h_bitmap = unsafe { self2.frames[*idx].raw_copy() };

            *idx += 1;
            if *idx >= self2.frames.len() {
                *idx = 0;
            }

            let hwnd = self2.wnd.hwnd();
            let rect = hwnd.GetClientRect()?;
            let hdc = hwnd.BeginPaint()?;
            let hdc_mem = hdc.CreateCompatibleDC()?;
            let _old_bitmap = hdc_mem.SelectObject(&h_bitmap)?;

            let background_brush = w::HBRUSH::CreateSolidBrush(w::COLORREF::new(255, 255, 255))?;
            hdc_mem.FillRect(rect, &background_brush)?;

            let progress = self2.progress.borrow();
            let progress_brush = w::HBRUSH::CreateSolidBrush(w::COLORREF::new(0, 255, 0))?;
            let progress_width = (rect.right as f32 * (*progress as f32 / 100.0)) as i32;
            let progress_rect = w::RECT { left: 0, bottom: rect.bottom, right: progress_width, top: rect.bottom - 10 };
            hdc_mem.FillRect(progress_rect, &progress_brush)?;

            hdc.SetStretchBltMode(co::STRETCH_MODE::STRETCH_HALFTONE)?;
            hdc.StretchBlt(
                w::POINT { x: 0, y: 0 },
                w::SIZE { cx: rect.right, cy: rect.bottom },
                &hdc_mem,
                w::POINT { x: 0, y: 0 },
                w::SIZE { cx: self2.w.into(), cy: self2.h.into() },
                co::ROP::SRCCOPY,
            )?;

            Ok(())
        });
    }
}

// pub const TDM_SET_PROGRESS_BAR_MARQUEE: co::WM = unsafe { co::WM::from_raw(1131) };
// pub const TDM_SET_MARQUEE_PROGRESS_BAR: co::WM = unsafe { co::WM::from_raw(1127) };
pub const TDM_SET_PROGRESS_BAR_POS: co::WM = unsafe { co::WM::from_raw(1130) };

// struct MsgSetProgressMarqueeOnOff {
//     is_marquee_on: bool,
// }
// unsafe impl MsgSend for MsgSetProgressMarqueeOnOff {
//     type RetType = ();
//     fn convert_ret(&self, _: isize) -> Self::RetType {
//         ()
//     }
//     fn as_generic_wm(&mut self) -> w::msg::WndMsg {
//         let v: usize = if self.is_marquee_on { 1 } else { 0 };
//         w::msg::WndMsg { msg_id: TDM_SET_PROGRESS_BAR_MARQUEE, wparam: v, lparam: 0 }
//     }
// }

// struct MsgSetProgressMarqueeMode {
//     is_marquee_on: bool,
// }
// unsafe impl MsgSend for MsgSetProgressMarqueeMode {
//     type RetType = ();
//     fn convert_ret(&self, _: isize) -> Self::RetType {
//         ()
//     }
//     fn as_generic_wm(&mut self) -> w::msg::WndMsg {
//         let v: usize = if self.is_marquee_on { 1 } else { 0 };
//         w::msg::WndMsg { msg_id: TDM_SET_MARQUEE_PROGRESS_BAR, wparam: v, lparam: 0 }
//     }
// }

struct MsgSetProgressPos {
    pos: usize,
}
unsafe impl MsgSend for MsgSetProgressPos {
    type RetType = ();
    fn convert_ret(&self, _: isize) -> Self::RetType {
        ()
    }
    fn as_generic_wm(&mut self) -> w::msg::WndMsg {
        w::msg::WndMsg { msg_id: TDM_SET_PROGRESS_BAR_POS, wparam: self.pos, lparam: 0 }
    }
}

#[derive(Clone)]
pub struct ComCtlProgressWindow {
    // hwnd: Rc<RefCell<w::HWND>>,
    rx: Rc<Receiver<i16>>,
}

fn show_com_ctl_progress_dialog(rx: Receiver<i16>, window_title: &str, content: &str) {
    let mut window_title = WString::from_str(window_title);
    let mut content = WString::from_str(content);

    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.dwFlags = co::TDF::SIZE_TO_CONTENT | co::TDF::SHOW_PROGRESS_BAR | co::TDF::CALLBACK_TIMER;
    config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::INFORMATION));
    config.set_pszWindowTitle(Some(&mut window_title));
    config.set_pszMainInstruction(Some(&mut content));

    // if (_icon != null) {
    //     config.dwFlags |= TASKDIALOG_FLAGS.TDF_USE_HICON_MAIN;
    //     config.mainIcon = _icon.Handle;
    // }

    let me = ComCtlProgressWindow { rx: Rc::new(rx) };
    config.lpCallbackData = &me as *const ComCtlProgressWindow as usize;
    config.pfCallback = Some(task_dialog_callback);

    let _ = w::TaskDialogIndirect(&config, None);
}

extern "system" fn task_dialog_callback(hwnd: w::HWND, msg: co::TDN, _: usize, _: isize, lp_ref_data: usize) -> co::HRESULT {
    let raw = lp_ref_data as *const ComCtlProgressWindow;
    let me: &ComCtlProgressWindow = unsafe { &*raw };

    // if msg == co::TDN::DIALOG_CONSTRUCTED {
    //     let mut h = me.hwnd.borrow_mut();
    //     *h = hwnd;
    // }

    if msg == co::TDN::BUTTON_CLICKED {
        return co::HRESULT::S_FALSE; // TODO, support cancellation
    }

    if msg == co::TDN::TIMER {
        let mut progress: i16 = -1;
        loop {
            let msg = me.rx.try_recv().unwrap_or(MSG_NOMESSAGE);
            if msg == MSG_NOMESSAGE {
                break;
            } else if msg == MSG_CLOSE {
                hwnd.SendMessage(w::msg::wm::Close {});
                return co::HRESULT::S_OK;
            } else if msg >= 0 {
                progress = msg;
            }
        }
        if progress > 0 {
            hwnd.SendMessage(MsgSetProgressPos { pos: progress as usize });
        }
    }

    return co::HRESULT::S_OK;
}
