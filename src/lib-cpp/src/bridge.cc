#include "velopack_libc/src/lib.rs.h"

// Update Manager
VPKC_EXPORT bool VPKC_CALL vpkc_new_update_manager(const char* pszUrlOrString, const vpkc_options_t* pOptions, const vpkc_locator_t* locator, vpkc_update_manager_t* pManager);
VPKC_EXPORT size_t VPKC_CALL vpkc_get_current_version(vpkc_update_manager_t* pManager, char* pszVersion, size_t cVersion);
VPKC_EXPORT size_t VPKC_CALL vpkc_get_app_id(vpkc_update_manager_t* pManager, char* pszId, size_t cId);
VPKC_EXPORT bool VPKC_CALL vpkc_is_portable(vpkc_update_manager_t* pManager);
VPKC_EXPORT bool VPKC_CALL vpkc_update_pending_restart(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset);
VPKC_EXPORT vpkc_update_check_t VPKC_CALL vpkc_check_for_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate);
VPKC_EXPORT bool VPKC_CALL vpkc_download_updates(vpkc_update_manager_t* pManager, const vpkc_update_info_t* pUpdate, vpkc_progress_callback_t cbProgress);
VPKC_EXPORT bool VPKC_CALL vpkc_wait_exit_then_apply_update(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset, bool bSilent, bool bRestart, char** pRestartArgs, size_t cRestartArgs);

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
    locator.has_data = true;
    locator.data.RootAppDir = pLocator->RootAppDir;
    locator.data.UpdateExePath = pLocator->UpdateExePath;
    locator.data.PackagesDir = pLocator->PackagesDir;
    locator.data.ManifestPath = pLocator->ManifestPath;
    locator.data.CurrentBinaryDir = pLocator->CurrentBinaryDir;
    locator.data.IsPortable = pLocator->IsPortable;
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
VPKC_EXPORT size_t VPKC_CALL vpkc_get_last_error(char* pszError, size_t cError);
VPKC_EXPORT void VPKC_CALL vpkc_set_log(vpkc_log_callback_t cbLog);
VPKC_EXPORT void VPKC_CALL vpkc_free_update_manager(vpkc_update_manager_t* pManager);
VPKC_EXPORT void VPKC_CALL vpkc_free_update_info(vpkc_update_info_t* pManager);
VPKC_EXPORT void VPKC_CALL vpkc_free_asset(vpkc_asset_t* pManager);