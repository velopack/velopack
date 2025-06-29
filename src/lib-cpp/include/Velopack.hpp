//! This header provides the C++ API for the Velopack library.
//! This C++ API is a thin wrapper around the C API, providing a more idiomatic C++ interface.
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

static inline void throw_last_error()
{
    size_t neededSize = vpkc_get_last_error(nullptr, 0);
    std::string strError(neededSize, '\0');
    vpkc_get_last_error(&strError[0], neededSize);
    throw std::runtime_error(strError);
}

static inline std::optional<std::string> to_cpp_string(const char* psz)
{
    return psz == nullptr ? std::optional<std::string>("") : std::optional<std::string>(psz);
}

static inline char* alloc_c_string(const std::string& str)
{
    char* result = new char[str.size() + 1]; // +1 for null-terminator
#ifdef _WIN32
    strcpy_s(result, str.size() + 1, str.c_str());  // Copy string content
#else
    strcpy(result, str.c_str());  // Copy string content
#endif
    result[str.size()] = '\0'; // Null-terminate the string
    return result;
}

static inline char* alloc_c_string(const std::optional<std::string>& str)
{
    if (!str.has_value()) { return nullptr; }
    return alloc_c_string(str.value());
}

static inline void free_c_string(char* str)
{
    delete[] str;
}

static inline char** alloc_c_string_vec(const std::vector<std::string>& dto, size_t* count)
{
    if (dto.empty()) {
        *count = 0;
        return nullptr;
    }
    *count = dto.size();
    char** arr = new char* [*count];
    for (size_t i = 0; i < *count; ++i) {
        arr[i] = alloc_c_string(dto[i]);
    }
    return arr;
}

static inline void free_c_string_vec(char** arr, size_t size)
{
    for (size_t i = 0; i < size; ++i) {
        free_c_string(arr[i]);
        arr[i] = nullptr;
    }
    delete[] arr;
}

template<typename T>
inline T unwrap(const std::optional<T>& opt, const std::string& message = "Expected value not present") {
    if (!opt.has_value()) {
        throw std::runtime_error(message);
    }
    return opt.value();
}

// !! AUTO-GENERATED-START CPP_TYPES

/** VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth). */
struct VelopackLocatorConfig {
    /** The root directory of the current app. */
    std::string RootAppDir;
    /** The path to the Update.exe binary. */
    std::string UpdateExePath;
    /** The path to the packages' directory. */
    std::string PackagesDir;
    /** The current app manifest. */
    std::string ManifestPath;
    /** The directory containing the application's user binaries. */
    std::string CurrentBinaryDir;
    /** Whether the current application is portable or installed. */
    bool IsPortable;
};

static inline std::optional<VelopackLocatorConfig> to_cpp_VelopackLocatorConfig(const vpkc_locator_config_t* dto) {
    if (dto == nullptr) { return std::nullopt; }
    return std::optional<VelopackLocatorConfig>({
        unwrap(to_cpp_string(dto->RootAppDir), "Required property RootAppDir was null"),
        unwrap(to_cpp_string(dto->UpdateExePath), "Required property UpdateExePath was null"),
        unwrap(to_cpp_string(dto->PackagesDir), "Required property PackagesDir was null"),
        unwrap(to_cpp_string(dto->ManifestPath), "Required property ManifestPath was null"),
        unwrap(to_cpp_string(dto->CurrentBinaryDir), "Required property CurrentBinaryDir was null"),
        dto->IsPortable,
    });
}

static inline std::vector<VelopackLocatorConfig> to_cpp_VelopackLocatorConfig_vec(const vpkc_locator_config_t* const* arr, size_t c) {
    if (arr == nullptr || c < 1) { return std::vector<VelopackLocatorConfig>(); }
    std::vector<VelopackLocatorConfig> result;
    result.reserve(c);
    for (size_t i = 0; i < c; ++i) {
        auto dto = arr[i];
        if (dto == nullptr) { continue; }
        result.push_back(unwrap(to_cpp_VelopackLocatorConfig(dto)));
    }
    return result;
}

static inline vpkc_locator_config_t* alloc_c_VelopackLocatorConfig_ptr(const VelopackLocatorConfig* dto) {
    if (dto == nullptr) { return nullptr; }
    vpkc_locator_config_t* obj = new vpkc_locator_config_t{};
    obj->RootAppDir = alloc_c_string(dto->RootAppDir);
    obj->UpdateExePath = alloc_c_string(dto->UpdateExePath);
    obj->PackagesDir = alloc_c_string(dto->PackagesDir);
    obj->ManifestPath = alloc_c_string(dto->ManifestPath);
    obj->CurrentBinaryDir = alloc_c_string(dto->CurrentBinaryDir);
    obj->IsPortable = dto->IsPortable;
    return obj;
}

static inline vpkc_locator_config_t* alloc_c_VelopackLocatorConfig(const std::optional<VelopackLocatorConfig>& dto) {
    if (!dto.has_value()) { return nullptr; }
    VelopackLocatorConfig obj = unwrap(dto);
    return alloc_c_VelopackLocatorConfig_ptr(&obj);
}

static inline vpkc_locator_config_t** alloc_c_VelopackLocatorConfig_vec(const std::vector<VelopackLocatorConfig>& dto, size_t* count) {
    if (dto.empty()) {
        *count = 0;
        return nullptr;
    }
    *count = dto.size();
    vpkc_locator_config_t** arr = new vpkc_locator_config_t*[*count];
    for (size_t i = 0; i < *count; ++i) {
        arr[i] = alloc_c_VelopackLocatorConfig(dto[i]);
    }
    return arr;
}

static inline void free_c_VelopackLocatorConfig(vpkc_locator_config_t* obj) {
    if (obj == nullptr) { return; }
    free_c_string(obj->RootAppDir);
    free_c_string(obj->UpdateExePath);
    free_c_string(obj->PackagesDir);
    free_c_string(obj->ManifestPath);
    free_c_string(obj->CurrentBinaryDir);

    delete obj;
}

static inline void free_c_VelopackLocatorConfig_vec(vpkc_locator_config_t** arr, size_t count) {
    if (arr == nullptr || count < 1) { return; }
    for (size_t i = 0; i < count; ++i) {
        free_c_VelopackLocatorConfig(arr[i]);
    }
    delete[] arr;
}

/** An individual Velopack asset, could refer to an asset on-disk or in a remote package feed. */
struct VelopackAsset {
    /** The name or Id of the package containing this release. */
    std::string PackageId;
    /** The version of this release. */
    std::string Version;
    /** The type of asset (eg. "Full" or "Delta"). */
    std::string Type;
    /** The filename of the update package containing this release. */
    std::string FileName;
    /** The SHA1 checksum of the update package containing this release. */
    std::string SHA1;
    /** The SHA256 checksum of the update package containing this release. */
    std::string SHA256;
    /** The size in bytes of the update package containing this release. */
    uint64_t Size;
    /** The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string. */
    std::string NotesMarkdown;
    /** The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string. */
    std::string NotesHtml;
};

static inline std::optional<VelopackAsset> to_cpp_VelopackAsset(const vpkc_asset_t* dto) {
    if (dto == nullptr) { return std::nullopt; }
    return std::optional<VelopackAsset>({
        unwrap(to_cpp_string(dto->PackageId), "Required property PackageId was null"),
        unwrap(to_cpp_string(dto->Version), "Required property Version was null"),
        unwrap(to_cpp_string(dto->Type), "Required property Type was null"),
        unwrap(to_cpp_string(dto->FileName), "Required property FileName was null"),
        unwrap(to_cpp_string(dto->SHA1), "Required property SHA1 was null"),
        unwrap(to_cpp_string(dto->SHA256), "Required property SHA256 was null"),
        dto->Size,
        unwrap(to_cpp_string(dto->NotesMarkdown), "Required property NotesMarkdown was null"),
        unwrap(to_cpp_string(dto->NotesHtml), "Required property NotesHtml was null"),
    });
}

static inline std::vector<VelopackAsset> to_cpp_VelopackAsset_vec(const vpkc_asset_t* const* arr, size_t c) {
    if (arr == nullptr || c < 1) { return std::vector<VelopackAsset>(); }
    std::vector<VelopackAsset> result;
    result.reserve(c);
    for (size_t i = 0; i < c; ++i) {
        auto dto = arr[i];
        if (dto == nullptr) { continue; }
        result.push_back(unwrap(to_cpp_VelopackAsset(dto)));
    }
    return result;
}

static inline vpkc_asset_t* alloc_c_VelopackAsset_ptr(const VelopackAsset* dto) {
    if (dto == nullptr) { return nullptr; }
    vpkc_asset_t* obj = new vpkc_asset_t{};
    obj->PackageId = alloc_c_string(dto->PackageId);
    obj->Version = alloc_c_string(dto->Version);
    obj->Type = alloc_c_string(dto->Type);
    obj->FileName = alloc_c_string(dto->FileName);
    obj->SHA1 = alloc_c_string(dto->SHA1);
    obj->SHA256 = alloc_c_string(dto->SHA256);
    obj->Size = dto->Size;
    obj->NotesMarkdown = alloc_c_string(dto->NotesMarkdown);
    obj->NotesHtml = alloc_c_string(dto->NotesHtml);
    return obj;
}

static inline vpkc_asset_t* alloc_c_VelopackAsset(const std::optional<VelopackAsset>& dto) {
    if (!dto.has_value()) { return nullptr; }
    VelopackAsset obj = unwrap(dto);
    return alloc_c_VelopackAsset_ptr(&obj);
}

static inline vpkc_asset_t** alloc_c_VelopackAsset_vec(const std::vector<VelopackAsset>& dto, size_t* count) {
    if (dto.empty()) {
        *count = 0;
        return nullptr;
    }
    *count = dto.size();
    vpkc_asset_t** arr = new vpkc_asset_t*[*count];
    for (size_t i = 0; i < *count; ++i) {
        arr[i] = alloc_c_VelopackAsset(dto[i]);
    }
    return arr;
}

static inline void free_c_VelopackAsset(vpkc_asset_t* obj) {
    if (obj == nullptr) { return; }
    free_c_string(obj->PackageId);
    free_c_string(obj->Version);
    free_c_string(obj->Type);
    free_c_string(obj->FileName);
    free_c_string(obj->SHA1);
    free_c_string(obj->SHA256);

    free_c_string(obj->NotesMarkdown);
    free_c_string(obj->NotesHtml);
    delete obj;
}

static inline void free_c_VelopackAsset_vec(vpkc_asset_t** arr, size_t count) {
    if (arr == nullptr || count < 1) { return; }
    for (size_t i = 0; i < count; ++i) {
        free_c_VelopackAsset(arr[i]);
    }
    delete[] arr;
}

/** Holds information about the current version and pending updates, such as how many there are, and access to release notes. */
struct UpdateInfo {
    /** The available version that we are updating to. */
    VelopackAsset TargetFullRelease;
    /** The base release that this update is based on. This is only available if the update is a delta update. */
    std::optional<VelopackAsset> BaseRelease;
    /** The list of delta updates that can be applied to the base version to get to the target version. */
    std::vector<VelopackAsset> DeltasToTarget;
    /**
     * True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
     * In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
     * deleted.
     */
    bool IsDowngrade;
};

static inline std::optional<UpdateInfo> to_cpp_UpdateInfo(const vpkc_update_info_t* dto) {
    if (dto == nullptr) { return std::nullopt; }
    return std::optional<UpdateInfo>({
        unwrap(to_cpp_VelopackAsset(dto->TargetFullRelease), "Required property TargetFullRelease was null"),
        to_cpp_VelopackAsset(dto->BaseRelease),
        to_cpp_VelopackAsset_vec(dto->DeltasToTarget, dto->DeltasToTargetCount),
        dto->IsDowngrade,
    });
}

static inline std::vector<UpdateInfo> to_cpp_UpdateInfo_vec(const vpkc_update_info_t* const* arr, size_t c) {
    if (arr == nullptr || c < 1) { return std::vector<UpdateInfo>(); }
    std::vector<UpdateInfo> result;
    result.reserve(c);
    for (size_t i = 0; i < c; ++i) {
        auto dto = arr[i];
        if (dto == nullptr) { continue; }
        result.push_back(unwrap(to_cpp_UpdateInfo(dto)));
    }
    return result;
}

static inline vpkc_update_info_t* alloc_c_UpdateInfo_ptr(const UpdateInfo* dto) {
    if (dto == nullptr) { return nullptr; }
    vpkc_update_info_t* obj = new vpkc_update_info_t{};
    obj->TargetFullRelease = alloc_c_VelopackAsset(dto->TargetFullRelease);
    obj->BaseRelease = alloc_c_VelopackAsset(dto->BaseRelease);
    obj->DeltasToTarget = alloc_c_VelopackAsset_vec(dto->DeltasToTarget, &obj->DeltasToTargetCount);
    obj->IsDowngrade = dto->IsDowngrade;
    return obj;
}

static inline vpkc_update_info_t* alloc_c_UpdateInfo(const std::optional<UpdateInfo>& dto) {
    if (!dto.has_value()) { return nullptr; }
    UpdateInfo obj = unwrap(dto);
    return alloc_c_UpdateInfo_ptr(&obj);
}

static inline vpkc_update_info_t** alloc_c_UpdateInfo_vec(const std::vector<UpdateInfo>& dto, size_t* count) {
    if (dto.empty()) {
        *count = 0;
        return nullptr;
    }
    *count = dto.size();
    vpkc_update_info_t** arr = new vpkc_update_info_t*[*count];
    for (size_t i = 0; i < *count; ++i) {
        arr[i] = alloc_c_UpdateInfo(dto[i]);
    }
    return arr;
}

static inline void free_c_UpdateInfo(vpkc_update_info_t* obj) {
    if (obj == nullptr) { return; }
    free_c_VelopackAsset(obj->TargetFullRelease);
    free_c_VelopackAsset(obj->BaseRelease);
    free_c_VelopackAsset_vec(obj->DeltasToTarget, obj->DeltasToTargetCount);

    delete obj;
}

static inline void free_c_UpdateInfo_vec(vpkc_update_info_t** arr, size_t count) {
    if (arr == nullptr || count < 1) { return; }
    for (size_t i = 0; i < count; ++i) {
        free_c_UpdateInfo(arr[i]);
    }
    delete[] arr;
}

/** Options to customise the behaviour of UpdateManager. */
struct UpdateOptions {
    /**
     * Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
     * This could happen if a release has bugs and was retracted from the release feed, or if you're using
     * ExplicitChannel to switch channels to another channel where the latest version on that
     * channel is lower than the current version.
     */
    bool AllowVersionDowngrade;
    /**
     * **This option should usually be left None**.
     * Overrides the default channel used to fetch updates.
     * The default channel will be whatever channel was specified on the command line when building this release.
     * For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
     * This allows users to automatically receive updates from the same channel they installed from. This options
     * allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
     * without having to reinstall the application.
     */
    std::optional<std::string> ExplicitChannel;
    /**
     * Sets the maximum number of deltas to consider before falling back to a full update.
     * The default is 10. Set to a negative number (eg. -1) to disable deltas.
     */
    int32_t MaximumDeltasBeforeFallback;
};

static inline std::optional<UpdateOptions> to_cpp_UpdateOptions(const vpkc_update_options_t* dto) {
    if (dto == nullptr) { return std::nullopt; }
    return std::optional<UpdateOptions>({
        dto->AllowVersionDowngrade,
        to_cpp_string(dto->ExplicitChannel),
        dto->MaximumDeltasBeforeFallback,
    });
}

static inline std::vector<UpdateOptions> to_cpp_UpdateOptions_vec(const vpkc_update_options_t* const* arr, size_t c) {
    if (arr == nullptr || c < 1) { return std::vector<UpdateOptions>(); }
    std::vector<UpdateOptions> result;
    result.reserve(c);
    for (size_t i = 0; i < c; ++i) {
        auto dto = arr[i];
        if (dto == nullptr) { continue; }
        result.push_back(unwrap(to_cpp_UpdateOptions(dto)));
    }
    return result;
}

static inline vpkc_update_options_t* alloc_c_UpdateOptions_ptr(const UpdateOptions* dto) {
    if (dto == nullptr) { return nullptr; }
    vpkc_update_options_t* obj = new vpkc_update_options_t{};
    obj->AllowVersionDowngrade = dto->AllowVersionDowngrade;
    obj->ExplicitChannel = alloc_c_string(dto->ExplicitChannel);
    obj->MaximumDeltasBeforeFallback = dto->MaximumDeltasBeforeFallback;
    return obj;
}

static inline vpkc_update_options_t* alloc_c_UpdateOptions(const std::optional<UpdateOptions>& dto) {
    if (!dto.has_value()) { return nullptr; }
    UpdateOptions obj = unwrap(dto);
    return alloc_c_UpdateOptions_ptr(&obj);
}

static inline vpkc_update_options_t** alloc_c_UpdateOptions_vec(const std::vector<UpdateOptions>& dto, size_t* count) {
    if (dto.empty()) {
        *count = 0;
        return nullptr;
    }
    *count = dto.size();
    vpkc_update_options_t** arr = new vpkc_update_options_t*[*count];
    for (size_t i = 0; i < *count; ++i) {
        arr[i] = alloc_c_UpdateOptions(dto[i]);
    }
    return arr;
}

static inline void free_c_UpdateOptions(vpkc_update_options_t* obj) {
    if (obj == nullptr) { return; }

    free_c_string(obj->ExplicitChannel);

    delete obj;
}

static inline void free_c_UpdateOptions_vec(vpkc_update_options_t** arr, size_t count) {
    if (arr == nullptr || count < 1) { return; }
    for (size_t i = 0; i < count; ++i) {
        free_c_UpdateOptions(arr[i]);
    }
    delete[] arr;
}
// !! AUTO-GENERATED-END CPP_TYPES

/**
 * VelopackApp helps you to handle app activation events correctly.
 * This should be used as early as possible in your application startup code.
 * (eg. the beginning of main() or wherever your entry point is)
 * To use this class, you should create a new VelopackApp::Build() builder instance,
 * and then chain calls to the builder to configure your app.
 * Finally, call the Run() method to execute the Velopack logic.
 */
class VelopackApp {
private:
    VelopackApp() {};
public:
    /**
     * Create and return a new VelopackApp builder.
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
     * Override the command line arguments used by VelopackApp.
     * By default, Velopack will use the command line arguments from the current process.
     * @param args The command line arguments to use.
     * @returns A reference to the builder.
     */
    VelopackApp& SetArgs(const std::vector<std::string>& args) {
        size_t c;
        char** pArgs = alloc_c_string_vec(args, &c);
        vpkc_app_set_args(pArgs, c);
        free_c_string_vec(pArgs, c);
        return *this;
    };

    /**
     * Override the default VelopackLocator. The locator is used to find important paths for the application.
     * @param locator The locator to use.
     * @returns A reference to the builder.
     */
    VelopackApp& SetLocator(const VelopackLocatorConfig& locator) {
        vpkc_locator_config_t* vpkc_locator = alloc_c_VelopackLocatorConfig(locator);
        vpkc_app_set_locator(vpkc_locator);
        free_c_VelopackLocatorConfig(vpkc_locator);
        return *this;
    };

    /**
     * This hook is triggered after the app has been installed.
     * WARNING: This hook is run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     * @param cbAfterInstall The callback to run after the app has been installed.
     * @returns A reference to the builder.
     */
    VelopackApp& OnAfterInstall(vpkc_hook_callback_t cbAfterInstall) {
        vpkc_app_set_hook_after_install(cbAfterInstall);
        return *this;
    };

    /**
     * This hook is triggered before the app is uninstalled.
     * WARNING: This hook is run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     * @param cbBeforeUninstall The callback to run before the app is uninstalled.
     * @returns A reference to the builder.
     */
    VelopackApp& OnBeforeUninstall(vpkc_hook_callback_t cbBeforeUninstall) {
        vpkc_app_set_hook_before_uninstall(cbBeforeUninstall);
        return *this;
    };

    /**
     * This hook is triggered before the app is updated.
     * WARNING: This hook is run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     * @param cbBeforeUpdate The callback to run before the app is updated.
     * @returns A reference to the builder.
     */
    VelopackApp& OnBeforeUpdate(vpkc_hook_callback_t cbBeforeUpdate) {
        vpkc_app_set_hook_before_update(cbBeforeUpdate);
        return *this;
    };

    /**
     * This hook is triggered after the app is updated.
     * WARNING: This hook is run during critical stages of Velopack operations.
     * Your code will be run and then the process will exit.
     * If your code has not completed within 30 seconds, it will be terminated.
     * Only supported on windows; On other operating systems, this will never be called.
     * @param cbAfterUpdate The callback to run after the app is updated.
     * @returns A reference to the builder.
     */
    VelopackApp& OnAfterUpdate(vpkc_hook_callback_t cbAfterUpdate) {
        vpkc_app_set_hook_after_update(cbAfterUpdate);
        return *this;
    };

    /**
     * This hook is triggered when the application is started for the first time after installation.
     * @param cbFirstRun The callback to run when the application is started for the first time.
     * @returns A reference to the builder.
     */
    VelopackApp& OnFirstRun(vpkc_hook_callback_t cbFirstRun) {
        vpkc_app_set_hook_first_run(cbFirstRun);
        return *this;
    };

    /**
     * This hook is triggered when the application is restarted by Velopack after installing updates.
     * @param cbRestarted The callback to run when the application is restarted.
     * @returns A reference to the builder.
     */
    VelopackApp& OnRestarted(vpkc_hook_callback_t cbRestarted) {
        vpkc_app_set_hook_restarted(cbRestarted);
        return *this;
    };

    /**
     * Runs the Velopack startup logic. This should be the first thing to run in your app.
     * In some circumstances it may terminate/restart the process to perform tasks.
     * @param pUserData A pointer to user data that will be passed to any hooks that are executed.
     */
    void Run(void* pUserData = 0) {
        vpkc_app_run(pUserData);
    };
};

/**
 * Progress callback function. Call with values between 0 and 100 inclusive.
 */
typedef std::function<void(int16_t)> vpkc_progress_send_t;

/**
 * Abstract class for retrieving release feeds and downloading assets. You should subclass this and
 * implement/override the GetReleaseFeed and DownloadReleaseEntry methods.
 * This class is used by the UpdateManager to fetch release feeds and download assets in a custom way.
 * SAFETY: It is your responsibility to ensure that a derived class instance is thread-safe,
 * as Velopack may call methods on this class from multiple threads.
 */
class IUpdateSource {
    friend class UpdateManager;
    friend class FileSource;
    friend class HttpSource;
private:
    IUpdateSource(vpkc_update_source_t* pSource) : m_pSource(pSource) {}
    vpkc_update_source_t* m_pSource = 0;
public:
    /**
     * Destructor for IUpdateSource.
     */
    virtual ~IUpdateSource() {
        vpkc_free_source(m_pSource);
    }
    /**
     * Default constructor for IUpdateSource. This will create a new custom source that calls back into the virtual methods of this class.
     */
    IUpdateSource() {
        m_pSource = vpkc_new_source_custom_callback(
            [](void* userData, const char* releasesName) {
                IUpdateSource* source = reinterpret_cast<IUpdateSource*>(userData);
                std::string json = source->GetReleaseFeed(releasesName);
                return alloc_c_string(json);
            },
            [](void* userData, char* pszFeed) {
                free_c_string(pszFeed);
            },
            [](void* userData, const struct vpkc_asset_t *pAsset, const char* pszLocalPath, size_t progressCallbackId) {
                IUpdateSource* source = reinterpret_cast<IUpdateSource*>(userData);
                VelopackAsset asset = to_cpp_VelopackAsset(pAsset).value();
                std::string localPath = to_cpp_string(pszLocalPath).value();
                std::function<void(int16_t)> progress_callback = [progressCallbackId](int16_t progress) {
                    vpkc_source_report_progress(progressCallbackId, progress);
                };
                return source->DownloadReleaseEntry(asset, localPath, progress_callback);
            },
            this);
        if (!m_pSource) {
            throw_last_error();
        }
    }

    /**
     * Fetches the release feed json for the specified releases name, and returns it as a string.
     */
    virtual const std::string GetReleaseFeed(const std::string releasesName) = 0;

    /**
     * Downloads an asset to the specified local file path. Progress is reported back to Velopack via a callback.
     */
    virtual bool DownloadReleaseEntry(const VelopackAsset& asset, const std::string localFilePath, vpkc_progress_send_t progress) = 0;
};

/**
 * A simple update source that reads release feeds and downloads assets from a local file path.
 */
class FileSource : public IUpdateSource {
public:
    /**
     * Creates a new FileSource.
     * @param filePath The path to the directory containing the releases.
     */
    FileSource(const std::string& filePath) : IUpdateSource(vpkc_new_source_file(filePath.c_str())) { }
    const std::string GetReleaseFeed(const std::string releasesName) override {
        throw std::runtime_error("Not implemented");
    }
    bool DownloadReleaseEntry(const VelopackAsset& asset, const std::string localFilePath, vpkc_progress_send_t progress) override {
        throw std::runtime_error("Not implemented");
    }
};

/**
 * A simple update source that reads release feeds and downloads assets from an remote http url.
 */
class HttpSource : public IUpdateSource {
public:
    /**
     * Creates a new HttpSource.
     * @param httpUrl The URL to the releases feed.
     */
    HttpSource(const std::string& httpUrl) : IUpdateSource(vpkc_new_source_http_url(httpUrl.c_str())) { }
    const std::string GetReleaseFeed(const std::string releasesName) override {
        throw std::runtime_error("Not implemented");
    }
    bool DownloadReleaseEntry(const VelopackAsset& asset, const std::string localFilePath, vpkc_progress_send_t progress) override {
        throw std::runtime_error("Not implemented");
    }
};

/**
 * Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
 * This class is the main entry point for interacting with Velopack.
 */
class UpdateManager {
private:
    vpkc_update_manager_t* m_pManager = 0;
    std::unique_ptr<IUpdateSource> m_pUpdateSource;

public:
    /**
     * Create a new UpdateManager instance for a local or remote directory of releases.
     * @param urlOrPath Location of the http update server or the local update directory path containing releases.
     * @param options Optional extra configuration for update manager.
     * @param locator Override the default locator configuration (usually used for testing / mocks).
     */
    UpdateManager(const std::string& urlOrPath, const UpdateOptions* options = nullptr, const VelopackLocatorConfig* locator = nullptr) {
        vpkc_update_options_t* pOptions = alloc_c_UpdateOptions_ptr(options);
        vpkc_locator_config_t* pLocator = alloc_c_VelopackLocatorConfig_ptr(locator);
        bool result = vpkc_new_update_manager(urlOrPath.c_str(), pOptions, pLocator, &m_pManager);
        free_c_UpdateOptions(pOptions);
        free_c_VelopackLocatorConfig(pLocator);
        if (!result) {
            throw_last_error();
        }
    };

    /**
     * Create a new UpdateManager instance with a custom update source.
     * @param updateSource The source to use for retrieving feed and downloading assets.
     * @param options Optional extra configuration for update manager.
     * @param locator Override the default locator configuration (usually used for testing / mocks).
     */
    template <typename T, typename = std::enable_if_t<std::is_base_of_v<IUpdateSource, T>>>
    UpdateManager(std::unique_ptr<T> pUpdateSource, const UpdateOptions* options = nullptr, const VelopackLocatorConfig* locator = nullptr) {
        vpkc_update_options_t* pOptions = alloc_c_UpdateOptions_ptr(options);
        vpkc_locator_config_t* pLocator = alloc_c_VelopackLocatorConfig_ptr(locator);
        m_pUpdateSource = std::unique_ptr<IUpdateSource>(static_cast<IUpdateSource*>(pUpdateSource.release()));
        vpkc_update_source_t* pSource = m_pUpdateSource->m_pSource;
        bool result = vpkc_new_update_manager_with_source(pSource, pOptions, pLocator, &m_pManager);
        free_c_UpdateOptions(pOptions);
        free_c_VelopackLocatorConfig(pLocator);
        if (!result) {
            throw_last_error();
        }
    };

    /**
     * Destructor for UpdateManager.
     */
    ~UpdateManager() {
        if (m_pManager != nullptr) {
            vpkc_free_update_manager(m_pManager);
            m_pManager = nullptr;
        }
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
     * Returns a VelopackAsset object if there is an update downloaded which still needs to be applied.
     * You can pass this object to WaitExitThenApplyUpdates to apply the update.
     * @returns A VelopackAsset object if there is a pending update, otherwise null.
     */
    std::optional<VelopackAsset> UpdatePendingRestart() noexcept {
        vpkc_asset_t* asset;
        if (vpkc_update_pending_restart(m_pManager, &asset)) {
            VelopackAsset cpp_asset = to_cpp_VelopackAsset(asset).value();
            vpkc_free_asset(asset);
            return cpp_asset;
        }
        return std::nullopt;
    };

    /**
     * Checks for updates, returning null if there are none available. If there are updates available, this method will return an
     * UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
     * @returns An UpdateInfo object if there is an update available, otherwise null.
     */
    std::optional<UpdateInfo> CheckForUpdates() {
        vpkc_update_info_t* update;
        vpkc_update_check_t result = vpkc_check_for_updates(m_pManager, &update);
        switch (result) {
            case vpkc_update_check_t::UPDATE_ERROR:
                throw_last_error();
                return std::nullopt;
            case vpkc_update_check_t::NO_UPDATE_AVAILABLE:
            case vpkc_update_check_t::REMOTE_IS_EMPTY:
                return std::nullopt;
            case vpkc_update_check_t::UPDATE_AVAILABLE:
                UpdateInfo cpp_info = to_cpp_UpdateInfo(update).value();
                vpkc_free_update_info(update);
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
     * @param update The update to download.
     * @param progress A callback to report progress to.
     * @param pUserData A pointer to user data that will be passed to the progress callback.
     */
    void DownloadUpdates(const UpdateInfo& update, vpkc_progress_callback_t progress = nullptr, void* pUserData = 0) {
        vpkc_update_info_t* vpkc_update = alloc_c_UpdateInfo(update);
        bool result = vpkc_download_updates(m_pManager, vpkc_update, progress, pUserData);
        free_c_UpdateInfo(vpkc_update);
        if (!result) {
            throw_last_error();
        }
    };

    /**
     * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
     * You should then clean up any state and exit your app. The updater will apply updates and then
     * optionally restart your app. The updater will only wait for 60 seconds before giving up.
     * @param asset The UpdateInfo object for the update to apply.
     * @param silent If true, the updater will not show any UI.
     * @param restart If true, the app will be restarted after the update is applied.
     * @param restartArgs The arguments to pass to the app when it is restarted.
     */
    void WaitExitThenApplyUpdates(const UpdateInfo& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        this->WaitExitThenApplyUpdates(asset.TargetFullRelease, silent, restart, restartArgs);
    };

    /**
     * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
     * You should then clean up any state and exit your app. The updater will apply updates and then
     * optionally restart your app. The updater will only wait for 60 seconds before giving up.
     * @param asset The update to apply.
     * @param silent If true, the updater will not show any UI.
     * @param restart If true, the app will be restarted after the update is applied.
     * @param restartArgs The arguments to pass to the app when it is restarted.
     */
    void WaitExitThenApplyUpdates(const VelopackAsset& asset, bool silent = false, bool restart = true, std::vector<std::string> restartArgs = {}) {
        size_t cRestartArgs;
        char** pRestartArgs = alloc_c_string_vec(restartArgs, &cRestartArgs);
        vpkc_asset_t* vpkc_asset = alloc_c_VelopackAsset(asset);
        bool result = vpkc_wait_exit_then_apply_updates(m_pManager, vpkc_asset, silent, restart, pRestartArgs, cRestartArgs);
        free_c_string_vec(pRestartArgs, cRestartArgs);
        free_c_VelopackAsset(vpkc_asset);
        if (!result) {
            throw_last_error();
        }
    };

    /**
     * This will launch the Velopack updater and optionally wait for a program to exit gracefully.
     * This method is unsafe because it does not necessarily wait for any / the correct process to exit
     * before applying updates. The `WaitExitThenApplyUpdates` method is recommended for most use cases.
     * If waitPid is 0, the updater will not wait for any process to exit before applying updates (Not Recommended).
     * @param asset The update to apply.
     * @param silent If true, the updater will not show any UI.
     * @param waitPid The process ID to wait for before applying updates. If 0, the updater will not wait.
     * @param restart If true, the app will be restarted after the update is applied.
     * @param restartArgs The arguments to pass to the app when it is restarted.
     */
    void UnsafeApplyUpdates(const VelopackAsset& asset, bool silent, uint32_t waitPid, bool restart, std::vector<std::string> restartArgs) {
        size_t cRestartArgs;
        char** pRestartArgs = alloc_c_string_vec(restartArgs, &cRestartArgs);
        vpkc_asset_t* vpkc_asset = alloc_c_VelopackAsset(asset);
        bool result = vpkc_unsafe_apply_updates(m_pManager, vpkc_asset, silent, waitPid, restart, pRestartArgs, cRestartArgs);
        free_c_string_vec(pRestartArgs, cRestartArgs);
        free_c_VelopackAsset(vpkc_asset);
        if (!result) {
            throw_last_error();
        }
    };
};

} // namespace Velopack

#endif // VELOPACK_HPP
