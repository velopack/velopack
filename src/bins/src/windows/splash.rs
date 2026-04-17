use anyhow::{bail, Result};
use image::{codecs::gif::GifDecoder, imageops::FilterType, AnimationDecoder, DynamicImage, ImageFormat, ImageReader};
use std::sync::mpsc::{self, Receiver, Sender};
use std::{io::Cursor, thread};
use velopack::wide_strings::string_to_wide;
use windows::{
    core::{BOOL, HRESULT},
    Win32::{
        Foundation::{COLORREF, HINSTANCE, HWND, LPARAM, LRESULT, POINT, RECT, SIZE, S_OK, WPARAM},
        Graphics::Gdi::*,
        System::LibraryLoader::GetModuleHandleW,
        UI::{Controls::*, WindowsAndMessaging::*},
    },
};

const TMR_GIF: usize = 1;
const MSG_NOMESSAGE: i16 = -99;

pub const MSG_CLOSE: i16 = -1;
pub const MSG_INDEFINITE: i16 = -2;

fn parse_hex_color(color_str: &str) -> (u8, u8, u8) {
    let hex_str = color_str.trim_start_matches('#');
    if hex_str.len() == 6 {
        if let (Ok(r), Ok(g), Ok(b)) = (
            u8::from_str_radix(&hex_str[0..2], 16),
            u8::from_str_radix(&hex_str[2..4], 16),
            u8::from_str_radix(&hex_str[4..6], 16),
        ) {
            return (r, g, b);
        }
    }
    warn!("Invalid color format '{}', using default green", color_str);
    (0, 255, 0)
}

#[derive(Default, Clone)]
pub struct SplashOptions {
    pub splash_progress_color: Option<String>,
}

impl SplashOptions {
    pub fn get_progress_bar_color(&self) -> Option<(u8, u8, u8)> {
        match self.splash_progress_color.as_deref() {
            Some(c) if c.eq_ignore_ascii_case("none") => None,
            Some(c) => Some(parse_hex_color(c)),
            None => Some((0, 255, 0)),
        }
    }
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
        if let Some(img) = imgstream {
            let progress_bar_color = options.get_progress_bar_color();
            if let Err(e) = unsafe { SplashWindow::run(app_name, img, rx, progress_bar_color) } {
                error!("Failed to show splash screen: {:?}", e);
            }
        } else {
            let setup_name = format!("{} Setup", app_name);
            let content = format!("Installing {}...", app_name);
            show_com_ctl_progress_dialog(rx, setup_name.as_str(), content.as_str());
        }
    });
    tx
}

struct SplashWindow {
    frames: Vec<HBITMAP>,
    rx: Receiver<i16>,
    progress: i16,
    frame_idx: usize,
    w: i32,         // current bitmap width (pre-scaled to match window)
    h: i32,         // current bitmap height (pre-scaled to match window)
    scaled_w: i32,  // DPI-scaled window width
    scaled_h: i32,  // DPI-scaled window height
    dpi_scale: f32, // current DPI scale factor
    hdc_screen: HDC,
    progress_bar_color: Option<(u8, u8, u8)>,
}

fn average(numbers: &[u32]) -> u32 {
    let sum: u32 = numbers.iter().sum();
    let count = numbers.len() as u32;
    sum / count
}

pub fn init_dpi_awareness() {
    // Lazy-load DPI awareness functions to support older Windows versions.
    // Fallback chain: V2 per-monitor (Win10 1607+) -> per-monitor (Win8.1+) -> system aware (Vista+)
    unsafe {
        // Try SetProcessDpiAwarenessContext (Win10 1607+, user32.dll)
        type SetDpiAwarenessContextFn = unsafe extern "system" fn(value: isize) -> BOOL;
        if let Ok(lib) = libloading::Library::new("user32.dll") {
            if let Ok(func) = lib.get::<SetDpiAwarenessContextFn>(b"SetProcessDpiAwarenessContext") {
                const DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: isize = -4;
                if func(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2).as_bool() {
                    return;
                }
            }
        }

        // Try SetProcessDpiAwareness (Win8.1+, shcore.dll)
        type SetDpiAwarenessFn = unsafe extern "system" fn(value: u32) -> HRESULT;
        if let Ok(lib) = libloading::Library::new("shcore.dll") {
            if let Ok(func) = lib.get::<SetDpiAwarenessFn>(b"SetProcessDpiAwareness") {
                const PROCESS_PER_MONITOR_DPI_AWARE: u32 = 2;
                if func(PROCESS_PER_MONITOR_DPI_AWARE) == S_OK {
                    return;
                }
            }
        }

        // Fallback: SetProcessDPIAware (Vista+, user32.dll)
        type SetDPIAwareFn = unsafe extern "system" fn() -> BOOL;
        if let Ok(lib) = libloading::Library::new("user32.dll") {
            if let Ok(func) = lib.get::<SetDPIAwareFn>(b"SetProcessDPIAware") {
                let _ = func();
            }
        }
    }
}

fn get_monitor_dpi_scale(h_monitor: HMONITOR) -> f32 {
    // Lazy-load GetDpiForMonitor from shcore.dll (Win8.1+).
    // Falls back to GetDpiForSystem (Vista+) if unavailable.
    unsafe {
        type GetDpiForMonitorFn = unsafe extern "system" fn(hmonitor: HMONITOR, dpi_type: u32, dpi_x: *mut u32, dpi_y: *mut u32) -> HRESULT;
        if let Ok(lib) = libloading::Library::new("shcore.dll") {
            if let Ok(func) = lib.get::<GetDpiForMonitorFn>(b"GetDpiForMonitor") {
                let mut dpi_x: u32 = 0;
                let mut dpi_y: u32 = 0;
                const MDT_EFFECTIVE_DPI: u32 = 0;
                if func(h_monitor, MDT_EFFECTIVE_DPI, &mut dpi_x, &mut dpi_y) == S_OK && dpi_x > 0 {
                    return dpi_x as f32 / 96.0;
                }
            }
        }
        // Fallback to system DPI
        use windows::Win32::UI::HiDpi::GetDpiForSystem;
        GetDpiForSystem() as f32 / 96.0
    }
}

fn clamp_to_monitor(scaled_w: i32, scaled_h: i32, monitor_rect: &RECT) -> (i32, i32) {
    let max_w = ((monitor_rect.right - monitor_rect.left) as f32 * 0.7) as i32;
    let max_h = ((monitor_rect.bottom - monitor_rect.top) as f32 * 0.7) as i32;
    if scaled_w > max_w || scaled_h > max_h {
        let ratio = (max_w as f32 / scaled_w as f32).min(max_h as f32 / scaled_h as f32);
        ((scaled_w as f32 * ratio) as i32, (scaled_h as f32 * ratio) as i32)
    } else {
        (scaled_w, scaled_h)
    }
}

fn convert_rgba_to_bgra(image_data: &mut [u8]) {
    for chunk in image_data.chunks_mut(4) {
        chunk.swap(0, 2);
    }
}

unsafe extern "system" fn window_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    let ptr = GetWindowLongPtrW(hwnd, GWL_USERDATA) as *mut SplashWindow;
    match ptr.as_mut() {
        Some(data) => LRESULT(data.handle_event(hwnd, msg, wparam, lparam)),
        // If the pointer is null, we can just call the default window procedure
        None => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

fn rgb(red: u8, green: u8, blue: u8) -> COLORREF {
    COLORREF((red as u32) | ((green as u32) << 8) | ((blue as u32) << 16))
}

const ALPHA_BLEND_FN: BLENDFUNCTION = BLENDFUNCTION {
    BlendOp: AC_SRC_OVER as u8,
    BlendFlags: 0,
    SourceConstantAlpha: 255,
    AlphaFormat: AC_SRC_ALPHA as u8,
};

impl SplashWindow {
    pub unsafe fn run(app_name: String, img_stream: Vec<u8>, rx: Receiver<i16>, progress_bar_color: Option<(u8, u8, u8)>) -> Result<()> {
        let mut delays = Vec::new();
        let mut decoded_images = Vec::new();

        let fmt_cursor = Cursor::new(&img_stream);
        let fmt_reader = ImageReader::new(fmt_cursor).with_guessed_format()?;
        let fmt = fmt_reader.format();
        let dims = &fmt_reader.into_dimensions()?;
        let w: i32 = i32::try_from(dims.0)?;
        let h: i32 = i32::try_from(dims.1)?;

        if Some(ImageFormat::Gif) == fmt {
            info!("Image is animated GIF ({}x{}), loading frames...", w, h);
            let gif_cursor = Cursor::new(&img_stream);
            let decoder = GifDecoder::new(gif_cursor)?;
            let dec_frames = decoder.into_frames();
            for frame in dec_frames.into_iter() {
                let frame = frame?;
                let (num, dem) = frame.delay().numer_denom_ms();
                delays.push((num / dem) as u32);
                let dynamic = DynamicImage::from(frame.buffer().to_owned());
                decoded_images.push(dynamic);
            }
            info!("Successfully loaded {} frames.", decoded_images.len());
        } else {
            info!("Loading static image (detected {:?})...", fmt);
            delays.push(16u32); // 60 fps
            let img_cursor = Cursor::new(&img_stream);
            let img_decoder = ImageReader::new(img_cursor).with_guessed_format()?.decode()?;
            decoded_images.push(img_decoder);
            info!("Successfully loaded.");
        }

        // TODO: only support a fixed frame delay for now. Maybe should
        // support a variable frame delay in the future.
        let delay = average(&delays);

        // Find the monitor containing the cursor for per-monitor DPI
        let mut lppoint = POINT::default();
        GetCursorPos(&mut lppoint)?;
        let h_monitor = MonitorFromPoint(lppoint, MONITOR_DEFAULTTONEAREST);

        // Get per-monitor DPI scale and calculate scaled dimensions
        let dpi_scale = get_monitor_dpi_scale(h_monitor);
        let scaled_w = (w as f32 * dpi_scale) as i32;
        let scaled_h = (h as f32 * dpi_scale) as i32;

        // Clamp to 70% of monitor size and center
        let mut mi: MONITORINFO = Default::default();
        mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
        let (scaled_w, scaled_h) = if GetMonitorInfoW(h_monitor, &mut mi).as_bool() {
            let (cw, ch) = clamp_to_monitor(scaled_w, scaled_h, &mi.rcMonitor);
            let rc = mi.rcMonitor;
            lppoint.x = (rc.left + rc.right - cw) / 2;
            lppoint.y = (rc.top + rc.bottom - ch) / 2;
            (cw, ch)
        } else {
            let mut rc_work_area: RECT = Default::default();
            SystemParametersInfoW(
                SPI_GETWORKAREA,
                0,
                Some(&mut rc_work_area as *mut RECT as *mut _),
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS(0),
            )?;
            let (cw, ch) = clamp_to_monitor(scaled_w, scaled_h, &rc_work_area);
            lppoint.x = (rc_work_area.left + rc_work_area.right - cw) / 2;
            lppoint.y = (rc_work_area.top + rc_work_area.bottom - ch) / 2;
            (cw, ch)
        };
        info!("DPI scale: {}, window size: {}x{} -> {}x{}", dpi_scale, w, h, scaled_w, scaled_h);

        // Pre-scale frames using high-quality Lanczos3 resampling so the bitmaps
        // match the window size. This avoids blurry GDI stretching at high DPI.
        let mut frames = Vec::new();
        for img in &decoded_images {
            let scaled_img = if scaled_w != w || scaled_h != h {
                img.resize_exact(scaled_w as u32, scaled_h as u32, FilterType::Lanczos3)
            } else {
                img.clone()
            };
            let mut vec = scaled_img.to_rgba8().into_vec();
            convert_rgba_to_bgra(&mut vec);
            let bitmap = CreateBitmap(scaled_w, scaled_h, 1, 32, Some(vec.as_mut_ptr() as *mut _));
            frames.push(bitmap);
        }

        let class_name = string_to_wide("VelopackSetupSplashWindow");
        let app_name = string_to_wide(&app_name);

        let h_instance: HINSTANCE = GetModuleHandleW(None)?.into();

        let wnd_class = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(window_proc),
            hInstance: h_instance,
            hCursor: LoadCursorW(None, IDC_APPSTARTING)?,
            hbrBackground: CreateSolidBrush(rgb(0, 0, 0)),
            lpszClassName: class_name.as_pcwstr(),
            ..Default::default()
        };

        let class_id = unsafe { RegisterClassExW(&wnd_class) };
        if class_id == 0 {
            // if class already registered we can ignore
            let err = std::io::Error::last_os_error();
            if err.raw_os_error() != Some(1410) {
                bail!("Failed to register window class: {:?}", err);
            }
        }

        let hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            class_name.as_pcwstr(),
            app_name.as_pcwstr(),
            WS_CLIPCHILDREN | WS_POPUP,
            lppoint.x,
            lppoint.y,
            scaled_w,
            scaled_h,
            None,
            None,
            Some(h_instance),
            None,
        )?;

        let hwnd_desktop = unsafe { GetDesktopWindow() };
        let hdc_screen = unsafe { GetDC(Some(hwnd_desktop)) };

        let data_ptr = Box::into_raw(Box::new(Self {
            frames,
            rx,
            frame_idx: 0,
            w: scaled_w,
            h: scaled_h,
            scaled_w,
            scaled_h,
            dpi_scale,
            progress: 0,
            hdc_screen,
            progress_bar_color,
        }));

        SetWindowLongPtrW(hwnd, GWL_USERDATA, (data_ptr as isize).try_into()?);
        let _ = ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        SetTimer(Some(hwnd), TMR_GIF, delay, None);

        let mut msg = MSG::default();
        let _ = PeekMessageW(&mut msg, Some(hwnd), 0, 0, PEEK_MESSAGE_REMOVE_TYPE(0)); // invoke creating message queue

        while GetMessageW(&mut msg, None, 0, 0).as_bool() {
            if msg.message == WM_QUIT {
                break;
            }
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        let data = Box::from_raw(data_ptr);
        ReleaseDC(Some(hwnd_desktop), data.hdc_screen);
        for h_bitmap in &data.frames {
            let _ = DeleteObject((*h_bitmap).into());
        }

        let _ = DestroyWindow(hwnd);
        Ok(())
    }

    pub unsafe fn handle_event(&mut self, hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> isize {
        match msg {
            WM_NCHITTEST => {
                return HTCAPTION as isize; // make the window draggable
            }
            WM_DPICHANGED => {
                // Let Windows handle position and size — the suggested rect already
                // has the correct DPI-scaled dimensions for the new monitor.
                let suggested = &*(lparam.0 as *const RECT);
                self.scaled_w = suggested.right - suggested.left;
                self.scaled_h = suggested.bottom - suggested.top;
                self.dpi_scale = (wparam.0 & 0xFFFF) as f32 / 96.0;

                let _ = SetWindowPos(
                    hwnd,
                    None,
                    suggested.left,
                    suggested.top,
                    self.scaled_w,
                    self.scaled_h,
                    SWP_NOZORDER | SWP_NOACTIVATE,
                );
                let _ = InvalidateRect(Some(hwnd), None, false);
                return 0;
            }
            WM_TIMER => {
                if wparam.0 as usize == TMR_GIF {
                    // handle any incoming messages before painting
                    let next_message = drain_and_get_next_message(&self.rx);
                    if next_message == MSG_CLOSE {
                        let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
                        return 0;
                    } else if next_message >= 0 {
                        self.progress = next_message;
                    }

                    // advance the frame index
                    self.frame_idx += 1;
                    if self.frame_idx >= self.frames.len() {
                        self.frame_idx = 0; // loop back to the first frame
                    }

                    // trigger a new WM_PAINT
                    let _ = InvalidateRect(Some(hwnd), None, false);
                }
                return 0;
            }
            WM_PAINT => {
                let mut ps = PAINTSTRUCT::default();
                let _hdc = BeginPaint(hwnd, &mut ps);
                if _hdc.is_invalid() {
                    return 0;
                }

                // get the bitmap for the current frame
                let h_bitmap = self.frames[self.frame_idx];

                // create memory DC with the original frame bitmap
                let hdc_src = CreateCompatibleDC(Some(self.hdc_screen));
                let src_old = SelectObject(hdc_src, h_bitmap.into());

                // create a scaled bitmap for the output
                let hdc_dest = CreateCompatibleDC(Some(self.hdc_screen));
                let scaled_bitmap = CreateCompatibleBitmap(self.hdc_screen, self.scaled_w, self.scaled_h);
                let dest_old = SelectObject(hdc_dest, scaled_bitmap.into());

                // stretch the original bitmap to the scaled size using AlphaBlend
                // to preserve alpha channel during scaling
                SetStretchBltMode(hdc_dest, STRETCH_HALFTONE);
                let result = AlphaBlend(
                    hdc_dest,
                    0,
                    0,
                    self.scaled_w,
                    self.scaled_h,
                    hdc_src,
                    0,
                    0,
                    self.w,
                    self.h,
                    ALPHA_BLEND_FN,
                );
                if !result.as_bool() {
                    warn!("AlphaBlend failed");
                }

                // draw progress bar on top of the scaled bitmap (if enabled)
                // Must use AlphaBlend with a 32-bit BGRA bitmap so the alpha channel
                // is set to 255 (opaque). FillRect leaves alpha at 0, which
                // UpdateLayeredWindow treats as fully transparent.
                if let Some((r, g, b)) = self.progress_bar_color {
                    let progress_width = (self.scaled_w as f32 * (self.progress as f32 / 100.0)) as i32;
                    let progress_height = (12.0 * self.dpi_scale) as i32;
                    if progress_width > 0 {
                        // BGRA pixel with alpha=255 (little-endian u32: 0xAARRGGBB)
                        let pixel: u32 = (255u32 << 24) | ((r as u32) << 16) | ((g as u32) << 8) | (b as u32);
                        let mut pixel_data = [pixel];
                        let bar_bmp = CreateBitmap(1, 1, 1, 32, Some(pixel_data.as_mut_ptr() as *mut _));
                        let hdc_bar = CreateCompatibleDC(Some(self.hdc_screen));
                        let old_bar = SelectObject(hdc_bar, bar_bmp.into());
                        let _ = AlphaBlend(
                            hdc_dest,
                            0,
                            self.scaled_h - progress_height,
                            progress_width,
                            progress_height,
                            hdc_bar,
                            0,
                            0,
                            1,
                            1,
                            ALPHA_BLEND_FN,
                        );
                        SelectObject(hdc_bar, old_bar);
                        let _ = DeleteDC(hdc_bar);
                        let _ = DeleteObject(bar_bmp.into());
                    }
                }

                // Use UpdateLayeredWindow for true transparency with per-pixel alpha
                let size = SIZE {
                    cx: self.scaled_w,
                    cy: self.scaled_h,
                };
                let pt_src = POINT { x: 0, y: 0 };
                let _ = UpdateLayeredWindow(
                    hwnd,
                    None,
                    None,
                    Some(&size),
                    Some(hdc_dest),
                    Some(&pt_src),
                    COLORREF(0),
                    Some(&ALPHA_BLEND_FN),
                    ULW_ALPHA,
                );

                // clean up
                SelectObject(hdc_dest, dest_old);
                SelectObject(hdc_src, src_old);
                let _ = DeleteDC(hdc_dest);
                let _ = DeleteDC(hdc_src);
                let _ = DeleteObject(scaled_bitmap.into());

                let _ = EndPaint(hwnd, &ps);
                return 0;
            }
            _ => {
                // handle other messages
                return DefWindowProcW(hwnd, msg, wparam, lparam).0;
            }
        }
    }
}

struct ComCtlProgressWindow {
    rx: Receiver<i16>,
    last_progress: i16,
}

fn show_com_ctl_progress_dialog(rx: Receiver<i16>, window_title: &str, content: &str) {
    let window_title = string_to_wide(window_title);
    let content = string_to_wide(content);
    let ok_text = string_to_wide("Hide");

    let td_btn = TASKDIALOG_BUTTON {
        nButtonID: 1, // OK button id
        pszButtonText: ok_text.as_pcwstr(),
    };
    let custom_btns = vec![td_btn];

    let mut config: TASKDIALOGCONFIG = Default::default();
    config.cbSize = std::mem::size_of::<TASKDIALOGCONFIG>() as u32;
    config.dwFlags = TDF_SIZE_TO_CONTENT | TDF_SHOW_PROGRESS_BAR | TDF_CALLBACK_TIMER;
    config.pszWindowTitle = window_title.as_pcwstr();
    config.pszMainInstruction = content.as_pcwstr();
    config.pButtons = custom_btns.as_ptr();
    config.cButtons = custom_btns.len() as u32;
    config.nDefaultButton = 1;
    config.Anonymous1.pszMainIcon = TD_INFORMATION_ICON;

    let me = ComCtlProgressWindow { rx, last_progress: 0 };
    let data_ptr = Box::into_raw(Box::new(me));
    config.lpCallbackData = data_ptr as isize;
    config.pfCallback = Some(task_dialog_callback);

    unsafe {
        let _ = TaskDialogIndirect(&config, None, None, None);
    }

    let _ = unsafe { Box::from_raw(data_ptr) }; // This will drop the ComCtlProgressWindow instance
}

fn drain_and_get_next_message(rx: &Receiver<i16>) -> i16 {
    let mut progress: i16 = MSG_NOMESSAGE;
    loop {
        let msg = rx.try_recv().unwrap_or(MSG_NOMESSAGE);
        if msg == MSG_NOMESSAGE {
            break;
        } else {
            progress = msg;
        }
    }
    progress
}

unsafe extern "system" fn task_dialog_callback(
    hwnd: HWND,
    msg: TASKDIALOG_NOTIFICATIONS,
    _wparam: WPARAM,
    _lparam: LPARAM,
    lp_ref_data: isize,
) -> HRESULT {
    let raw = lp_ref_data as *mut ComCtlProgressWindow;

    if let Some(me) = raw.as_mut() {
        if msg == TDN_TIMER {
            let next_message = drain_and_get_next_message(&me.rx);
            if next_message == MSG_CLOSE {
                let _ = EndDialog(hwnd, 0);
            } else if next_message == MSG_INDEFINITE {
                SendMessageW(hwnd, TDM_SET_PROGRESS_BAR_MARQUEE.0 as u32, Some(WPARAM(1)), Some(LPARAM(0)));
                SendMessageW(hwnd, TDM_SET_MARQUEE_PROGRESS_BAR.0 as u32, Some(WPARAM(1)), Some(LPARAM(0)));
                me.last_progress = MSG_INDEFINITE;
            } else if next_message >= 0 {
                if me.last_progress < 0 {
                    SendMessageW(hwnd, TDM_SET_PROGRESS_BAR_MARQUEE.0 as u32, Some(WPARAM(0)), Some(LPARAM(0)));
                    SendMessageW(hwnd, TDM_SET_MARQUEE_PROGRESS_BAR.0 as u32, Some(WPARAM(0)), Some(LPARAM(0)));
                }
                SendMessageW(
                    hwnd,
                    TDM_SET_PROGRESS_BAR_POS.0 as u32,
                    Some(WPARAM(next_message as usize)),
                    Some(LPARAM(0)),
                );
                me.last_progress = next_message;
            }
        }
    }

    return S_OK;
}

#[test]
fn test_parse_hex_color_valid() {
    assert_eq!(parse_hex_color("FF0000"), (255, 0, 0));
    assert_eq!(parse_hex_color("#00FF00"), (0, 255, 0));
    assert_eq!(parse_hex_color("#0000ff"), (0, 0, 255));
    assert_eq!(parse_hex_color("AB12CD"), (0xAB, 0x12, 0xCD));
}

#[test]
fn test_parse_hex_color_invalid_falls_back_to_green() {
    assert_eq!(parse_hex_color(""), (0, 255, 0));
    assert_eq!(parse_hex_color("FFF"), (0, 255, 0));
    assert_eq!(parse_hex_color("#FFF"), (0, 255, 0));
    assert_eq!(parse_hex_color("ZZZZZZ"), (0, 255, 0));
    assert_eq!(parse_hex_color("not a color"), (0, 255, 0));
}

#[test]
#[ignore]
fn show_splash_gif() {
    let rd = std::fs::read(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test/fixtures/splash-test.gif")).unwrap();
    let tx = show_splash_dialog("osu!".to_string(), Some(rd), SplashOptions::default());
    let _ = tx.send(25);
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(50);
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(75);
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(100);
    std::thread::sleep(std::time::Duration::from_secs(3));
    let _ = tx.send(MSG_CLOSE);
    std::thread::sleep(std::time::Duration::from_secs(3));
}

#[test]
#[ignore]
fn show_splash_png_transparency() {
    let fixture = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test/fixtures/splash-test.png");
    let rd = std::fs::read(&fixture).unwrap();
    let tx = show_splash_dialog("Beer App".to_string(), Some(rd), SplashOptions::default());
    let _ = tx.send(50);
    std::thread::sleep(std::time::Duration::from_secs(5));
    let _ = tx.send(100);
    std::thread::sleep(std::time::Duration::from_secs(3));
    let _ = tx.send(MSG_CLOSE);
    std::thread::sleep(std::time::Duration::from_secs(1));
}

#[test]
#[ignore]
fn show_splash_without_progress_bar() {
    let rd = std::fs::read(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test/fixtures/splash-test.gif")).unwrap();
    let tx = show_splash_dialog(
        "osu!".to_string(),
        Some(rd),
        SplashOptions {
            splash_progress_color: Some("None".to_string()),
        },
    );
    let _ = tx.send(80);
    std::thread::sleep(std::time::Duration::from_secs(6));
}

#[test]
#[ignore]
fn show_splash_progress() {
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
