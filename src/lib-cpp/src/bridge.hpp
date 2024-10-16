#pragma once
#include "rust/cxx.h"
#include "velopack_libc/include/Velopack.h"

struct HookCallbackManager {
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