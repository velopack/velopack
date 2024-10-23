//! This header provides the C and C++ API for the Velopack library.
//! All the C constructs are prefixed by `vpkc_` and all the C++ constructs are in the `Velopack` namespace.
//! The C++ API is a thin wrapper around the C API, providing a more idiomatic C++ interface.
//! You should not mix and match the C and C++ APIs in the same program.
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

typedef enum vpkc_update_check_t {
    UPDATE_AVAILABLE = 0,
    NO_UPDATE_AVAILABLE = 1,
    UPDATE_ERROR = 2,
} vpkc_update_check_t;

// !! AUTO-GENERATED-START C_TYPES

/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
typedef struct vpkc_locator_config_t {
    /// The root directory of the current app.
    char* RootAppDir;
    /// The path to the Update.exe binary.
    char* UpdateExePath;
    /// The path to the packages' directory.
    char* PackagesDir;
    /// The current app manifest.
    char* ManifestPath;
    /// The directory containing the application's user binaries.
    char* CurrentBinaryDir;
    /// Whether the current application is portable or installed.
    bool IsPortable;
} vpkc_locator_config_t;

/// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
typedef struct vpkc_asset_t {
    /// The name or Id of the package containing this release.
    char* PackageId;
    /// The version of this release.
    char* Version;
    /// The type of asset (eg. "Full" or "Delta").
    char* Type;
    /// The filename of the update package containing this release.
    char* FileName;
    /// The SHA1 checksum of the update package containing this release.
    char* SHA1;
    /// The SHA256 checksum of the update package containing this release.
    char* SHA256;
    /// The size in bytes of the update package containing this release.
    uint64_t Size;
    /// The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
    char* NotesMarkdown;
    /// The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
    char* NotesHtml;
} vpkc_asset_t;

/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
typedef struct vpkc_update_info_t {
    /// The available version that we are updating to.
    vpkc_asset_t TargetFullRelease;
    /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
    /// In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
    /// deleted.
    bool IsDowngrade;
} vpkc_update_info_t;

/// Options to customise the behaviour of UpdateManager.
typedef struct vpkc_update_options_t {
    /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
    /// This could happen if a release has bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest version on that
    /// channel is lower than the current version.
    bool AllowVersionDowngrade;
    /// **This option should usually be left None**. <br/>
    /// Overrides the default channel used to fetch updates.
    /// The default channel will be whatever channel was specified on the command line when building this release.
    /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
    /// This allows users to automatically receive updates from the same channel they installed from. This options
    /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
    /// without having to reinstall the application.
    char* ExplicitChannel;
} vpkc_update_options_t;
// !! AUTO-GENERATED-END C_TYPES

/// Creates a new vpkc_update_manager_t. Free with vpkc_free_update_manager.
/// \group UpdateManager
VPKC_EXPORT bool VPKC_CALL vpkc_new_update_manager(const char* pszUrlOrString, vpkc_update_options_t* pOptions, vpkc_locator_config_t* pLocator, vpkc_update_manager_t** pManager);
/// \group UpdateManager
VPKC_EXPORT size_t VPKC_CALL vpkc_get_current_version(vpkc_update_manager_t* pManager, char* pszVersion, size_t cVersion);
/// \group UpdateManager
VPKC_EXPORT size_t VPKC_CALL vpkc_get_app_id(vpkc_update_manager_t* pManager, char* pszId, size_t cId);
/// \group UpdateManager
VPKC_EXPORT bool VPKC_CALL vpkc_is_portable(vpkc_update_manager_t* pManager);
/// \group UpdateManager
VPKC_EXPORT bool VPKC_CALL vpkc_update_pending_restart(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset);
/// \group UpdateManager
VPKC_EXPORT vpkc_update_check_t VPKC_CALL vpkc_check_for_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate);
/// \group UpdateManager
VPKC_EXPORT bool VPKC_CALL vpkc_download_updates(vpkc_update_manager_t* pManager, vpkc_update_info_t* pUpdate, vpkc_progress_callback_t cbProgress);
/// \group UpdateManager
VPKC_EXPORT bool VPKC_CALL vpkc_wait_exit_then_apply_update(vpkc_update_manager_t* pManager, vpkc_asset_t* pAsset, bool bSilent, bool bRestart, char** pRestartArgs, size_t cRestartArgs);
/// \group UpdateManager
VPKC_EXPORT void VPKC_CALL vpkc_free_update_manager(vpkc_update_manager_t* pManager);
/// \group UpdateManager
VPKC_EXPORT void VPKC_CALL vpkc_free_update_info(vpkc_update_info_t* pUpdateInfo);
/// \group UpdateManager
VPKC_EXPORT void VPKC_CALL vpkc_free_asset(vpkc_asset_t* pAsset);

/// Should be run at the beginning of your application to handle Velopack events.
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_run();
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_auto_apply_on_startup(bool bAutoApply);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_args(char** pArgs, size_t cArgs);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_locator(vpkc_locator_config_t* pLocator);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_install(vpkc_hook_callback_t cbAfterInstall);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_uninstall(vpkc_hook_callback_t cbBeforeUninstall);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_before_update(vpkc_hook_callback_t cbBeforeUpdate);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_after_update(vpkc_hook_callback_t cbAfterUpdate);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_first_run(vpkc_hook_callback_t cbFirstRun);
/// \group VelopackApp
VPKC_EXPORT void VPKC_CALL vpkc_app_set_hook_restarted(vpkc_hook_callback_t cbRestarted);

/// Given a function has returned a failure, this function will return the last error message as a string.
VPKC_EXPORT size_t VPKC_CALL vpkc_get_last_error(char* pszError, size_t cError);

/// Sets the callback to be used/called with log messages from Velopack.
VPKC_EXPORT void VPKC_CALL vpkc_set_log(vpkc_log_callback_t cbLog);

#ifdef __cplusplus // end of extern "C"
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

/// VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
struct VelopackLocatorConfig {
    /// The root directory of the current app.
    std::string RootAppDir;
    /// The path to the Update.exe binary.
    std::string UpdateExePath;
    /// The path to the packages' directory.
    std::string PackagesDir;
    /// The current app manifest.
    std::string ManifestPath;
    /// The directory containing the application's user binaries.
    std::string CurrentBinaryDir;
    /// Whether the current application is portable or installed.
    bool IsPortable;
};

/// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
struct VelopackAsset {
    /// The name or Id of the package containing this release.
    std::string PackageId;
    /// The version of this release.
    std::string Version;
    /// The type of asset (eg. "Full" or "Delta").
    std::string Type;
    /// The filename of the update package containing this release.
    std::string FileName;
    /// The SHA1 checksum of the update package containing this release.
    std::string SHA1;
    /// The SHA256 checksum of the update package containing this release.
    std::string SHA256;
    /// The size in bytes of the update package containing this release.
    uint64_t Size;
    /// The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
    std::string NotesMarkdown;
    /// The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
    std::string NotesHtml;
};

/// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
struct UpdateInfo {
    /// The available version that we are updating to.
    VelopackAsset TargetFullRelease;
    /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
    /// In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
    /// deleted.
    bool IsDowngrade;
};

/// Options to customise the behaviour of UpdateManager.
struct UpdateOptions {
    /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
    /// This could happen if a release has bugs and was retracted from the release feed, or if you're using
    /// ExplicitChannel to switch channels to another channel where the latest version on that
    /// channel is lower than the current version.
    bool AllowVersionDowngrade;
    /// **This option should usually be left None**. <br/>
    /// Overrides the default channel used to fetch updates.
    /// The default channel will be whatever channel was specified on the command line when building this release.
    /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
    /// This allows users to automatically receive updates from the same channel they installed from. This options
    /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
    /// without having to reinstall the application.
    std::optional<std::string> ExplicitChannel;
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

/** 
 * VelopackApp helps you to handle app activation events correctly.
 * This should be used as early as possible in your application startup code.
 * (eg. the beginning of main() or wherever your entry point is)
 */
class VelopackApp {
private:
    VelopackApp() {};
public:
    /**
     * Build a new VelopackApp instance.
     */
    static VelopackApp Build() { 
        return VelopackApp(); 
    };

    /**
     * Set whether to automatically apply downloaded updates on startup. This is ON by default.
     */
    VelopackApp& SetAutoApplyOnStartup(bool bAutoApply) {
        vpkc_app_set_auto_apply_on_startup(bAutoApply);
        return *this;
    };

    /**
     * Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
     */
    VelopackApp& SetArgs(const std::vector<std::string>& args) {
        char** pArgs = to_cstring_array(args);
        vpkc_app_set_args(pArgs, args.size());
        free_cstring_array(pArgs, args.size());
        return *this;
    };

    /**
     * VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
     */
    VelopackApp& SetLocator(const VelopackLocatorConfig& locator) {
        vpkc_locator_config_t vpkc_locator = to_c(locator);
        vpkc_app_set_locator(&vpkc_locator);
        return *this;
    };

    /**
     * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     */
    VelopackApp& OnAfterInstall(vpkc_hook_callback_t cbInstall) {
        vpkc_app_set_hook_after_install(cbInstall);
        return *this;
    };

    /**
     * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     */
    VelopackApp& OnBeforeUninstall(vpkc_hook_callback_t cbInstall) {
        vpkc_app_set_hook_before_uninstall(cbInstall);
        return *this;
    };

    /**
     * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     */
    VelopackApp& OnBeforeUpdate(vpkc_hook_callback_t cbInstall) {
        vpkc_app_set_hook_before_update(cbInstall);
        return *this;
    };

    /**
     * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     */
    VelopackApp& OnAfterUpdate(vpkc_hook_callback_t cbInstall) {
        vpkc_app_set_hook_after_update(cbInstall);
        return *this;
    };

    /**
     * This hook is triggered when the application is started for the first time after installation.
     */
    VelopackApp& OnFirstRun(vpkc_hook_callback_t cbInstall) {
        vpkc_app_set_hook_first_run(cbInstall);
        return *this;
    };

    /**
     * This hook is triggered when the application is restarted by Velopack after installing updates.
     */
    VelopackApp& OnRestarted(vpkc_hook_callback_t cbInstall) {
        vpkc_app_set_hook_restarted(cbInstall);
        return *this;
    };

    /**
     * Runs the Velopack startup logic. This should be the first thing to run in your app.
     * In some circumstances it may terminate/restart the process to perform tasks.
     */
    void Run() {
        vpkc_app_run();
    };
};

/**
 * Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
 */
class UpdateManager {
private:
    vpkc_update_manager_t* m_pManager = 0;
public:
    /**
     * Create a new UpdateManager instance.
     * @param urlOrPath Location of the update server or path to the local update directory.
     * @param options Optional extra configuration for update manager.
     * @param locator Override the default locator configuration (usually used for testing / mocks).
     */
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

    /**
     * Destructor for UpdateManager.
     */
    ~UpdateManager() {
        vpkc_free_update_manager(m_pManager);
    };

    /**
     * Returns whether the app is in portable mode. On Windows this can be true or false.
     * On MacOS and Linux this will always be true.
     */
    bool IsPortable() noexcept {
        return vpkc_is_portable(m_pManager);
    };

    /**
     * Returns the currently installed version of the app.
     */
    std::string GetCurrentVersion() noexcept {
        size_t neededSize = vpkc_get_current_version(m_pManager, nullptr, 0);
        std::string strVersion(neededSize, '\0');
        vpkc_get_current_version(m_pManager, &strVersion[0], neededSize);
        return strVersion;
    };

    /**
     * Returns the currently installed app id.
     */
    std::string GetAppId() noexcept {
        size_t neededSize = vpkc_get_app_id(m_pManager, nullptr, 0);
        std::string strId(neededSize, '\0');
        vpkc_get_app_id(m_pManager, &strId[0], neededSize);
        return strId;
    };

    /**
     * Returns an UpdateInfo object if there is an update downloaded which still needs to be applied.
     * You can pass the UpdateInfo object to waitExitThenApplyUpdate to apply the update.
     */
    std::optional<VelopackAsset> UpdatePendingRestart() noexcept {
        vpkc_asset_t asset;
        if (vpkc_update_pending_restart(m_pManager, &asset)) {
            VelopackAsset cpp_asset = to_cpp(asset);
            vpkc_free_asset(&asset);
            return cpp_asset;
        }
        return std::nullopt;
    };

    /**
     * Checks for updates, returning None if there are none available. If there are updates available, this method will return an
     * UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
     */
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

    /**
     * Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
     * This function will acquire a global update lock so may fail if there is already another update operation in progress.
     * - If the update contains delta packages and the delta feature is enabled
     *   this method will attempt to unpack and prepare them.
     * - If there is no delta update available, or there is an error preparing delta
     *   packages, this method will fall back to downloading the full version of the update.
     */
    void DownloadUpdates(const UpdateInfo& update, vpkc_progress_callback_t progress = nullptr) {
        vpkc_update_info_t vpkc_update = to_c(update);
        if (!vpkc_download_updates(m_pManager, &vpkc_update, progress)) {
            throw_last_error();
        }
    };

    /**
     * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
     * You should then clean up any state and exit your app. The updater will apply updates and then
     * optionally restart your app. The updater will only wait for 60 seconds before giving up.
     */
    void WaitExitThenApplyUpdate(const VelopackAsset& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        char** pRestartArgs = to_cstring_array(restartArgs);
        vpkc_asset_t vpkc_asset = to_c(asset);
        bool result = vpkc_wait_exit_then_apply_update(m_pManager, &vpkc_asset, silent, restart, pRestartArgs, restartArgs.size());
        free_cstring_array(pRestartArgs, restartArgs.size());
        
        if (!result) {
            throw_last_error();
        }
    };

    /**
     * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
     * You should then clean up any state and exit your app. The updater will apply updates and then
     * optionally restart your app. The updater will only wait for 60 seconds before giving up.
     */
    void WaitExitThenApplyUpdate(const UpdateInfo& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        this->WaitExitThenApplyUpdate(asset.TargetFullRelease, silent, restart, restartArgs);
    };
};

} // namespace Velopack

#endif // __cplusplus

#endif // VELOPACK_H