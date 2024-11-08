// Uncomment to enable debug type checking
// #pragma include_alias( "velopack_libc/src/lib.rs.h", "../../../target/cxxbridge/velopack_libc/src/lib.rs.h" )
// #pragma include_alias( "velopack_libc/include/Velopack.h", "../include/Velopack.h" )
// #pragma include_alias( "velopack_libc/src/bridge.hpp", "bridge.hpp" )
// #pragma include_alias( "rust/cxx.h", "../../../target/cxxbridge/rust/cxx.h" )

#include "velopack_libc/src/lib.rs.h"

static inline std::string to_bridgestring(const char* psz) {
    return psz == nullptr ? "" : psz;
}

static inline char* to_cstring(const std::string& str) {
    return const_cast<char*>(str.c_str());
}

static inline char* to_cstring_opt(const std::optional<std::string>& str) {
    return str.has_value() ? to_cstring(str.value()) : nullptr;
}

static inline StringOption to_bridgestring_opt(const char* psz) {
    StringOption opt;
    if (psz == nullptr) {
        opt.has_data = false;
        return opt;
    }
    opt.has_data = true;
    opt.data = psz;
    return opt;
}

static inline void allocate_string(::rust::String& str, char** ppsz) {
#ifdef _WIN32
    *ppsz = _strdup(str.c_str());
#else
    *ppsz = strdup(str.c_str());
#endif
}

static inline void allocate_string_opt(StringOption str, char** ppsz) {
    if (str.has_data) {
        allocate_string(str.data, ppsz);
    } else {
        *ppsz = nullptr;
    }
}

// !! AUTO-GENERATED-START BRIDGE_MAPPING
static inline VelopackLocatorConfigDto to_bridge(vpkc_locator_config_t* pDto) {
    if (pDto == nullptr) { return {}; }
    return {
        to_bridgestring(pDto->RootAppDir),
        to_bridgestring(pDto->UpdateExePath),
        to_bridgestring(pDto->PackagesDir),
        to_bridgestring(pDto->ManifestPath),
        to_bridgestring(pDto->CurrentBinaryDir),
        pDto->IsPortable,
    };
}

static inline VelopackLocatorConfigDtoOption to_bridge_opt(vpkc_locator_config_t* pDto) {
    VelopackLocatorConfigDtoOption opt;
    if (pDto == nullptr) {
        opt.has_data = false;
        return opt;
    }

    opt.has_data = true;
    opt.data = to_bridge(pDto);
    return opt;
}

static inline void allocate_velopacklocatorconfig(VelopackLocatorConfigDto bridgeDto, vpkc_locator_config_t* pDto) {
    if (pDto == nullptr) { return; }
    allocate_string(bridgeDto.RootAppDir, &pDto->RootAppDir);
    allocate_string(bridgeDto.UpdateExePath, &pDto->UpdateExePath);
    allocate_string(bridgeDto.PackagesDir, &pDto->PackagesDir);
    allocate_string(bridgeDto.ManifestPath, &pDto->ManifestPath);
    allocate_string(bridgeDto.CurrentBinaryDir, &pDto->CurrentBinaryDir);
    pDto->IsPortable = bridgeDto.IsPortable;
}

static inline void free_velopacklocatorconfig(vpkc_locator_config_t* pDto) {
    if (pDto == nullptr) { return; }
    free(pDto->RootAppDir);
    free(pDto->UpdateExePath);
    free(pDto->PackagesDir);
    free(pDto->ManifestPath);
    free(pDto->CurrentBinaryDir);
}

static inline VelopackAssetDto to_bridge(vpkc_asset_t* pDto) {
    if (pDto == nullptr) { return {}; }
    return {
        to_bridgestring(pDto->PackageId),
        to_bridgestring(pDto->Version),
        to_bridgestring(pDto->Type),
        to_bridgestring(pDto->FileName),
        to_bridgestring(pDto->SHA1),
        to_bridgestring(pDto->SHA256),
        pDto->Size,
        to_bridgestring(pDto->NotesMarkdown),
        to_bridgestring(pDto->NotesHtml),
    };
}

static inline VelopackAssetDtoOption to_bridge_opt(vpkc_asset_t* pDto) {
    VelopackAssetDtoOption opt;
    if (pDto == nullptr) {
        opt.has_data = false;
        return opt;
    }

    opt.has_data = true;
    opt.data = to_bridge(pDto);
    return opt;
}

static inline void allocate_velopackasset(VelopackAssetDto bridgeDto, vpkc_asset_t* pDto) {
    if (pDto == nullptr) { return; }
    allocate_string(bridgeDto.PackageId, &pDto->PackageId);
    allocate_string(bridgeDto.Version, &pDto->Version);
    allocate_string(bridgeDto.Type, &pDto->Type);
    allocate_string(bridgeDto.FileName, &pDto->FileName);
    allocate_string(bridgeDto.SHA1, &pDto->SHA1);
    allocate_string(bridgeDto.SHA256, &pDto->SHA256);
    pDto->Size = bridgeDto.Size;
    allocate_string(bridgeDto.NotesMarkdown, &pDto->NotesMarkdown);
    allocate_string(bridgeDto.NotesHtml, &pDto->NotesHtml);
}

static inline void free_velopackasset(vpkc_asset_t* pDto) {
    if (pDto == nullptr) { return; }
    free(pDto->PackageId);
    free(pDto->Version);
    free(pDto->Type);
    free(pDto->FileName);
    free(pDto->SHA1);
    free(pDto->SHA256);
    free(pDto->NotesMarkdown);
    free(pDto->NotesHtml);
}

static inline UpdateInfoDto to_bridge(vpkc_update_info_t* pDto) {
    if (pDto == nullptr) { return {}; }
    return {
        to_bridge(&pDto->TargetFullRelease),
        pDto->IsDowngrade,
    };
}

static inline UpdateInfoDtoOption to_bridge_opt(vpkc_update_info_t* pDto) {
    UpdateInfoDtoOption opt;
    if (pDto == nullptr) {
        opt.has_data = false;
        return opt;
    }

    opt.has_data = true;
    opt.data = to_bridge(pDto);
    return opt;
}

static inline void allocate_updateinfo(UpdateInfoDto bridgeDto, vpkc_update_info_t* pDto) {
    if (pDto == nullptr) { return; }
    allocate_velopackasset(bridgeDto.TargetFullRelease, &pDto->TargetFullRelease);
    pDto->IsDowngrade = bridgeDto.IsDowngrade;
}

static inline void free_updateinfo(vpkc_update_info_t* pDto) {
    if (pDto == nullptr) { return; }
    free_velopackasset(&pDto->TargetFullRelease);
}

static inline UpdateOptionsDto to_bridge(vpkc_update_options_t* pDto) {
    if (pDto == nullptr) { return {}; }
    return {
        pDto->AllowVersionDowngrade,
        to_bridgestring_opt(pDto->ExplicitChannel),
    };
}

static inline UpdateOptionsDtoOption to_bridge_opt(vpkc_update_options_t* pDto) {
    UpdateOptionsDtoOption opt;
    if (pDto == nullptr) {
        opt.has_data = false;
        return opt;
    }

    opt.has_data = true;
    opt.data = to_bridge(pDto);
    return opt;
}

static inline void allocate_updateoptions(UpdateOptionsDto bridgeDto, vpkc_update_options_t* pDto) {
    if (pDto == nullptr) { return; }
    pDto->AllowVersionDowngrade = bridgeDto.AllowVersionDowngrade;
    allocate_string_opt(bridgeDto.ExplicitChannel, &pDto->ExplicitChannel);
}

static inline void free_updateoptions(vpkc_update_options_t* pDto) {
    if (pDto == nullptr) { return; }
    free(pDto->ExplicitChannel);
}
// !! AUTO-GENERATED-END BRIDGE_MAPPING

static inline size_t return_c_string(std::string& value, char* psz, size_t csz) {
    if (value.empty()) {
        return 0;
    }

    const char* c_str = value.c_str();
    size_t len = strlen(c_str);
    if (psz == nullptr || csz == 0 || len == 0) {
        // no buffer has been provided, return the length
        return len;
    }
    
    // shorten the length if it's longer than the buffer
    if (len > csz) {
        len = csz;
    }
    
    // copy the string to the buffer
    memcpy(psz, c_str, len);
    return len;
}

// Error handling
std::string lastError;
VPKC_EXPORT size_t VPKC_CALL vpkc_get_last_error(char* pszError, size_t cError) {
    return return_c_string(lastError, pszError, cError);
}
static inline void set_last_error(const char* pszError) {
    lastError = pszError;
}
static inline void clear_last_error() {
    lastError.clear();
}

// Update Manager
VPKC_EXPORT bool VPKC_CALL vpkc_new_update_manager(const char* pszUrlOrString, vpkc_update_options_t* pOptions, vpkc_locator_config_t* pLocator, vpkc_update_manager_t** pManager) {
    clear_last_error();
    try {
        if (pManager == nullptr) {
            set_last_error("pManager cannot be null");
            return false;
        }

        VelopackLocatorConfigDtoOption locator = to_bridge_opt(pLocator);
        UpdateOptionsDtoOption options = to_bridge_opt(pOptions);

        ::rust::Box<::UpdateManagerOpaque> manager = bridge_new_update_manager(pszUrlOrString, options, locator);
        UpdateManagerOpaque* pOpaque = manager.into_raw();
        *pManager = reinterpret_cast<vpkc_update_manager_t*>(pOpaque);
        return true;
    } catch (const std::exception& e) {
        set_last_error(e.what());
        return false;
    }
}
VPKC_EXPORT size_t VPKC_CALL vpkc_get_current_version(vpkc_update_manager_t* pManager, char* pszVersion, size_t cVersion) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
    std::string version = (std::string)bridge_get_current_version(*pOpaque);
    return return_c_string(version, pszVersion, cVersion);
}
VPKC_EXPORT size_t VPKC_CALL vpkc_get_app_id(vpkc_update_manager_t* pManager, char* pszId, size_t cId) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
    std::string id = (std::string)bridge_get_app_id(*pOpaque);
    return return_c_string(id, pszId, cId);

}
VPKC_EXPORT bool VPKC_CALL vpkc_is_portable(vpkc_update_manager_t* pManager) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
    return bridge_is_portable(*pOpaque);
}
VPKC_EXPORT bool VPKC_CALL vpkc_update_pending_restart(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
    VelopackAssetDtoOption asset = bridge_update_pending_restart(*pOpaque);
    if (asset.has_data) {
        allocate_velopackasset(asset.data, pAsset);
        return true;
    }
    return false;
}
VPKC_EXPORT vpkc_update_check_t VPKC_CALL vpkc_check_for_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate) {
    clear_last_error();
    try {
        UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
        UpdateInfoDtoOption update = bridge_check_for_updates(*pOpaque);
        if (update.has_data) {
            allocate_updateinfo(update.data, pUpdate);
            return vpkc_update_check_t::UPDATE_AVAILABLE;
        }
        return vpkc_update_check_t::NO_UPDATE_AVAILABLE;
    }
    catch (const std::exception& e) {
        set_last_error(e.what());
        return vpkc_update_check_t::UPDATE_ERROR;
    }
}
VPKC_EXPORT bool VPKC_CALL vpkc_download_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate, vpkc_progress_callback_t cbProgress, void* pUserData) {
    clear_last_error();
    try {
        if (!pUpdate) {
            set_last_error("pUpdate is a required parameter");
            return false;
        }

        UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
        UpdateInfoDto update = to_bridge(pUpdate);

        DownloadCallbackManager download{};
        download.progress_cb = cbProgress;
        download.user_data = pUserData;
        bridge_download_updates(*pOpaque, update, download);
        return true;
    }
    catch (const std::exception& e) {
        set_last_error(e.what());
        return false;
    }
}
VPKC_EXPORT bool VPKC_CALL vpkc_wait_exit_then_apply_update(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset, bool bSilent, bool bRestart, char** pRestartArgs, size_t cRestartArgs) {
    clear_last_error();
    try {
        if (!pAsset) {
            set_last_error("pAsset is a required parameter");
            return false;
        }

        UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
        VelopackAssetDto asset = to_bridge(pAsset);

        ::rust::Vec<::rust::String> restartArgs{};
        for (size_t i = 0; i < cRestartArgs; i++) {
            restartArgs.push_back(pRestartArgs[i]);
        }
        
        bridge_wait_exit_then_apply_update(*pOpaque, asset, bSilent, bRestart, restartArgs);
        return true;
    }
    catch (const std::exception& e) {
        set_last_error(e.what());
        return false;
    }
}

// VelopackApp
bool autoApply = true;
StringArrayOption args{};
VelopackLocatorConfigDtoOption locator{};
HookCallbackManager hooks{};

VPKC_EXPORT void VPKC_CALL vpkc_app_set_auto_apply_on_startup(bool bAutoApply) {
    autoApply = bAutoApply;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_args(char** pArgs, size_t cArgs) {
    args.has_data = true;
    args.data.clear();
    for (size_t i = 0; i < cArgs; i++) {
        args.data.push_back(pArgs[i]);
    }
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_locator(vpkc_locator_config_t* pLocator) {
    locator = to_bridge_opt(pLocator);
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_install(vpkc_hook_callback_t cbAfterInstall) {
    hooks.after_install = cbAfterInstall;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_uninstall(vpkc_hook_callback_t cbBeforeUninstall) {
    hooks.before_uninstall = cbBeforeUninstall;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_update(vpkc_hook_callback_t cbBeforeUpdate) {
    hooks.before_update = cbBeforeUpdate;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_update(vpkc_hook_callback_t cbAfterUpdate) {
    hooks.after_update = cbAfterUpdate;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_first_run(vpkc_hook_callback_t cbFirstRun) {
    hooks.first_run = cbFirstRun;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_restarted(vpkc_hook_callback_t cbRestarted) {
    hooks.restarted = cbRestarted;
}
VPKC_EXPORT void VPKC_CALL vpkc_app_run(void* pUserData) {
    hooks.user_data = pUserData;
    bridge_appbuilder_run(hooks, args, locator, autoApply);
}

// Misc functions
LoggerCallbackManager logMgr{};
VPKC_EXPORT void VPKC_CALL vpkc_set_logger(vpkc_log_callback_t cbLog, void* pUserData) {
    logMgr.lob_cb = cbLog;
    logMgr.user_data = pUserData;
    bridge_set_logger_callback(&logMgr);
}
VPKC_EXPORT void VPKC_CALL vpkc_free_update_manager(vpkc_update_manager_t* pManager) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(pManager);
    auto box = ::rust::Box<::UpdateManagerOpaque>::from_raw(pOpaque);
    // this will free when the box goes out of scope
}
VPKC_EXPORT void VPKC_CALL vpkc_free_update_info(vpkc_update_info_t* pUpdateInfo) {
    free_updateinfo(pUpdateInfo);
}
VPKC_EXPORT void VPKC_CALL vpkc_free_asset(vpkc_asset_t* pAsset) {
    free_velopackasset(pAsset);
}