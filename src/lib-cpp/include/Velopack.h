#ifndef VELOPACK_H
#define VELOPACK_H

#ifdef _WIN32
#define DLL_EXPORT __declspec(dllexport)
#else
#define DLL_EXPORT __attribute__((visibility("default"))) __attribute__((used))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void* update_manager_t;

typedef struct {
    bool AllowVersionDowngrade;
    char* ExplicitChannel;
} update_options_t;

typedef struct {
    char* RootAppDir;
    char* UpdateExePath;
    char* PackagesDir;
    char* ManifestPath;
    char* CurrentBinaryDir;
    bool IsPortable;
} locator_config_t;

DLL_EXPORT update_manager_t velopack_new_update_manager(const char* url_or_path, const update_options_t* options, const locator_config_t* locator = 0);
//fn bridge_get_current_version(manager: &UpdateManagerOpaque) -> String;
//fn bridge_get_app_id(manager: &UpdateManagerOpaque) -> String;
//fn bridge_is_portable(manager: &UpdateManagerOpaque) -> bool;
//fn bridge_update_pending_restart(manager: &UpdateManagerOpaque) -> AssetOption;
//fn bridge_check_for_updates(manager: &UpdateManagerOpaque) -> Result<UpdateInfoOption>;
//fn bridge_download_update(manager: &UpdateManagerOpaque, to_download: UpdateInfoDto, progress: UniquePtr<DownloadCallbackManager>) -> Result<()>;
//fn bridge_wait_exit_then_apply_update(manager: &UpdateManagerOpaque, to_download: AssetDto, silent: bool, restart: bool, restart_args: Vec<String>) -> Result<()>;
//fn bridge_appbuilder_run(cb: UniquePtr<HookCallbackManager>, custom_args: StringArrayOption, locator: LocatorConfigOption, auto_apply: bool);
//fn bridge_set_logger_callback(cb: UniquePtr<LoggerCallbackManager>);

#ifdef __cplusplus
}
#endif

#endif // VELOPACK_H