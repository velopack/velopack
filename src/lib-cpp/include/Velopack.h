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

#if !defined(_WIN32)
#include <string.h>
#endif

#if defined(VELOPACK_LIBC_EXPORTS) && defined(_WIN32)
#define VPKC_EXPORT __declspec(dllexport)
#define VPKC_CALL __cdecl
#pragma comment(linker, "/EXPORT:vpkc_new_update_manager")
#pragma comment(linker, "/EXPORT:vpkc_get_current_version")
#pragma comment(linker, "/EXPORT:vpkc_get_app_id")
#pragma comment(linker, "/EXPORT:vpkc_is_portable")
#pragma comment(linker, "/EXPORT:vpkc_update_pending_restart")
#pragma comment(linker, "/EXPORT:vpkc_download_updates")
#pragma comment(linker, "/EXPORT:vpkc_wait_exit_then_apply_update")
#pragma comment(linker, "/EXPORT:vpkc_app_set_auto_apply_on_startup")
#pragma comment(linker, "/EXPORT:vpkc_app_set_args")
#pragma comment(linker, "/EXPORT:vpkc_app_set_locator")
#pragma comment(linker, "/EXPORT:vpkc_app_set_hook_after_install")
#pragma comment(linker, "/EXPORT:vpkc_app_set_hook_before_uninstall")
#pragma comment(linker, "/EXPORT:vpkc_app_set_hook_before_update")
#pragma comment(linker, "/EXPORT:vpkc_app_set_hook_after_update")
#pragma comment(linker, "/EXPORT:vpkc_app_set_hook_first_run")
#pragma comment(linker, "/EXPORT:vpkc_app_set_hook_restarted")
#pragma comment(linker, "/EXPORT:vpkc_app_run")
#pragma comment(linker, "/EXPORT:vpkc_get_last_error")
#pragma comment(linker, "/EXPORT:vpkc_set_log")
#pragma comment(linker, "/EXPORT:vpkc_free_update_manager")
#pragma comment(linker, "/EXPORT:vpkc_free_update_info")
#pragma comment(linker, "/EXPORT:vpkc_free_asset")
#elif defined(VELOPACK_LIBC_EXPORTS) && !defined(_WIN32)
#define VPKC_EXPORT __attribute__((visibility("default"))) __attribute__((used))
#define VPKC_CALL
#else
#define VPKC_EXPORT
#define VPKC_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void vpkc_update_manager_t;
typedef void (*vpkc_progress_callback_t)(size_t progress);
typedef void (*vpkc_log_callback_t)(const char* pszLevel, const char* pszMessage);
typedef void (*vpkc_hook_callback_t)(const char* pszAppVersion);

typedef enum {
    UPDATE_AVAILABLE = 0,
    NO_UPDATE_AVAILABLE = 1,
    UPDATE_ERROR = 2,
} vpkc_update_check_t;

// !! AUTO-GENERATED-START C_TYPES
typedef struct {
    char* RootAppDir;
    char* UpdateExePath;
    char* PackagesDir;
    char* ManifestPath;
    char* CurrentBinaryDir;
    bool IsPortable;
} vpkc_locator_config_t;

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

typedef struct {
    bool AllowVersionDowngrade;
    char* ExplicitChannel;
} vpkc_update_options_t;
// !! AUTO-GENERATED-END C_TYPES

// Update Manager
VPKC_EXPORT bool VPKC_CALL vpkc_new_update_manager(const char* pszUrlOrString, vpkc_update_options_t* pOptions, vpkc_locator_config_t* pLocator, vpkc_update_manager_t** pManager);
VPKC_EXPORT size_t VPKC_CALL vpkc_get_current_version(vpkc_update_manager_t* pManager, char* pszVersion, size_t cVersion);
VPKC_EXPORT size_t VPKC_CALL vpkc_get_app_id(vpkc_update_manager_t* pManager, char* pszId, size_t cId);
VPKC_EXPORT bool VPKC_CALL vpkc_is_portable(vpkc_update_manager_t* pManager);
VPKC_EXPORT bool VPKC_CALL vpkc_update_pending_restart(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset);
VPKC_EXPORT vpkc_update_check_t VPKC_CALL vpkc_check_for_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate);
VPKC_EXPORT bool VPKC_CALL vpkc_download_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate, vpkc_progress_callback_t cbProgress);
VPKC_EXPORT bool VPKC_CALL vpkc_wait_exit_then_apply_update(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset, bool bSilent, bool bRestart, char** pRestartArgs, size_t cRestartArgs);

// VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_auto_apply_on_startup(bool bAutoApply);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_args(char** pArgs, size_t cArgs);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_locator(vpkc_locator_config_t* pLocator);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_install(vpkc_hook_callback_t cbAfterInstall);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_uninstall(vpkc_hook_callback_t cbBeforeUninstall);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_update(vpkc_hook_callback_t cbBeforeUpdate);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_update(vpkc_hook_callback_t cbAfterUpdate);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_first_run(vpkc_hook_callback_t cbFirstRun);
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_restarted(vpkc_hook_callback_t cbRestarted);
VPKC_EXPORT void VPKC_CALL vpkc_app_run();

// Misc functions
VPKC_EXPORT size_t VPKC_CALL vpkc_get_last_error(char* pszError, size_t cError);
VPKC_EXPORT void VPKC_CALL vpkc_set_log(vpkc_log_callback_t cbLog);
VPKC_EXPORT void VPKC_CALL vpkc_free_update_manager(vpkc_update_manager_t* pManager);
VPKC_EXPORT void VPKC_CALL vpkc_free_update_info(vpkc_update_info_t* pUpdateInfo);
VPKC_EXPORT void VPKC_CALL vpkc_free_asset(vpkc_asset_t* pAsset);

#ifdef __cplusplus
}
#endif

#ifdef __cplusplus

namespace Velopack {

static inline void throw_last_error() {
    size_t neededSize = vpkc_get_last_error(nullptr, 0);
    std::string strError(neededSize, '\0');
    vpkc_get_last_error(&strError[0], neededSize);
    throw std::runtime_error(strError);
}

static inline std::string to_cppstring(const char* psz) {
    return psz == nullptr ? "" : psz;
}

static inline char* to_cstring(const std::string& str) {
    return const_cast<char*>(str.c_str());
}

static inline char* to_cstring_opt(const std::optional<std::string>& str) {
    return str.has_value() ? to_cstring(str.value()) : nullptr;
}

static inline std::optional<std::string> to_cppstring_opt(const char* psz) {
    return psz == nullptr ? std::nullopt : std::optional<std::string>(psz);
}

static inline bool to_cppbool(bool b) { return b; }
static inline bool to_cbool(bool b) { return b; }
static inline uint64_t to_cu64(uint64_t i) { return i; }
static inline uint64_t to_cppu64(uint64_t i) { return i; }

// !! AUTO-GENERATED-START CPP_TYPES
struct VelopackLocatorConfig {
    std::string RootAppDir;
    std::string UpdateExePath;
    std::string PackagesDir;
    std::string ManifestPath;
    std::string CurrentBinaryDir;
    bool IsPortable;
};

static inline vpkc_locator_config_t to_c(const VelopackLocatorConfig& dto) {
    return {
        to_cstring(dto.RootAppDir),
        to_cstring(dto.UpdateExePath),
        to_cstring(dto.PackagesDir),
        to_cstring(dto.ManifestPath),
        to_cstring(dto.CurrentBinaryDir),
        to_cbool(dto.IsPortable),
    };
}

static inline VelopackLocatorConfig to_cpp(const vpkc_locator_config_t& dto) {
    return {
        to_cppstring(dto.RootAppDir),
        to_cppstring(dto.UpdateExePath),
        to_cppstring(dto.PackagesDir),
        to_cppstring(dto.ManifestPath),
        to_cppstring(dto.CurrentBinaryDir),
        to_cppbool(dto.IsPortable),
    };
}

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

static inline vpkc_asset_t to_c(const VelopackAsset& dto) {
    return {
        to_cstring(dto.PackageId),
        to_cstring(dto.Version),
        to_cstring(dto.Type),
        to_cstring(dto.FileName),
        to_cstring(dto.SHA1),
        to_cstring(dto.SHA256),
        to_cu64(dto.Size),
        to_cstring(dto.NotesMarkdown),
        to_cstring(dto.NotesHtml),
    };
}

static inline VelopackAsset to_cpp(const vpkc_asset_t& dto) {
    return {
        to_cppstring(dto.PackageId),
        to_cppstring(dto.Version),
        to_cppstring(dto.Type),
        to_cppstring(dto.FileName),
        to_cppstring(dto.SHA1),
        to_cppstring(dto.SHA256),
        to_cppu64(dto.Size),
        to_cppstring(dto.NotesMarkdown),
        to_cppstring(dto.NotesHtml),
    };
}

struct UpdateInfo {
    VelopackAsset TargetFullRelease;
    bool IsDowngrade;
};

static inline vpkc_update_info_t to_c(const UpdateInfo& dto) {
    return {
        to_c(dto.TargetFullRelease),
        to_cbool(dto.IsDowngrade),
    };
}

static inline UpdateInfo to_cpp(const vpkc_update_info_t& dto) {
    return {
        to_cpp(dto.TargetFullRelease),
        to_cppbool(dto.IsDowngrade),
    };
}

struct UpdateOptions {
    bool AllowVersionDowngrade;
    std::optional<std::string> ExplicitChannel;
};

static inline vpkc_update_options_t to_c(const UpdateOptions& dto) {
    return {
        to_cbool(dto.AllowVersionDowngrade),
        to_cstring_opt(dto.ExplicitChannel),
    };
}

static inline UpdateOptions to_cpp(const vpkc_update_options_t& dto) {
    return {
        to_cppbool(dto.AllowVersionDowngrade),
        to_cppstring_opt(dto.ExplicitChannel),
    };
}
// !! AUTO-GENERATED-END CPP_TYPES

static inline char** to_cstring_array(const std::vector<std::string>& vec) {
    char** result = new char*[vec.size()];
    for (size_t i = 0; i < vec.size(); ++i) {
        result[i] = new char[vec[i].size() + 1]; // +1 for null-terminator
#ifdef _WIN32
        strcpy_s(result[i], vec[i].size() + 1, vec[i].c_str());  // Copy string content
#else
        strcpy(result[i], vec[i].c_str());  // Copy string content
#endif
    }
    return result;
}

static inline void free_cstring_array(char** arr, size_t size) {
    for (size_t i = 0; i < size; ++i) {
        delete[] arr[i];
    }
    delete[] arr;
}


class VelopackApp {
private:
    VelopackApp() {};
public:
    static VelopackApp Build() { 
        return VelopackApp(); 
    };
    VelopackApp& SetAutoApplyOnStartup(bool bAutoApply) {
        vpkc_app_set_auto_apply_on_startup(bAutoApply);
        return *this;
    };
    VelopackApp& SetArgs(const std::vector<std::string>& args) {
        char** pArgs = to_cstring_array(args);
        vpkc_app_set_args(pArgs, args.size());
        free_cstring_array(pArgs, args.size());
        return *this;
    };
    VelopackApp& SetLocator(const VelopackLocatorConfig& locator) {
        vpkc_locator_config_t vpkc_locator = to_c(locator);
        vpkc_app_set_locator(&vpkc_locator);
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
    vpkc_update_manager_t* m_pManager = 0;
public:
    UpdateManager(const std::string& urlOrPath, const UpdateOptions* options = nullptr, const VelopackLocatorConfig* locator = nullptr) {
        vpkc_update_options_t* pOptions = nullptr;
        if (options != nullptr) {
            vpkc_update_options_t vpkc_options = to_c(*options);
            pOptions = &vpkc_options;
        }
        
        vpkc_locator_config_t* pLocator = nullptr;
        if (locator != nullptr) {
            vpkc_locator_config_t vpkc_locator = to_c(*locator);
            pLocator = &vpkc_locator;
        }
        
        if (!vpkc_new_update_manager(urlOrPath.c_str(), pOptions, pLocator, &m_pManager)) {
            throw_last_error();
        }
    };
    ~UpdateManager() {
        vpkc_free_update_manager(m_pManager);
    };
    bool IsPortable() noexcept {
        return vpkc_is_portable(m_pManager);
    };
    std::string GetCurrentVersion() noexcept {
        size_t neededSize = vpkc_get_current_version(m_pManager, nullptr, 0);
        std::string strVersion(neededSize, '\0');
        vpkc_get_current_version(m_pManager, &strVersion[0], neededSize);
        return strVersion;
    };
    std::string GetAppId() noexcept {
        size_t neededSize = vpkc_get_app_id(m_pManager, nullptr, 0);
        std::string strId(neededSize, '\0');
        vpkc_get_app_id(m_pManager, &strId[0], neededSize);
        return strId;
    };
    std::optional<VelopackAsset> UpdatePendingRestart() noexcept {
        vpkc_asset_t asset;
        if (vpkc_update_pending_restart(m_pManager, &asset)) {
            VelopackAsset cpp_asset = to_cpp(asset);
            vpkc_free_asset(&asset);
            return cpp_asset;
        }
        return std::nullopt;
    };
    std::optional<UpdateInfo> CheckForUpdates() {
        vpkc_update_info_t update;
        vpkc_update_check_t result = vpkc_check_for_updates(m_pManager, &update);
        switch (result) {
            case vpkc_update_check_t::UPDATE_ERROR:
                throw_last_error();
                return std::nullopt;
            case vpkc_update_check_t::NO_UPDATE_AVAILABLE:
                return std::nullopt;
            case vpkc_update_check_t::UPDATE_AVAILABLE:
                UpdateInfo cpp_info = to_cpp(update);
                vpkc_free_update_info(&update);
                return cpp_info;
        }
        return std::nullopt;
    };
    void DownloadUpdates(const UpdateInfo& update, vpkc_progress_callback_t progress = nullptr) {
        vpkc_update_info_t vpkc_update = to_c(update);
        if (!vpkc_download_updates(m_pManager, &vpkc_update, progress)) {
            throw_last_error();
        }
    };
    void WaitExitThenApplyUpdate(const VelopackAsset& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        char** pRestartArgs = to_cstring_array(restartArgs);
        vpkc_asset_t vpkc_asset = to_c(asset);
        bool result = vpkc_wait_exit_then_apply_update(m_pManager, &vpkc_asset, silent, restart, pRestartArgs, restartArgs.size());
        free_cstring_array(pRestartArgs, restartArgs.size());
        
        if (!result) {
            throw_last_error();
        }
    };
    void WaitExitThenApplyUpdate(const UpdateInfo& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        this->WaitExitThenApplyUpdate(asset.TargetFullRelease, silent, restart, restartArgs);
    };
};

} // namespace Velopack

#endif // __cplusplus

#endif // VELOPACK_H