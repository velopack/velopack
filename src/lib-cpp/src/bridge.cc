#include "velopack_libc/src/lib.rs.h"

// Error handling
char* lastError;
LoggerCallbackManager logMgr{};
VPKC_EXPORT size_t VPKC_CALL vpkc_get_last_error(char* pszError, size_t cError) {
    if (lastError == nullptr) {
        return 0;
    }
    
    if (pszError == nullptr || cError == 0) {
        return strlen(lastError);
    }
    
    size_t len = strlen(lastError);
    if (len > cError) {
        len = cError;
    }
    
    memcpy(pszError, lastError, len);
    return len;
}
static inline void set_last_error(const char* pszError) {
    if (lastError != nullptr) {
        free(lastError);
    }
    lastError = _strdup(pszError);
}
static inline void clear_last_error() {
    if (lastError != nullptr) {
        free(lastError);
        lastError = nullptr;
    }
}

static inline void copy_to_locator_option(vpkc_locator_t* pLocator, LocatorConfigOption& locator) {
    if (pLocator) {
        locator.has_data = true;
        locator.data.RootAppDir = pLocator->RootAppDir;
        locator.data.UpdateExePath = pLocator->UpdateExePath;
        locator.data.PackagesDir = pLocator->PackagesDir;
        locator.data.ManifestPath = pLocator->ManifestPath;
        locator.data.CurrentBinaryDir = pLocator->CurrentBinaryDir;
        locator.data.IsPortable = pLocator->IsPortable;
    } else {
        locator.has_data = false;
    }
}

static inline void copy_to_asset_dto(vpkc_asset_t* pAsset, AssetDto& asset) {
    if (pAsset != nullptr) {
        asset.PackageId = pAsset->PackageId;
        asset.Version = pAsset->Version;
        asset.Type = pAsset->Type;
        asset.FileName = pAsset->FileName;
        asset.SHA1 = pAsset->SHA1;
        asset.SHA256 = pAsset->SHA256;
        asset.NotesMarkdown = pAsset->NotesMarkdown;
        asset.NotesHtml = pAsset->NotesHtml;
        asset.Size = pAsset->Size;
    }
}

static inline void copy_to_update_info_dto(vpkc_update_info_t* pUpdate, UpdateInfoDto& update) {
    if (pUpdate != nullptr) {
        copy_to_asset_dto(&pUpdate->TargetFullRelease, update.TargetFullRelease);
        update.IsDowngrade = pUpdate->IsDowngrade;
    }
}

static inline void copy_to_asset_pointer(AssetDto& asset, vpkc_asset_t* pAsset) {
    if (pAsset != nullptr) {
        pAsset->PackageId = _strdup(asset.PackageId.c_str());
        pAsset->Version = _strdup(asset.Version.c_str());
        pAsset->Type = _strdup(asset.Type.c_str());
        pAsset->FileName = _strdup(asset.FileName.c_str());
        pAsset->SHA1 = _strdup(asset.SHA1.c_str());
        pAsset->SHA256 = _strdup(asset.SHA256.c_str());
        pAsset->NotesMarkdown = _strdup(asset.NotesMarkdown.c_str());
        pAsset->NotesHtml = _strdup(asset.NotesHtml.c_str());
        pAsset->Size = asset.Size;
    }
}

static inline void copy_to_asset_pointer(AssetOption& asset, vpkc_asset_t* pAsset) {
    if (asset.has_data && pAsset != nullptr) {
        copy_to_asset_pointer(asset.data, pAsset);
    }
}

static inline void copy_to_update_info_pointer(UpdateInfoOption& update, vpkc_update_info_t* pUpdate) {
    if (update.has_data && pUpdate != nullptr) {
        copy_to_asset_pointer(update.data.TargetFullRelease, &pUpdate->TargetFullRelease);
        pUpdate->IsDowngrade = update.data.IsDowngrade;
    }
}

// Update Manager
VPKC_EXPORT bool VPKC_CALL vpkc_new_update_manager(const char* pszUrlOrString, const vpkc_options_t* pOptions, vpkc_locator_t* pLocator, vpkc_update_manager_t* pManager) {
    clear_last_error();
    try {
        LocatorConfigOption locator{};
        UpdateOptionsDto options{};
        if (pOptions) {
            options.AllowVersionDowngrade = pOptions->AllowVersionDowngrade;
            if (pOptions->ExplicitChannel) {
                options.ExplicitChannel = pOptions->ExplicitChannel;
            }
        }
        copy_to_locator_option(pLocator, locator);
    
        ::rust::Box<::UpdateManagerOpaque> manager = bridge_new_update_manager(pszUrlOrString, options, locator);
        UpdateManagerOpaque* pOpaque = manager.into_raw();
        *pManager = pOpaque;
        return true;
    } catch (const std::exception& e) {
        set_last_error(e.what());
        return false;
    }
}
VPKC_EXPORT size_t VPKC_CALL vpkc_get_current_version(vpkc_update_manager_t* pManager, char* pszVersion, size_t cVersion) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
    ::rust::String version = bridge_get_current_version(*pOpaque);
    if (version.empty()) {
        return 0;
    }
    
    size_t len = version.size();
    if (pszVersion == nullptr || cVersion == 0) {
        return len;
    }
    
    if (len > cVersion) {
        len = cVersion;
    }
    
    memcpy(pszVersion, version.data(), len);
    return len;
}
VPKC_EXPORT size_t VPKC_CALL vpkc_get_app_id(vpkc_update_manager_t* pManager, char* pszId, size_t cId) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
    ::rust::String id = bridge_get_app_id(*pOpaque);
    if (id.empty()) {
        return 0;
    }
    
    size_t len = id.size();
    if (pszId == nullptr || cId == 0) {
        return len;
    }
    
    if (len > cId) {
        len = cId;
    }
    
    memcpy(pszId, id.data(), len);
    return len;
}
VPKC_EXPORT bool VPKC_CALL vpkc_is_portable(vpkc_update_manager_t* pManager) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
    return bridge_is_portable(*pOpaque);
}
VPKC_EXPORT bool VPKC_CALL vpkc_update_pending_restart(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
    AssetOption asset = bridge_update_pending_restart(*pOpaque);
    if (asset.has_data) {
        copy_to_asset_pointer(asset, pAsset);
        return true;
    }
    return false;
}

VPKC_EXPORT vpkc_update_check_t VPKC_CALL vpkc_check_for_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate) {
    clear_last_error();
    try {
        UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
        UpdateInfoOption update = bridge_check_for_updates(*pOpaque);
        if (update.has_data) {
            copy_to_update_info_pointer(update, pUpdate);
            return vpkc_update_check_t::UPDATE_AVAILABLE;
        }
        return vpkc_update_check_t::NO_UPDATE_AVAILABLE;
    }
    catch (const std::exception& e) {
        set_last_error(e.what());
        return vpkc_update_check_t::ERROR;
    }
}
VPKC_EXPORT bool VPKC_CALL vpkc_download_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate, vpkc_progress_callback_t cbProgress) {
    clear_last_error();
    try {
        UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
        UpdateInfoDto update{};
        
        if (!pUpdate) {
            throw new std::runtime_error("pUpdate is a required parameter");
        }
        
        copy_to_update_info_dto(pUpdate, update);
        
        DownloadCallbackManager download{};
        download.progress_cb = cbProgress;
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
        UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
        AssetDto asset{};
        
        if (!pAsset) {
            throw new std::runtime_error("pAsset is a required parameter");
        }
        
        copy_to_asset_dto(pAsset, asset);
        
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
LocatorConfigOption locator{};
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
VPKC_EXPORT void VPKC_CALL vpkc_app_set_locator(vpkc_locator_t* pLocator) {
    copy_to_locator_option(pLocator, locator);
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
VPKC_EXPORT void VPKC_CALL vpkc_app_run() {
    bridge_appbuilder_run(hooks, args, locator, autoApply);
}

// Misc functions
VPKC_EXPORT void VPKC_CALL vpkc_set_log(vpkc_log_callback_t cbLog) {
    logMgr.lob_cb = cbLog;
    bridge_set_logger_callback(&logMgr);
}
VPKC_EXPORT void VPKC_CALL vpkc_free_update_manager(vpkc_update_manager_t* pManager) {
    UpdateManagerOpaque* pOpaque = reinterpret_cast<UpdateManagerOpaque*>(*pManager);
    auto box = ::rust::Box<::UpdateManagerOpaque>::from_raw(pOpaque);
}
VPKC_EXPORT void VPKC_CALL vpkc_free_update_info(vpkc_update_info_t* pUpdateInfo) {
    if (pUpdateInfo != nullptr) {
        vpkc_free_asset(&pUpdateInfo->TargetFullRelease);
    }
}
VPKC_EXPORT void VPKC_CALL vpkc_free_asset(vpkc_asset_t* pAsset) {
    if (pAsset != nullptr) {
        if (pAsset->PackageId) {
            free(pAsset->PackageId);
            pAsset->PackageId = nullptr;
        }
        if (pAsset->Version) {
            free(pAsset->Version);
            pAsset->Version = nullptr;
        }
        if (pAsset->Type) {
            free(pAsset->Type);
            pAsset->Type = nullptr;
        }
        if (pAsset->FileName) {
            free(pAsset->FileName);
            pAsset->FileName = nullptr;
        }
        if (pAsset->SHA1) {
            free(pAsset->SHA1);
            pAsset->SHA1 = nullptr;
        }
        if (pAsset->SHA256) {
            free(pAsset->SHA256);
            pAsset->SHA256 = nullptr;
        }
        if (pAsset->NotesMarkdown) {
            free(pAsset->NotesMarkdown);
            pAsset->NotesMarkdown = nullptr;
        }
        if (pAsset->NotesHtml) {
            free(pAsset->NotesHtml);
            pAsset->NotesHtml = nullptr;
        }
    }
}