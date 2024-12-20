//! This header provides the C++ API for the Velopack library.
//! This C++ API is a thin wrapper around the C API, providing a more idiomatic C++ interface.
//! You should not mix and match the C and C++ APIs in the same program.
#ifndef VELOPACK_HPP
#define VELOPACK_HPP

#include <string>
#include <optional>
#include <vector>
#include <stdexcept>
#include <memory>
#include <functional>

#include "Velopack.h"

#if !defined(_WIN32)
#include <string.h>
#endif

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

static inline char* allocate_cstring(const std::string& str) {
    char* result = new char[str.size() + 1]; // +1 for null-terminator
#ifdef _WIN32
    strcpy_s(result, str.size() + 1, str.c_str());  // Copy string content
#else
    strcpy(result, str.c_str());  // Copy string content
#endif
    result[str.size()] = '\0'; // Null-terminate the string
    return result;
}

static inline void free_cstring(char* str) {
    delete[] str;
}

static inline char** allocate_cstring_array(const std::vector<std::string>& vec) {
    char** result = new char*[vec.size()];
    for (size_t i = 0; i < vec.size(); ++i) {
        result[i] = allocate_cstring(vec[i]);
    }
    return result;
}

static inline void free_cstring_array(char** arr, size_t size) {
    for (size_t i = 0; i < size; ++i) {
        free_cstring(arr[i]);
        arr[i] = nullptr;
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
     * Attach a custom callback to receive log messages from Velopack.
     */
    VelopackApp& SetLogger(vpkc_log_callback_t cbInstall, void* p_user_data) {
        vpkc_set_logger(cbInstall, p_user_data);
        return *this;
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
        char** pArgs = allocate_cstring_array(args);
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
    void Run(void* pUserData = 0) {
        vpkc_app_run(pUserData);
    };
};

/**
 * Progress callback function. Call with values between 0 and 100 inclusive.
 */
typedef std::function<void(size_t)> vpkc_progress_send_t;

/**
 * Abstract class for retrieving release feeds and downloading assets. You should subclass this and 
 * implement/override the GetReleaseFeed and DownloadReleaseEntry methods.
 * This class is used by the UpdateManager to fetch release feeds and download assets in a custom way.
 */
class IUpdateSource {
    friend class UpdateManager;
    friend class FileSource;
    friend class HttpSource;
private: 
    IUpdateSource(vpkc_update_source_t* pSource) : m_pSource(pSource) {}
    vpkc_update_source_t* m_pSource = 0;
public:
    ~IUpdateSource() {
        vpkc_free_source(m_pSource);
    }
    IUpdateSource() {
        m_pSource = vpkc_new_source_custom_callback(
            [](void* userData, const char* releasesName) {
                IUpdateSource* source = reinterpret_cast<IUpdateSource*>(userData);
                std::string json = source->GetReleaseFeed(releasesName);
                return allocate_cstring(json);
            }, 
            [](void* userData, char* pszFeed) {
                free_cstring(pszFeed);
            },
            [](void* userData, const struct vpkc_asset_t *pAsset, const char* pszLocalPath, size_t progressCallbackId) {
                IUpdateSource* source = reinterpret_cast<IUpdateSource*>(userData);
                VelopackAsset asset = to_cpp(*pAsset);
                std::string localPath = to_cppstring(pszLocalPath);
                std::function<void(size_t)> progress_callback = [progressCallbackId](size_t progress) {
                    vpkc_source_report_progress(progressCallbackId, progress);
                };
                return source->DownloadReleaseEntry(asset, localPath, progress_callback);
            },
            this);
    }
    virtual const std::string GetReleaseFeed(const std::string releasesName) = 0;
    virtual bool DownloadReleaseEntry(const VelopackAsset& asset, const std::string localFilePath, vpkc_progress_send_t progress) = 0;
};

/**
 * A simple update source that reads release feeds and downloads assets from a local file path.
 */
class FileSource : public IUpdateSource {
public:
    FileSource(const std::string& filePath) : IUpdateSource(vpkc_new_source_file(filePath.c_str())) { }
};

/**
 * A simple update source that reads release feeds and downloads assets from an remote http url.
 */
class HttpSource : public IUpdateSource {
public:
    HttpSource(const std::string& httpUrl) : IUpdateSource(vpkc_new_source_http_url(httpUrl.c_str())) { }
};

/**
 * Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
 */
class UpdateManager {
private:
    vpkc_update_manager_t* m_pManager = 0;
    std::unique_ptr<IUpdateSource> m_pUpdateSource;

public:
    /**
     * Create a new UpdateManager instance.
     * @param urlOrPath Location of the http update server or the local update directory path containing releases.
     * @param options Optional extra configuration for update manager.
     * @param locator Override the default locator configuration (usually used for testing / mocks).
     */
    UpdateManager(const std::string& urlOrPath, const UpdateOptions* options = nullptr, const VelopackLocatorConfig* locator = nullptr) {
        vpkc_update_options_t vpkc_options;
        vpkc_update_options_t* pOptions = nullptr;
        if (options != nullptr) {
            vpkc_options = to_c(*options);
            pOptions = &vpkc_options;
        }
        
        vpkc_locator_config_t vpkc_locator;
        vpkc_locator_config_t* pLocator = nullptr;
        if (locator != nullptr) {
            vpkc_locator = to_c(*locator);
            pLocator = &vpkc_locator;
        }
        
        if (!vpkc_new_update_manager(urlOrPath.c_str(), pOptions, pLocator, &m_pManager)) {
            throw_last_error();
        }
    };

    /**
     * Create a new UpdateManager instance.
     * @param updateSource The source to use for retrieving feed and downloading assets.
     * @param options Optional extra configuration for update manager.
     * @param locator Override the default locator configuration (usually used for testing / mocks).
     */
    UpdateManager(std::unique_ptr<IUpdateSource> pUpdateSource, const UpdateOptions* options = nullptr, const VelopackLocatorConfig* locator = nullptr) {
        vpkc_update_options_t vpkc_options;
        vpkc_update_options_t* pOptions = nullptr;
        if (options != nullptr) {
            vpkc_options = to_c(*options);
            pOptions = &vpkc_options;
        }
        
        vpkc_locator_config_t vpkc_locator;
        vpkc_locator_config_t* pLocator = nullptr;
        if (locator != nullptr) {
            vpkc_locator = to_c(*locator);
            pLocator = &vpkc_locator;
        }

        m_pUpdateSource.swap(pUpdateSource);
        vpkc_update_source_t* pSource = m_pUpdateSource->m_pSource;
        if (!vpkc_new_update_manager_with_source(pSource, pOptions, pLocator, &m_pManager)) {
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
            case vpkc_update_check_t::REMOTE_IS_EMPTY:
                return std::nullopt;
            case vpkc_update_check_t::UPDATE_AVAILABLE:
                UpdateInfo cpp_info = to_cpp(update);
                vpkc_free_update_info(&update);
                return cpp_info;
        }
        return std::nullopt;
    };

    /**
     * Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional callback.
     * This function will acquire a global update lock so may fail if there is already another update operation in progress.
     * - If the update contains delta packages and the delta feature is enabled
     *   this method will attempt to unpack and prepare them.
     * - If there is no delta update available, or there is an error preparing delta
     *   packages, this method will fall back to downloading the full version of the update.
     */
    void DownloadUpdates(const UpdateInfo& update, vpkc_progress_callback_t progress = nullptr, void* pUserData = 0) {
        vpkc_update_info_t vpkc_update = to_c(update);
        if (!vpkc_download_updates(m_pManager, &vpkc_update, progress, pUserData)) {
            throw_last_error();
        }
    };

    /**
     * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
     * You should then clean up any state and exit your app. The updater will apply updates and then
     * optionally restart your app. The updater will only wait for 60 seconds before giving up.
     */
    void WaitExitThenApplyUpdate(const VelopackAsset& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        char** pRestartArgs = allocate_cstring_array(restartArgs);
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

#endif // VELOPACK_HPP
