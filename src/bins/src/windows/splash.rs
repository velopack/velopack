use anyhow::{bail, Result};
use image::{codecs::gif::GifDecoder, AnimationDecoder, DynamicImage, ImageFormat, ImageReader};
use std::sync::atomic::{AtomicI16, Ordering};
use std::{
    cell::RefCell,
    io::Cursor,
    ops::Deref,
    rc::Rc,
    sync::mpsc::{self, Receiver, Sender},
    thread,
};
use winsafe::guard::DeleteObjectGuard;
use winsafe::{self as w, co, gui, prelude::*, WString};

const TMR_GIF: usize = 1;
const MSG_NOMESSAGE: i16 = -99;

pub const MSG_CLOSE: i16 = -1;
pub const MSG_INDEFINITE: i16 = -2;

#[derive(Default, Clone)]
pub struct SplashOptions {
    pub no_progress_bar: bool,
    pub progress_bar_color: Option<String>,
}

pub fn show_progress_dialog<T1: AsRef<str>, T2: AsRef<str>>(window_title: T1, content: T2) -> Sender<i16> {
    let window_title = window_title.as_ref().to_string();
    let content = content.as_ref().to_string();
    let (tx, rx) = mpsc::channel::<i16>();
    thread::spawn(move || {
        show_com_ctl_progress_dialog(rx, &window_title, &content);
    });
    tx
}

pub fn show_splash_dialog(app_name: String, imgstream: Option<Vec<u8>>, options: SplashOptions) -> Sender<i16> {
    let (tx, rx) = mpsc::channel::<i16>();
    thread::spawn(move || {
        info!("Showing splash screen immediately...");
        if imgstream.is_some() {
            let color = options.progress_bar_color.as_deref().unwrap_or("");
            let _ = SplashWindow::new(app_name, imgstream.unwrap(), rx, options.no_progress_bar, color).and_then(|w| {
                w.run()?;
                Ok(())
            });
        } else {
            let setup_name = format!("{} Setup", app_name);
            let content = format!("Installing {}...", app_name);
            show_com_ctl_progress_dialog(rx, setup_name.as_str(), content.as_str());
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
    no_progress_bar: bool,
    progress_bar_color: (u8, u8, u8),
}

fn average(numbers: &[u16]) -> u16 {
    let sum: u16 = numbers.iter().sum();
    let count = numbers.len() as u16;
    sum / count
}

fn parse_hex_color(color_str: &str) -> (u8, u8, u8) {
    // Default to green if parsing fails
    let default_color = (0, 255, 0);

    if color_str.is_empty() {
        return default_color;
    }

    // Remove leading # if present
    let hex_str = color_str.trim_start_matches('#');

    // Parse hex string (should be 6 characters: RRGGBB)
    if hex_str.len() != 6 {
        warn!("Invalid color format '{}', using default green", color_str);
        return default_color;
    }

    match (
        u8::from_str_radix(&hex_str[0..2], 16),
        u8::from_str_radix(&hex_str[2..4], 16),
        u8::from_str_radix(&hex_str[4..6], 16),
    ) {
        (Ok(r), Ok(g), Ok(b)) => (r, g, b),
        _ => {
            warn!("Failed to parse color '{}', using default green", color_str);
            default_color
        }
    }
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
    pub fn new(app_name: String, img_stream: Vec<u8>, rx: Receiver<i16>, no_progress_bar: bool, progress_bar_color: &str) -> Result<Self> {
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
            ex_style: co::WS_EX::LAYERED | co::WS_EX::TOPMOST,
            style: co::WS::POPUP,
            ..Default::default()
        });

        let frames = Rc::new(frames);
        let rx = Rc::new(rx);
        let progress = Rc::new(RefCell::new(0));
        let frame_idx = Rc::new(RefCell::new(0));
        let progress_bar_color = parse_hex_color(progress_bar_color);
        let mut new_self = Self { wnd, frames, delay, frame_idx, w, h, rx, progress, no_progress_bar, progress_bar_color };
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
            self2.wnd.hwnd().InvalidateRect(None, false)?;
            Ok(())
        });

        let self2 = self.clone();
        self.wnd.on().wm_paint(move || {
            // initial setup
            let hwnd = self2.wnd.hwnd();
            let rect = hwnd.GetClientRect()?;
            let _hdc = hwnd.BeginPaint()?;
            let _w = rect.right - rect.left;
            let _h = rect.bottom - rect.top;
            let desktop = w::HWND::GetDesktopWindow();
            let hdc_screen = desktop.GetDC()?;

            // retrieve the next frame to draw
            let mut idx = self2.frame_idx.borrow_mut();
            let h_bitmap = self2.frames[*idx].deref();
            *idx += 1;
            if *idx >= self2.frames.len() {
                *idx = 0;
            }

            // create memory DC with the frame bitmap
            let hdc_mem = hdc_screen.CreateCompatibleDC()?;
            let _bitmap_old = hdc_mem.SelectObject(h_bitmap)?;

            // draw progress bar on top of the bitmap (if enabled)
            if !self2.no_progress_bar {
                let progress = self2.progress.borrow();
                let (r, g, b) = self2.progress_bar_color;
                let progress_brush = w::HBRUSH::CreateSolidBrush(w::COLORREF::new(r, g, b))?;
                let progress_width = (self2.w as f32 * (*progress as f32 / 100.0)) as i32;
                let progress_rect = w::RECT { left: 0, bottom: self2.h.into(), right: progress_width, top: self2.h as i32 - 10 };
                hdc_mem.FillRect(progress_rect, &progress_brush)?;
            }

            // Use UpdateLayeredWindow for true transparency with per-pixel alpha
            unsafe {
                use windows::Win32::Graphics::Gdi::{BLENDFUNCTION, AC_SRC_OVER, AC_SRC_ALPHA, HDC};
                use windows::Win32::UI::WindowsAndMessaging::{UpdateLayeredWindow, ULW_ALPHA};
                use windows::Win32::Foundation::{POINT, SIZE, COLORREF};

                let blend_fn = BLENDFUNCTION {
                    BlendOp: AC_SRC_OVER as u8,
                    BlendFlags: 0,
                    SourceConstantAlpha: 255,
                    AlphaFormat: AC_SRC_ALPHA as u8,
                };

                let size = SIZE {
                    cx: self2.w as i32,
                    cy: self2.h as i32,
                };

                let pt_src = POINT { x: 0, y: 0 };

                UpdateLayeredWindow(
                    windows::Win32::Foundation::HWND(hwnd.ptr()),
                    None,
                    None,
                    Some(&size),
                    Some(HDC(hdc_mem.ptr() as _)),
                    Some(&pt_src),
                    COLORREF(0),
                    Some(&blend_fn),
                    ULW_ALPHA,
                )?;
            }
            Ok(())
        });
    }
}

pub const TDM_SET_PROGRESS_BAR_MARQUEE: co::WM = unsafe { co::WM::from_raw(1131) };
pub const TDM_SET_MARQUEE_PROGRESS_BAR: co::WM = unsafe { co::WM::from_raw(1127) };
pub const TDM_SET_PROGRESS_BAR_POS: co::WM = unsafe { co::WM::from_raw(1130) };

struct MsgSetProgressMarqueeOnOff {
    is_marquee_on: bool,
}
unsafe impl MsgSend for MsgSetProgressMarqueeOnOff {
    type RetType = ();
    fn convert_ret(&self, _: isize) -> Self::RetType {
        ()
    }
    fn as_generic_wm(&mut self) -> w::msg::WndMsg {
        let v: usize = if self.is_marquee_on { 1 } else { 0 };
        w::msg::WndMsg { msg_id: TDM_SET_PROGRESS_BAR_MARQUEE, wparam: v, lparam: 0 }
    }
}

struct MsgSetProgressMarqueeMode {
    is_marquee_on: bool,
}
unsafe impl MsgSend for MsgSetProgressMarqueeMode {
    type RetType = ();
    fn convert_ret(&self, _: isize) -> Self::RetType {
        ()
    }
    fn as_generic_wm(&mut self) -> w::msg::WndMsg {
        let v: usize = if self.is_marquee_on { 1 } else { 0 };
        w::msg::WndMsg { msg_id: TDM_SET_MARQUEE_PROGRESS_BAR, wparam: v, lparam: 0 }
    }
}

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
    last_progress: Rc<AtomicI16>,
}

impl ComCtlProgressWindow {
    pub fn set_progress(&self, value: i16) {
        self.last_progress.store(value, Ordering::SeqCst);
    }
    pub fn get_progress(&self) -> i16 {
        self.last_progress.load(Ordering::SeqCst)
    }
    pub fn get_next_message(&self) -> i16 {
        let mut progress: i16 = MSG_NOMESSAGE;
        loop {
            let msg = self.rx.try_recv().unwrap_or(MSG_NOMESSAGE);
            if msg == MSG_NOMESSAGE {
                break;
            } else {
                progress = msg;
            }
        }
        progress
    }
}

fn show_com_ctl_progress_dialog(rx: Receiver<i16>, window_title: &str, content: &str) {
    let mut window_title = WString::from_str(window_title);
    let mut content = WString::from_str(content);

    let mut ok_text_buf = WString::from_str("Hide");
    let mut td_btn = w::TASKDIALOG_BUTTON::default();
    td_btn.set_nButtonID(co::DLGID::OK.into());
    td_btn.set_pszButtonText(Some(&mut ok_text_buf));
    let mut custom_btns = Vec::with_capacity(1);
    custom_btns.push(td_btn);

    let mut config: w::TASKDIALOGCONFIG = Default::default();
    config.dwFlags = co::TDF::SIZE_TO_CONTENT | co::TDF::SHOW_PROGRESS_BAR | co::TDF::CALLBACK_TIMER;
    config.set_pszMainIcon(w::IconIdTdicon::Tdicon(co::TD_ICON::INFORMATION));
    config.set_pszWindowTitle(Some(&mut window_title));
    config.set_pszMainInstruction(Some(&mut content));
    config.set_pButtons(Some(&mut custom_btns));

    // if (_icon != null) {
    //     config.dwFlags |= TASKDIALOG_FLAGS.TDF_USE_HICON_MAIN;
    //     config.mainIcon = _icon.Handle;
    // }

    let me = ComCtlProgressWindow { rx: Rc::new(rx), last_progress: Rc::new(AtomicI16::new(0)) };
    config.lpCallbackData = &me as *const ComCtlProgressWindow as usize;
    config.pfCallback = Some(task_dialog_callback);

    let _ = w::TaskDialogIndirect(&config, None);
}

extern "system" fn task_dialog_callback(hwnd: w::HWND, msg: co::TDN, _: usize, _: isize, lp_ref_data: usize) -> co::HRESULT {
    let raw = lp_ref_data as *const ComCtlProgressWindow;
    let me: &ComCtlProgressWindow = unsafe { &*raw };

    if msg == co::TDN::TIMER {
        let next_message = me.get_next_message();
        if next_message == MSG_CLOSE {
            let _ = hwnd.EndDialog(0);
            return co::HRESULT::S_OK;
        } else if next_message == MSG_INDEFINITE {
            hwnd.SendMessage(MsgSetProgressMarqueeOnOff { is_marquee_on: true });
            hwnd.SendMessage(MsgSetProgressMarqueeMode { is_marquee_on: true });
            me.set_progress(MSG_INDEFINITE);
        } else if next_message >= 0 {
            if me.get_progress() < 0 {
                hwnd.SendMessage(MsgSetProgressMarqueeOnOff { is_marquee_on: false });
                hwnd.SendMessage(MsgSetProgressMarqueeMode { is_marquee_on: false });
            }
            hwnd.SendMessage(MsgSetProgressPos { pos: next_message as usize });
            me.set_progress(next_message);
        }
    }

    return co::HRESULT::S_OK;
}

#[test]
#[ignore]
fn show_test_gif() {
    let rd = std::fs::read(r"C:\Source\Clowd\artwork\splash.gif").unwrap();
    let tx = show_splash_dialog("osu!".to_string(), Some(rd), SplashOptions::default());
    tx.send(80).unwrap();
    std::thread::sleep(std::time::Duration::from_secs(6));
}

#[test]
#[ignore]
fn show_test_gif_without_progress_bar() {
    let rd = std::fs::read(r"C:\Source\Clowd\artwork\splash.gif").unwrap();
    let tx = show_splash_dialog("osu!".to_string(), Some(rd), SplashOptions { no_progress_bar: true, progress_bar_color: None });
    tx.send(80).unwrap();
    std::thread::sleep(std::time::Duration::from_secs(6));
}

#[test]
#[ignore]
fn show_test_progress() {
    let tx = show_progress_dialog("hello!", "this is some content");
    let _ = tx.send(25);
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(50);
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(75);
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(100);
    std::thread::sleep(std::time::Duration::from_secs(3));

    let _ = tx.send(MSG_INDEFINITE);
    std::thread::sleep(std::time::Duration::from_secs(3));

    let _ = tx.send(50);
    std::thread::sleep(std::time::Duration::from_secs(3));

    let _ = tx.send(MSG_CLOSE);
    std::thread::sleep(std::time::Duration::from_secs(3));
}
