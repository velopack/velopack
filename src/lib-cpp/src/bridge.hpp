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
    
    void install_hook(::rust::String app_version) const {
        if (after_install) {
            after_install(app_version.c_str());
        }
    };
    
    void update_hook(::rust::String app_version) const {
        if (after_update) {
            after_update(app_version.c_str());
        }
    };
    
    void obsolete_hook(::rust::String app_version) const {
        if (before_update) {
            before_update(app_version.c_str());
        }
    };
    
    void uninstall_hook(::rust::String app_version) const {
        if (before_uninstall) {
            before_uninstall(app_version.c_str());
        }
    };
    
    void firstrun_hook(::rust::String app_version) const {
        if (first_run) {
            first_run(app_version.c_str());
        }
    };
    
    void restarted_hook(::rust::String app_version) const {
        if (restarted) {
            restarted(app_version.c_str());
        }
    };
};

struct DownloadCallbackManager {
    vpkc_progress_callback_t progress_cb = nullptr;
    void download_progress(int16_t progress) const {
        if (progress_cb) {
            progress_cb(progress);
        }
    };
};

struct LoggerCallbackManager {
    vpkc_log_callback_t lob_cb = nullptr;
    void log(::rust::String level, ::rust::String message) const {
        if (lob_cb) {
            lob_cb(level.c_str(), message.c_str());
        }
    };
};