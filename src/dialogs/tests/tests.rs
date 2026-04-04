#[test]
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
fn test_show_all_dialogs() {
    velopack_dialogs::init();
    velopack_dialogs::set_silent(false);
    velopack_dialogs::show_generic_error("TestApp", "This is an error.");
    velopack_dialogs::show_restart_required("TestApp", "1.0.0");
    velopack_dialogs::show_uninstall_complete("TestApp");
    velopack_dialogs::show_setup_error("TestApp", "This is a setup error.");
}
