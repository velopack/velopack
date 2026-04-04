use serial_test::serial;
use velopack_dialogs::progress::show_apply_progress;
#[cfg(windows)]
use velopack_dialogs::splash::{show_splash_dialog, SplashOptions};

#[test]
#[serial(dialogs)]
#[ntest::timeout(2000)]
fn test_no_dialogs_show_if_silent() {
    velopack_dialogs::init();
    velopack_dialogs::set_silent(true);
    velopack_dialogs::show_generic_error("TestApp", "This is an error.");
    velopack_dialogs::show_restart_required("TestApp", "1.0.0");
    velopack_dialogs::show_uninstall_complete("TestApp");
    velopack_dialogs::show_setup_error("TestApp", "This is a setup error.");
}

#[test]
#[serial(dialogs)]
#[ignore]
fn test_show_all_dialogs() {
    velopack_dialogs::init();
    velopack_dialogs::set_silent(false);
    velopack_dialogs::show_generic_error("TestApp", "This is an error.");
    velopack_dialogs::show_restart_required("TestApp", "1.0.0");
    velopack_dialogs::show_uninstall_complete("TestApp");
    velopack_dialogs::show_setup_error("TestApp", "This is a setup error.");
}

#[cfg(windows)]
fn fixtures_dir() -> std::path::PathBuf {
    std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test/fixtures")
}

#[test]
#[serial(dialogs)]
#[ignore]
fn show_progress_window() {
    velopack_dialogs::init();
    let proxy = show_apply_progress("hello! app name", "1.2.3");
    proxy.set_progress_value_i16(25);
    std::thread::sleep(std::time::Duration::from_secs(1));
    proxy.set_progress_value_i16(50);
    std::thread::sleep(std::time::Duration::from_secs(1));
    proxy.set_progress_value_i16(75);
    std::thread::sleep(std::time::Duration::from_secs(1));
    proxy.set_progress_value_i16(100);
    std::thread::sleep(std::time::Duration::from_secs(3));
    proxy.set_progress_indeterminate();
    std::thread::sleep(std::time::Duration::from_secs(5));
    proxy.close();
    std::thread::sleep(std::time::Duration::from_secs(3));
}

#[cfg(windows)]
#[test]
#[serial(dialogs)]
#[ignore]
fn show_splash_gif() {
    velopack_dialogs::init();
    let rd = std::fs::read(fixtures_dir().join("splash-test.gif")).unwrap();
    let proxy = show_splash_dialog("osu!".to_string(), "1.0.0".to_string(), Some(rd), SplashOptions::default());
    proxy.set_progress_value_i16(25);
    std::thread::sleep(std::time::Duration::from_secs(1));
    proxy.set_progress_value_i16(50);
    std::thread::sleep(std::time::Duration::from_secs(1));
    proxy.set_progress_value_i16(75);
    std::thread::sleep(std::time::Duration::from_secs(1));
    proxy.set_progress_value_i16(100);
    std::thread::sleep(std::time::Duration::from_secs(3));
    proxy.close();
    std::thread::sleep(std::time::Duration::from_secs(3));
}

#[cfg(windows)]
#[test]
#[serial(dialogs)]
#[ignore]
fn show_splash_png_transparency() {
    velopack_dialogs::init();
    let rd = std::fs::read(fixtures_dir().join("splash-test.png")).unwrap();
    let proxy = show_splash_dialog("Beer App".to_string(), "1.0.0".to_string(), Some(rd), SplashOptions::default());
    proxy.set_progress_value_i16(50);
    std::thread::sleep(std::time::Duration::from_secs(5));
    proxy.set_progress_value_i16(100);
    std::thread::sleep(std::time::Duration::from_secs(3));
    proxy.close();
    std::thread::sleep(std::time::Duration::from_secs(1));
}

#[cfg(windows)]
#[test]
#[serial(dialogs)]
#[ignore]
fn show_splash_without_progress_bar() {
    velopack_dialogs::init();
    let rd = std::fs::read(fixtures_dir().join("splash-test.gif")).unwrap();
    let proxy = show_splash_dialog(
        "osu!".to_string(),
        "1.0.0".to_string(),
        Some(rd),
        SplashOptions { splash_progress_color: Some("None".to_string()) },
    );
    proxy.set_progress_value_i16(80);
    std::thread::sleep(std::time::Duration::from_secs(6));
}
