use anyhow::{bail, Result};
use fast_image_resize as fr;
use image::{codecs::gif::GifDecoder, AnimationDecoder, ImageFormat, ImageReader};
use rayon::prelude::*;
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

struct ProgressBar {
    hdc: OwnedCompatibleDC,
    _bmp: OwnedBitmap,
}

impl ProgressBar {
    fn new(color: (u8, u8, u8), screen: &OwnedDC) -> Self {
        let (r, g, b) = color;
        let pixel: u32 = (255u32 << 24) | ((r as u32) << 16) | ((g as u32) << 8) | (b as u32);
        let pixel_bytes = pixel.to_ne_bytes();
        let bmp = OwnedBitmap::from_bgra8_pixels(1, 1, &pixel_bytes);
        let hdc = OwnedCompatibleDC::new(screen);
        unsafe { SelectObject(hdc.0, bmp.0.into()) };
        Self { hdc, _bmp: bmp }
    }

    fn draw(&self, dest: &OwnedCompatibleDC, scaled_w: i32, scaled_h: i32, dpi_scale: f32, progress: i16) {
        let progress_width = (scaled_w as f32 * (progress as f32 / 100.0)) as i32;
        let progress_height = (12.0 * dpi_scale) as i32;
        if progress_width > 0 {
            unsafe {
                let _ = StretchBlt(dest.0, 0, scaled_h - progress_height, progress_width, progress_height, Some(self.hdc.0), 0, 0, 1, 1, SRCCOPY);
            }
        }
    }
}

struct SplashWindow {
    frames: Vec<OwnedBitmap>,
    decoded_images: Vec<Vec<u8>>,
    orig_w: u32,
    orig_h: u32,
    rx: Receiver<i16>,
    progress: i16,
    frame_idx: usize,
    scaled_w: i32,
    scaled_h: i32,
    dpi_scale: f32,
    hdc_screen: OwnedDC,
    hdc_src: OwnedCompatibleDC,
    hdc_mem: OwnedCompatibleDC,
    bmp_mem: OwnedBitmap,
    bar: Option<ProgressBar>,
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

// Lanczos3 ringing can produce pixels where RGB > A (invalid premultiplied state),
// which appear as bright garbage pixels at alpha edges.
// LLVM auto-vectorizes this to SSE2 or SSE4.1 pminud, depending on compiler flags.
// https://rust.godbolt.org/z/W7jPEsxTz
fn clamp_premultiplied(pixels: &mut [u8]) {
    for chunk in pixels.chunks_exact_mut(4) {
        let p = u32::from_ne_bytes(chunk.try_into().unwrap());
        let a = (p >> 24) & 0xFF;
        let r = (p & 0xFF).min(a);
        let g = ((p >> 8) & 0xFF).min(a);
        let b = ((p >> 16) & 0xFF).min(a);
        chunk.copy_from_slice(&(r | (g << 8) | (b << 16) | (a << 24)).to_ne_bytes());
    }
}

// Swaps R/B channels and premultiplies RGB by alpha in a single pass using SWAR
// (SIMD Within A Register). Processes R+B in parallel within a u32 by exploiting
// the fact that their products (max 255*256) can't overflow 16-bit lanes.
// LLVM auto-vectorizes this to SSE2 or SSE4.1 (pshufb + pmulld), depending on compiler flags.
// UpdateLayeredWindow with AC_SRC_ALPHA expects premultiplied BGRA.
// See: https://users.rust-lang.org/t/the-fastest-way-to-copy-a-buffer-bgra-to-rgba/126651
// https://rust.godbolt.org/z/W7jPEsxTz
fn convert_rgba_to_premultiplied_bgra(pixels: &mut [u8]) {
    for chunk in pixels.chunks_exact_mut(4) {
        let p = u32::from_ne_bytes(chunk.try_into().unwrap());
        let a = p >> 24;
        let a1 = a + 1;
        let rb_swapped = (p >> 16 & 0xFF) | ((p & 0xFF) << 16);
        let rb = (rb_swapped.wrapping_mul(a1) >> 8) & 0x00FF00FF;
        let g = ((p & 0x0000FF00).wrapping_mul(a1) >> 8) & 0x0000FF00;
        chunk.copy_from_slice(&(rb | g | (a << 24)).to_ne_bytes());
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

struct OwnedBitmap(HBITMAP);
unsafe impl Send for OwnedBitmap {}

impl OwnedBitmap {
    fn from_bgra8_pixels(w: i32, h: i32, pixels: &[u8]) -> Self {
        Self(unsafe { CreateBitmap(w, h, 1, 32, Some(pixels.as_ptr() as *const _)) })
    }
}

impl Drop for OwnedBitmap {
    fn drop(&mut self) {
        unsafe {
            let _ = DeleteObject(self.0.into());
        }
    }
}

struct OwnedDC {
    hdc: HDC,
    hwnd: HWND,
}

impl OwnedDC {
    fn from_desktop() -> Self {
        unsafe {
            let hwnd = GetDesktopWindow();
            Self { hdc: GetDC(Some(hwnd)), hwnd }
        }
    }
}

impl Drop for OwnedDC {
    fn drop(&mut self) {
        unsafe { ReleaseDC(Some(self.hwnd), self.hdc) };
    }
}

struct BitmapGuard {
    hdc: HDC,
    old: HGDIOBJ,
}

impl Drop for BitmapGuard {
    fn drop(&mut self) {
        unsafe { SelectObject(self.hdc, self.old) };
    }
}

struct OwnedCompatibleDC(HDC);

impl OwnedCompatibleDC {
    fn new(screen: &OwnedDC) -> Self {
        Self(unsafe { CreateCompatibleDC(Some(screen.hdc)) })
    }

    fn hdc(&self) -> HDC {
        self.0
    }

    fn use_bitmap(&self, bmp: HBITMAP) -> BitmapGuard {
        let old = unsafe { SelectObject(self.0, bmp.into()) };
        BitmapGuard { hdc: self.0, old }
    }

    fn blit_from(&self, src: &Self, w: i32, h: i32) {
        unsafe {
            let _ = BitBlt(self.0, 0, 0, w, h, Some(src.0), 0, 0, SRCCOPY);
        }
    }

    fn create_surface(&self, screen: &OwnedDC, w: i32, h: i32) -> OwnedBitmap {
        unsafe {
            let bmp = CreateCompatibleBitmap(screen.hdc, w, h);
            SelectObject(self.0, bmp.into());
            OwnedBitmap(bmp)
        }
    }
}

impl Drop for OwnedCompatibleDC {
    fn drop(&mut self) {
        unsafe {
            let _ = DeleteDC(self.0);
        }
    }
}

fn scale_frames(images: &[Vec<u8>], src_w: u32, src_h: u32, dst_w: u32, dst_h: u32) -> Vec<OwnedBitmap> {
    let need_scale = src_w != dst_w || src_h != dst_h;
    let resize_opts = fr::ResizeOptions::new().resize_alg(fr::ResizeAlg::Convolution(fr::FilterType::Lanczos3));
    let (dw, dh) = (dst_w as i32, dst_h as i32);
    images
        .par_iter()
        .map_init(
            || (fr::Resizer::new(), fr::images::Image::new(dst_w, dst_h, fr::PixelType::U8x4)),
            |(resizer, dst_image), bgra| {
                if need_scale {
                    let src_image = fr::images::ImageRef::new(src_w, src_h, bgra, fr::PixelType::U8x4).unwrap();
                    resizer.resize(&src_image, dst_image, &resize_opts).unwrap();
                    let buf = dst_image.buffer_mut();
                    clamp_premultiplied(buf);
                    OwnedBitmap::from_bgra8_pixels(dw, dh, buf)
                } else {
                    OwnedBitmap::from_bgra8_pixels(dw, dh, bgra)
                }
            },
        )
        .collect()
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
        let mut decoded_images: Vec<Vec<u8>> = Vec::new();

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
            for frame in decoder.into_frames() {
                let frame = frame?;
                let (num, dem) = frame.delay().numer_denom_ms();
                delays.push((num / dem) as u32);
                let mut buf = frame.into_buffer().into_vec();
                convert_rgba_to_premultiplied_bgra(&mut buf);
                decoded_images.push(buf);
            }
            info!("Successfully loaded {} frames.", decoded_images.len());
        } else {
            info!("Loading static image (detected {:?})...", fmt);
            delays.push(16u32);
            let img_cursor = Cursor::new(&img_stream);
            let mut buf = ImageReader::new(img_cursor).with_guessed_format()?.decode()?.into_rgba8().into_vec();
            convert_rgba_to_premultiplied_bgra(&mut buf);
            decoded_images.push(buf);
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

        let frames = scale_frames(&decoded_images, w as u32, h as u32, scaled_w as u32, scaled_h as u32);

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

        let hdc_screen = OwnedDC::from_desktop();
        let hdc_src = OwnedCompatibleDC::new(&hdc_screen);
        let hdc_mem = OwnedCompatibleDC::new(&hdc_screen);
        let bmp_mem = hdc_mem.create_surface(&hdc_screen, scaled_w, scaled_h);
        let bar = progress_bar_color.map(|c| ProgressBar::new(c, &hdc_screen));

        let data_ptr = Box::into_raw(Box::new(Self {
            frames,
            decoded_images,
            orig_w: w as u32,
            orig_h: h as u32,
            rx,
            frame_idx: 0,
            scaled_w,
            scaled_h,
            dpi_scale,
            progress: 0,
            hdc_screen,
            hdc_src,
            hdc_mem,
            bmp_mem,
            bar,
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

        drop(Box::from_raw(data_ptr));

        let _ = DestroyWindow(hwnd);
        Ok(())
    }

    pub unsafe fn handle_event(&mut self, hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> isize {
        match msg {
            WM_NCHITTEST => {
                return HTCAPTION as isize; // make the window draggable
            }
            WM_DPICHANGED => {
                let suggested = &*(lparam.0 as *const RECT);
                self.scaled_w = suggested.right - suggested.left;
                self.scaled_h = suggested.bottom - suggested.top;
                self.dpi_scale = (wparam.0 & 0xFFFF) as f32 / 96.0;

                self.frames = scale_frames(&self.decoded_images, self.orig_w, self.orig_h, self.scaled_w as u32, self.scaled_h as u32);
                self.frame_idx = 0;

                self.bmp_mem = self.hdc_mem.create_surface(&self.hdc_screen, self.scaled_w, self.scaled_h);

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

                let _guard = self.hdc_src.use_bitmap(self.frames[self.frame_idx].0);
                self.hdc_mem.blit_from(&self.hdc_src, self.scaled_w, self.scaled_h);
                drop(_guard);

                if let Some(bar) = &self.bar {
                    bar.draw(&self.hdc_mem, self.scaled_w, self.scaled_h, self.dpi_scale, self.progress);
                }

                let size = SIZE { cx: self.scaled_w, cy: self.scaled_h };
                let pt_src = POINT { x: 0, y: 0 };
                let _ = UpdateLayeredWindow(
                    hwnd,
                    None,
                    None,
                    Some(&size),
                    Some(self.hdc_mem.hdc()),
                    Some(&pt_src),
                    COLORREF(0),
                    Some(&ALPHA_BLEND_FN),
                    ULW_ALPHA,
                );

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
    let duration = std::time::Duration::from_secs(10);
    let interval = std::time::Duration::from_millis(16);
    let steps = (duration.as_millis() / interval.as_millis()) as i16;
    for i in 1..=steps {
        std::thread::sleep(interval);
        let progress = (i as f32 / steps as f32 * 100.0) as i16;
        let _ = tx.send(progress);
    }
    std::thread::sleep(std::time::Duration::from_secs(1));
    let _ = tx.send(MSG_CLOSE);
    std::thread::sleep(std::time::Duration::from_secs(1));
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
    std::thread::sleep(std::time::Duration::from_secs(1));
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
