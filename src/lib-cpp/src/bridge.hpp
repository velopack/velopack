#pragma once
#include "rust/cxx.h"
#include "velopack_libc/include/Velopack.h"

struct HookCallbackManager {
    vpkc_hook_callback_t after_install = nullptr;
    vpkc_hook_callback_t before_uninstall = nullptr;
    vpkc_hook_callback_t before_update = nullptr;
    vpkc_hook_callback_t after_update = nullptr;
    vpkc_hook_callback_t first_run = nullptr;
    vpkc_hook_callback_t restarted = nullptr;

    void install_hook(::rust::String app_version) const {};
    void update_hook(::rust::String app_version) const {};
    void obsolete_hook(::rust::String app_version) const {};
    void uninstall_hook(::rust::String app_version) const {};
    void firstrun_hook(::rust::String app_version) const {};
    void restarted_hook(::rust::String app_version) const {};
};

struct DownloadCallbackManager {
    void download_progress(int16_t progress) const {};
};

struct LoggerCallbackManager {
    void log(::rust::String level, ::rust::String message) const {};
};