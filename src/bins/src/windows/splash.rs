use super::strings::string_to_wide;
use anyhow::{bail, Result};
use image::{codecs::gif::GifDecoder, AnimationDecoder, DynamicImage, ImageFormat, ImageReader};
use std::sync::mpsc::{self, Receiver, Sender};
use std::{io::Cursor, thread};
use windows::{
    core::HRESULT,
    Win32::{
        Foundation::{COLORREF, HINSTANCE, HWND, LPARAM, LRESULT, POINT, RECT, S_OK, WPARAM},
        Graphics::Gdi::*,
        System::LibraryLoader::GetModuleHandleW,
        UI::{Controls::*, WindowsAndMessaging::*},
    },
};

const TMR_GIF: usize = 1;
const MSG_NOMESSAGE: i16 = -99;

pub const MSG_CLOSE: i16 = -1;
pub const MSG_INDEFINITE: i16 = -2;

pub fn show_progress_dialog<T1: AsRef<str>, T2: AsRef<str>>(window_title: T1, content: T2) -> Sender<i16> {
    let window_title = window_title.as_ref().to_string();
    let content = content.as_ref().to_string();
    let (tx, rx) = mpsc::channel::<i16>();
    thread::spawn(move || {
        show_com_ctl_progress_dialog(rx, &window_title, &content);
    });
    tx
}

pub fn show_splash_dialog(app_name: String, imgstream: Option<Vec<u8>>) -> Sender<i16> {
    let (tx, rx) = mpsc::channel::<i16>();
    thread::spawn(move || {
        info!("Showing splash screen immediately...");
        if imgstream.is_some() {
            if let Err(e) = unsafe { SplashWindow::run(app_name, imgstream.unwrap(), rx) } {
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
    w: i32,
    h: i32,
    hdc_screen: HDC,
}

fn average(numbers: &[u32]) -> u32 {
    let sum: u32 = numbers.iter().sum();
    let count = numbers.len() as u32;
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

impl SplashWindow {
    pub unsafe fn run(app_name: String, img_stream: Vec<u8>, rx: Receiver<i16>) -> Result<()> {
        let mut delays = Vec::new();
        let mut frames = Vec::new();

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
                let mut vec = dynamic.to_rgba8().to_vec();
                convert_rgba_to_bgra(&mut vec);
                let bitmap = CreateBitmap(w, h, 1, 32, Some(vec.as_mut_ptr() as *mut _));
                frames.push(bitmap);
            }
            info!("Successfully loaded {} frames.", frames.len());
        } else {
            info!("Loading static image (detected {:?})...", fmt);
            delays.push(16u32); // 60 fps
            let img_cursor = Cursor::new(&img_stream);
            let img_decoder = ImageReader::new(img_cursor).with_guessed_format()?.decode()?;
            let mut vec = img_decoder.to_rgba8().to_vec();
            convert_rgba_to_bgra(&mut vec);
            let bitmap = CreateBitmap(w, h, 1, 32, Some(vec.as_mut_ptr() as *mut _));
            frames.push(bitmap);
            info!("Successfully loaded.");
        }

        // TODO: only support a fixed frame delay for now. Maybe should
        // support a variable frame delay in the future.
        let delay = average(&delays);

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

        // center the window on the screen containing the cursor
        let mut lppoint = POINT::default();
        GetCursorPos(&mut lppoint)?;

        let h_monitor = MonitorFromPoint(lppoint, MONITOR_DEFAULTTONEAREST);
        let mut mi: MONITORINFO = Default::default();
        mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
        if GetMonitorInfoW(h_monitor, &mut mi).as_bool() {
            // center the window in the monitor
            let rc_monitor = mi.rcMonitor;
            let left = (rc_monitor.left + rc_monitor.right - w) / 2;
            let top = (rc_monitor.top + rc_monitor.bottom - h) / 2;
            lppoint.x = left;
            lppoint.y = top;
        } else {
            // fallback to work area if monitor info is not available
            let mut rc_work_area: RECT = Default::default();
            SystemParametersInfoW(
                SPI_GETWORKAREA,
                0,
                Some(&mut rc_work_area as *mut RECT as *mut _),
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS(0),
            )?;
            lppoint.x = (rc_work_area.left + rc_work_area.right - w) / 2;
            lppoint.y = (rc_work_area.top + rc_work_area.bottom - h) / 2;
        }

        let hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            class_name.as_pcwstr(),
            app_name.as_pcwstr(),
            WS_CLIPCHILDREN | WS_POPUP,
            lppoint.x,
            lppoint.y,
            w,
            h,
            None,
            None,
            Some(h_instance),
            None,
        )?;

        let desktop = unsafe { GetDesktopWindow() };
        let hdc_screen = unsafe { GetDC(Some(desktop)) };

        let data_ptr = Box::into_raw(Box::new(Self { frames, rx, frame_idx: 0, w, h, progress: 0, hdc_screen }));

        SetWindowLongPtrW(hwnd, GWL_USERDATA, data_ptr as isize);
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

        let arc_data = Box::from_raw(data_ptr); // drop the reference to the data
        let _ = DeleteDC(arc_data.hdc_screen);
        for h_bitmap in &arc_data.frames {
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
                let hdc = BeginPaint(hwnd, &mut ps);
                if hdc.is_invalid() {
                    return 0;
                }

                // get the bitmap for the current frame
                let h_bitmap = self.frames[self.frame_idx];

                // create double buffer
                let hdc_mem = CreateCompatibleDC(Some(self.hdc_screen));
                let buffer_bmp = CreateCompatibleBitmap(self.hdc_screen, self.w, self.h);
                let buffer_old = SelectObject(hdc_mem, buffer_bmp.into());

                // load image into hdc_bitmap
                let hdc_bitmap = CreateCompatibleDC(Some(self.hdc_screen));
                let bitmap_old = SelectObject(hdc_bitmap, h_bitmap.into());

                // draw background to hdc_mem
                let background_brush = CreateSolidBrush(rgb(0, 0, 0));
                FillRect(hdc_mem, &RECT { left: 0, top: 0, right: self.w, bottom: self.h }, background_brush);

                // copy bitmap from hdc_bitmap to hdc_mem
                SetStretchBltMode(hdc_mem, STRETCH_HALFTONE);
                let _ = StretchBlt(hdc_mem, 0, 0, self.w, self.h, Some(hdc_bitmap), 0, 0, self.w, self.h, SRCCOPY);

                // draw progress bar to hdc_mem
                let progress = self.progress;
                let progress_brush = CreateSolidBrush(rgb(15, 123, 15));
                let progress_width = (self.w as f32 * (progress as f32 / 100.0)) as i32;
                let progress_rect = RECT { left: 0, bottom: self.h, right: progress_width, top: self.h - 10 };
                FillRect(hdc_mem, &progress_rect, progress_brush);

                // finally, copy hdc_mem to hdc
                let _ = BitBlt(hdc, 0, 0, self.w, self.h, Some(hdc_mem), 0, 0, SRCCOPY);

                // clean up
                let _ = DeleteObject(background_brush.into());
                let _ = DeleteObject(progress_brush.into());
                SelectObject(hdc_mem, buffer_old);
                SelectObject(hdc_bitmap, bitmap_old);
                let _ = DeleteDC(hdc_mem);
                let _ = DeleteDC(hdc_bitmap);
                let _ = DeleteObject(buffer_bmp.into());

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

pub struct ComCtlProgressWindow {
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
                SendMessageW(hwnd, TDM_SET_PROGRESS_BAR_POS.0 as u32, Some(WPARAM(next_message as usize)), Some(LPARAM(0)));
                me.last_progress = next_message;
            }
        }
    }

    return S_OK;
}

#[test]
#[ignore]
fn show_test_gif() {
    let rd = std::fs::read(r"C:\Source\Clowd\artwork\splash.gif").unwrap();
    let tx = show_splash_dialog("osu!".to_string(), Some(rd));
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
