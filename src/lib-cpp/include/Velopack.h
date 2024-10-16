#ifndef VELOPACK_H
#define VELOPACK_H

#include <stddef.h>  // For size_t
#include <stdbool.h> // For bool
#include <stdint.h> // For uint64_t, uint32_t

#ifdef __cplusplus
#include <string>
#include <optional>
#include <vector>
#include <stdexcept>
#endif

#if defined(VELOPACK_LIBC_EXPORTS) && defined(_WIN32)
#define VPKC_EXPORT __declspec(dllexport)
#pragma comment(linker, "/EXPORT:vpkc_new_update_manager")
#elif defined(VELOPACK_LIBC_EXPORTS) && !defined(_WIN32)
#define VPKC_EXPORT __attribute__((visibility("default"))) __attribute__((used))
#else
#define VPKC_EXPORT
#endif

#define VPKC_CALL __cdecl

#ifdef __cplusplus
extern "C" {
#endif

typedef void* vpkc_update_manager_t;
typedef void (*vpkc_progress_callback_t)(size_t progress);
typedef void (*vpkc_log_callback_t)(char* pszLevel, char* pszMessage);
typedef void (*vpkc_hook_callback_t)(char* pszAppVersion);

typedef struct {
    bool AllowVersionDowngrade;
    char* ExplicitChannel;
} vpkc_options_t;

typedef enum {
    UPDATE_AVAILABLE = 0,
    NO_UPDATE_AVAILABLE = 1,
    ERROR = 2,
} vpkc_update_check_t;

typedef struct {
    char* RootAppDir;
    char* UpdateExePath;
    char* PackagesDir;
    char* ManifestPath;
    char* CurrentBinaryDir;
    bool IsPortable;
} vpkc_locator_t;

typedef struct {
    char* PackageId;
    char* Version;
    char* Type;
    char* FileName;
    char* SHA1;
    char* SHA256;
    uint64_t Size;
    char* NotesMarkdown;
    char* NotesHtml;
} vpkc_asset_t;

typedef struct {
    vpkc_asset_t TargetFullRelease;
    bool IsDowngrade;
} vpkc_update_info_t;

VPKC_EXPORT bool VPKC_CALL vpkc_new_update_manager(const char* pszUrlOrString, const vpkc_options_t* pOptions, const vpkc_locator_t* locator, vpkc_update_manager_t* pManager);
VPKC_EXPORT size_t VPKC_CALL vpkc_get_current_version(vpkc_update_manager_t* pManager, char* pszVersion, size_t cVersion);
VPKC_EXPORT size_t VPKC_CALL vpkc_get_app_id(vpkc_update_manager_t* pManager, char* pszId, size_t cId);
VPKC_EXPORT bool VPKC_CALL vpkc_is_portable(vpkc_update_manager_t* pManager);
VPKC_EXPORT bool VPKC_CALL vpkc_update_pending_restart(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset);
VPKC_EXPORT vpkc_update_check_t VPKC_CALL vpkc_check_for_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate);
VPKC_EXPORT bool VPKC_CALL vpkc_download_updates(vpkc_update_manager_t* pManager, const vpkc_update_info_t* pUpdate, vpkc_progress_callback_t cbProgress);
VPKC_EXPORT bool VPKC_CALL vpkc_wait_exit_then_apply_update(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset, bool bSilent, bool bRestart, char** pRestartArgs, size_t cRestartArgs);

VPKC_EXPORT void VPKC_CALL vpkc_app_set_auto_apply_on_startup(bool bAutoApply);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_args(char** pArgs, size_t cArgs);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_locator(vpkc_locator_t* pLocator);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_install(vpkc_hook_callback_t cbAfterInstall);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_uninstall(vpkc_hook_callback_t cbBeforeUninstall);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_update(vpkc_hook_callback_t cbBeforeUpdate);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_update(vpkc_hook_callback_t cbAfterUpdate);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_first_run(vpkc_hook_callback_t cbFirstRun);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_restarted(vpkc_hook_callback_t cbRestarted);
VPKC_EXPORT void VPKC_CALL vpkc_app_run();

VPKC_EXPORT size_t VPKC_CALL vpkc_get_last_error(char* pszError, size_t cError);
VPKC_EXPORT void VPKC_CALL vpkc_set_log(vpkc_log_callback_t cbLog);
VPKC_EXPORT void VPKC_CALL vpkc_free_update_manager(vpkc_update_manager_t* pManager);
VPKC_EXPORT void VPKC_CALL vpkc_free_update_info(vpkc_update_info_t* pManager);
VPKC_EXPORT void VPKC_CALL vpkc_free_asset(vpkc_asset_t* pManager);

#ifdef __cplusplus
}
#endif

#ifdef __cplusplus

namespace Velopack {
    struct VelopackAsset {
        std::string PackageId;
        std::string Version;
        std::string Type;
        std::string FileName;
        std::string SHA1;
        std::string SHA256;
        uint64_t Size;
        std::string NotesMarkdown;
        std::string NotesHtml;
    };
    
    struct UpdateInfo {
        VelopackAsset TargetFullRelease;
        bool IsDowngrade;
    };
    
    struct UpdateOptions {
        bool AllowVersionDowngrade;
        std::string ExplicitChannel;
    };
    
    struct VelopackLocator {
        std::string RootAppDir;
        std::string UpdateExePath;
        std::string PackagesDir;
        std::string ManifestPath;
        std::string CurrentBinaryDir;
        bool IsPortable;
    };
    
    static inline void throw_last_error() {
        size_t neededSize = vpkc_get_last_error(nullptr, 0);
        std::string strError(neededSize, '\0');
        vpkc_get_last_error(&strError[0], neededSize);
        throw std::runtime_error(strError);
    }
    
    static inline vpkc_locator_t to_vpkc(const VelopackLocator& locator) {
        return {
            const_cast<char*>(locator.RootAppDir.c_str()),
            const_cast<char*>(locator.UpdateExePath.c_str()),
            const_cast<char*>(locator.PackagesDir.c_str()),
            const_cast<char*>(locator.ManifestPath.c_str()),
            const_cast<char*>(locator.CurrentBinaryDir.c_str()),
            locator.IsPortable
        };
    }
    
    static inline vpkc_options_t to_vpkc(const UpdateOptions& options) {
        return {
            options.AllowVersionDowngrade,
            const_cast<char*>(options.ExplicitChannel.c_str())
        };
    }
    
    static inline vpkc_asset_t to_vpkc(const VelopackAsset& asset) {
        return {
            const_cast<char*>(asset.PackageId.c_str()),
            const_cast<char*>(asset.Version.c_str()),
            const_cast<char*>(asset.Type.c_str()),
            const_cast<char*>(asset.FileName.c_str()),
            const_cast<char*>(asset.SHA1.c_str()),
            const_cast<char*>(asset.SHA256.c_str()),
            asset.Size,
            const_cast<char*>(asset.NotesMarkdown.c_str()),
            const_cast<char*>(asset.NotesHtml.c_str())
        };
    }
    
    static inline VelopackAsset from_vpkc(const vpkc_asset_t& asset) {
        return {
            asset.PackageId,
            asset.Version,
            asset.Type,
            asset.FileName,
            asset.SHA1,
            asset.SHA256,
            asset.Size,
            asset.NotesMarkdown,
            asset.NotesHtml
        };
    }
    
    static inline vpkc_update_info_t to_vpkc(const UpdateInfo& update) {
        return {
            to_vpkc(update.TargetFullRelease),
            update.IsDowngrade
        };
    }
    
    static inline UpdateInfo from_vpkc(const vpkc_update_info_t& update) {
        return {
            from_vpkc(update.TargetFullRelease),
            update.IsDowngrade
        };
    }
    
    class VelopackApp {
    private:
        VelopackApp();
    public:
        static VelopackApp Build() { 
            return VelopackApp(); 
        };
        VelopackApp& SetAutoApplyOnStartup(bool bAutoApply) {
            vpkc_app_set_auto_apply_on_startup(bAutoApply);
            return *this;
        };
        VelopackApp& SetArgs(const std::vector<std::string>& args) {
            char** pArgs = new char*[args.size()];
            for (size_t i = 0; i < args.size(); i++) {
                pArgs[i] = new char[args[i].size() + 1];
                strcpy_s(pArgs[i], args[i].size() + 1, args[i].c_str());
            }
            vpkc_app_set_args(pArgs, args.size());
            
            // Free all the memory
            for (size_t i = 0; i < args.size(); i++) {
                delete[] pArgs[i];
            }
            delete[] pArgs;
            return *this;
        };
        VelopackApp& SetLocator(const VelopackLocator& locator) {
            vpkc_locator_t vpkc_locator = to_vpkc(locator);
            vpkc_app_set_locator(const_cast<vpkc_locator_t*>(&vpkc_locator));
            return *this;
        };
        VelopackApp& OnAfterInstall(vpkc_hook_callback_t cbInstall) {
            vpkc_app_set_hook_after_install(cbInstall);
            return *this;
        };
        VelopackApp& OnBeforeUninstall(vpkc_hook_callback_t cbInstall) {
            vpkc_app_set_hook_before_uninstall(cbInstall);
            return *this;
        };
        VelopackApp& OnBeforeUpdate(vpkc_hook_callback_t cbInstall) {
            vpkc_app_set_hook_before_update(cbInstall);
            return *this;
        };
        VelopackApp& OnAfterUpdate(vpkc_hook_callback_t cbInstall) {
            vpkc_app_set_hook_after_update(cbInstall);
            return *this;
        };
        VelopackApp& OnFirstRun(vpkc_hook_callback_t cbInstall) {
            vpkc_app_set_hook_first_run(cbInstall);
            return *this;
        };
        VelopackApp& OnRestarted(vpkc_hook_callback_t cbInstall) {
            vpkc_app_set_hook_restarted(cbInstall);
            return *this;
        };
        void Run() {
            vpkc_app_run();
        };
    };

    class UpdateManager {
    private:
        vpkc_update_manager_t m_pManager;
    public:
        UpdateManager(const std::string& urlOrPath, const UpdateOptions* options, const VelopackLocator* locator) {
            vpkc_options_t* pOptions = nullptr;
            if (options != nullptr) {
                vpkc_options_t vpkc_options = to_vpkc(*options);
                pOptions = const_cast<vpkc_options_t*>(&vpkc_options);
            }
            
            vpkc_locator_t* pLocator = nullptr;
            if (locator != nullptr) {
                vpkc_locator_t vpkc_locator = to_vpkc(*locator);
                pLocator = const_cast<vpkc_locator_t*>(&vpkc_locator);
            }
            
            if (0 != vpkc_new_update_manager(urlOrPath.c_str(), pOptions, pLocator, &m_pManager)) {
                throw_last_error();
            }
        };
        ~UpdateManager() {
            vpkc_free_update_manager(&m_pManager);
        };
        bool IsPortable() noexcept {
            return vpkc_is_portable(&m_pManager);
        };
        std::string GetCurrentVersion() noexcept {
            size_t neededSize = vpkc_get_current_version(&m_pManager, nullptr, 0);
            std::string strVersion(neededSize, '\0');
            vpkc_get_current_version(&m_pManager, &strVersion[0], neededSize);
            return strVersion;
        };
        std::string GetAppId() noexcept {
            size_t neededSize = vpkc_get_app_id(&m_pManager, nullptr, 0);
            std::string strId(neededSize, '\0');
            vpkc_get_app_id(&m_pManager, &strId[0], neededSize);
            return strId;
        };
        std::optional<VelopackAsset> UpdatePendingRestart() noexcept {
            vpkc_asset_t asset;
            if (vpkc_update_pending_restart(&m_pManager, &asset)) {
                VelopackAsset cpp_asset = from_vpkc(asset);
                vpkc_free_asset(&asset);
                return cpp_asset;
            }
            return std::nullopt;
        };
        std::optional<UpdateInfo> CheckForUpdates() {
            vpkc_update_info_t update;
            vpkc_update_check_t result = vpkc_check_for_updates(&m_pManager, &update);
            switch (result) {
                case vpkc_update_check_t::ERROR:
                    throw_last_error();
                    return std::nullopt;
                case vpkc_update_check_t::NO_UPDATE_AVAILABLE:
                    return std::nullopt;
                case vpkc_update_check_t::UPDATE_AVAILABLE:
                    UpdateInfo cpp_info = from_vpkc(update);
                    vpkc_free_update_info(&update);
                    return cpp_info;
            }
        };
        void DownloadUpdates(const UpdateInfo& update, vpkc_progress_callback_t progress) {
            vpkc_update_info_t vpkc_update = to_vpkc(update);
            if (!vpkc_download_updates(&m_pManager, &vpkc_update, progress)) {
                throw_last_error();
            }
        };
        void WaitExitThenApplyUpdate(const VelopackAsset& asset, bool silent, bool restart, std::vector<std::string> restartArgs) {
            char** pRestartArgs = new char*[restartArgs.size()];
            for (size_t i = 0; i < restartArgs.size(); i++) {
                pRestartArgs[i] = new char[restartArgs[i].size() + 1];
                strcpy_s(pRestartArgs[i], restartArgs[i].size() + 1, restartArgs[i].c_str());
            }
            
            bool result = vpkc_wait_exit_then_apply_update(&m_pManager, &to_vpkc(asset), silent, restart, pRestartArgs, restartArgs.size());
            
            // Free all the memory
            for (size_t i = 0; i < restartArgs.size(); i++) {
                delete[] pRestartArgs[i];
            }
            delete[] pRestartArgs;
            
            if (!result) {
                throw_last_error();
            }
        };
        void WaitExitThenApplyUpdate(const UpdateInfo& asset, bool silent, bool restart, std::vector<std::string> restartArgs) {
            this->WaitExitThenApplyUpdate(asset.TargetFullRelease, silent, restart, restartArgs);
        };
    };
}

#endif // __cplusplus

#endif // VELOPACK_H