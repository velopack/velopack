#ifndef VELOPACK_H
#define VELOPACK_H

/* Generated with cbindgen:0.28.0 */

/* THIS FILE IS AUTO-GENERATED - DO NOT EDIT */

#include <stdarg.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

/**
 * The result of a call to check for updates. This can indicate that an update is available, or that an error occurred.
 */
enum vpkc_update_check_t
#ifdef __cplusplus
  : int8_t
#endif // __cplusplus
 {
  UPDATE_ERROR = -1,
  UPDATE_AVAILABLE = 0,
  NO_UPDATE_AVAILABLE = 1,
  REMOTE_IS_EMPTY = 2,
};
#ifndef __cplusplus
typedef int8_t vpkc_update_check_t;
#endif // __cplusplus

/**
 * Opaque type for a Velopack UpdateSource. Must be freed with `vpkc_free_update_source`.
 */
typedef void vpkc_update_source_t;

/**
 * User delegate for to fetch a release feed. This function should return the raw JSON string of the release.json feed.
 */
typedef char *(*vpkc_release_feed_delegate_t)(void *p_user_data, const char *psz_releases_name);

/**
 * User delegate for freeing a release feed. This function should free the feed string returned by `vpkc_release_feed_delegate_t`.
 */
typedef void (*vpkc_free_release_feed_t)(void *p_user_data, char *psz_feed);

/**
 * An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
 */
typedef struct vpkc_asset_t {
  /**
   * The name or Id of the package containing this release.
   */
  char *PackageId;
  /**
   * The version of this release.
   */
  char *Version;
  /**
   * The type of asset (eg. "Full" or "Delta").
   */
  char *Type;
  /**
   * The filename of the update package containing this release.
   */
  char *FileName;
  /**
   * The SHA1 checksum of the update package containing this release.
   */
  char *SHA1;
  /**
   * The SHA256 checksum of the update package containing this release.
   */
  char *SHA256;
  /**
   * The size in bytes of the update package containing this release.
   */
  uint64_t Size;
  /**
   * The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string.
   */
  char *NotesMarkdown;
  /**
   * The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string.
   */
  char *NotesHtml;
} vpkc_asset_t;

/**
 * User delegate for downloading an asset file. This function is expected to download the provided asset
 * to the provided local file path. Througout, you can use the progress callback to write progress reports.
 * The function should return true if the download was successful, false otherwise.
 * Progress
 */
typedef bool (*vpkc_download_asset_delegate_t)(void *p_user_data,
                                               const struct vpkc_asset_t *p_asset,
                                               const char *psz_local_path,
                                               size_t progress_callback_id);

/**
 * Options to customise the behaviour of UpdateManager.
 */
typedef struct vpkc_update_options_t {
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
  char *ExplicitChannel;
  /**
   * Sets the maximum number of deltas to consider before falling back to a full update.
   * The default is 10. Set to a negative number (eg. -1) to disable deltas.
   */
  int32_t MaximumDeltasBeforeFallback;
} vpkc_update_options_t;

/**
 * VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
 */
typedef struct vpkc_locator_config_t {
  /**
   * The root directory of the current app.
   */
  char *RootAppDir;
  /**
   * The path to the Update.exe binary.
   */
  char *UpdateExePath;
  /**
   * The path to the packages' directory.
   */
  char *PackagesDir;
  /**
   * The current app manifest.
   */
  char *ManifestPath;
  /**
   * The directory containing the application's user binaries.
   */
  char *CurrentBinaryDir;
  /**
   * Whether the current application is portable or installed.
   */
  bool IsPortable;
} vpkc_locator_config_t;

/**
 * Opaque type for the Velopack UpdateManager. Must be freed with `vpkc_free_update_manager`.
 */
typedef void vpkc_update_manager_t;

/**
 * Holds information about the current version and pending updates, such as how many there are, and access to release notes.
 */
typedef struct vpkc_update_info_t {
  /**
   * The available version that we are updating to.
   */
  struct vpkc_asset_t *TargetFullRelease;
  /**
   * The base release that this update is based on. This is only available if the update is a delta update.
   */
  struct vpkc_asset_t *BaseRelease;
  /**
   * The list of delta updates that can be applied to the base version to get to the target version.
   */
  struct vpkc_asset_t **DeltasToTarget;
  /**
   * The number of elements in the DeltasToTarget array.
   */
  size_t DeltasToTargetCount;
  /**
   * True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
   * In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
   * deleted.
   */
  bool IsDowngrade;
} vpkc_update_info_t;

/**
 * Progress callback function.
 */
typedef void (*vpkc_progress_callback_t)(void *p_user_data, size_t progress);

/**
 * VelopackApp startup hook callback function.
 */
typedef void (*vpkc_hook_callback_t)(void *p_user_data, const char *psz_app_version);

/**
 * Log callback function.
 */
typedef void (*vpkc_log_callback_t)(void *p_user_data,
                                    const char *psz_level,
                                    const char *psz_message);

#ifdef __cplusplus
extern "C" {
#endif // __cplusplus

/**
 * Create a new FileSource update source for a given file path.
 */
vpkc_update_source_t *vpkc_new_source_file(const char *psz_file_path);

/**
 * Create a new HttpSource update source for a given HTTP URL.
 */
vpkc_update_source_t *vpkc_new_source_http_url(const char *psz_http_url);

/**
 * Create a new _CUSTOM_ update source with user-provided callbacks to fetch release feeds and download assets.
 * You can report download progress using `vpkc_source_report_progress`. Note that the callbacks must be valid
 * for the lifetime of any UpdateManager's that use this source. You should call `vpkc_free_source` to free the source,
 * but note that if the source is still in use by an UpdateManager, it will not be freed until the UpdateManager is freed.
 * Therefore to avoid possible issues, it is recommended to create this type of source once for the lifetime of your application.
 */
vpkc_update_source_t *vpkc_new_source_custom_callback(vpkc_release_feed_delegate_t cb_release_feed,
                                                      vpkc_free_release_feed_t cb_free_release_feed,
                                                      vpkc_download_asset_delegate_t cb_download_entry,
                                                      void *p_user_data);

/**
 * Sends a progress update to the callback with the specified ID. This is used by custom
 * update sources created with `vpkc_new_source_custom_callback` to report download progress.
 */
void vpkc_source_report_progress(size_t progress_callback_id, int16_t progress);

/**
 * Frees a vpkc_update_source_t instance.
 */
void vpkc_free_source(vpkc_update_source_t *p_source);

/**
 * Create a new UpdateManager instance.
 * @param urlOrPath Location of the update server or path to the local update directory.
 * @param options Optional extra configuration for update manager.
 * @param locator Override the default locator configuration (usually used for testing / mocks).
 */
bool vpkc_new_update_manager(const char *psz_url_or_path,
                             struct vpkc_update_options_t *p_options,
                             struct vpkc_locator_config_t *p_locator,
                             vpkc_update_manager_t **p_manager);

/**
 * Create a new UpdateManager instance with a custom UpdateSource.
 * @param urlOrPath Location of the update server or path to the local update directory.
 * @param options Optional extra configuration for update manager.
 * @param locator Override the default locator configuration (usually used for testing / mocks).
 */
bool vpkc_new_update_manager_with_source(vpkc_update_source_t *p_source,
                                         struct vpkc_update_options_t *p_options,
                                         struct vpkc_locator_config_t *p_locator,
                                         vpkc_update_manager_t **p_manager);

/**
 * Returns the currently installed version of the app.
 */
size_t vpkc_get_current_version(vpkc_update_manager_t *p_manager,
                                char *psz_version,
                                size_t c_version);

/**
 * Returns the currently installed app id.
 */
size_t vpkc_get_app_id(vpkc_update_manager_t *p_manager, char *psz_id, size_t c_id);

/**
 * Returns whether the app is in portable mode. On Windows this can be true or false.
 * On MacOS and Linux this will always be true.
 */
bool vpkc_is_portable(vpkc_update_manager_t *p_manager);

/**
 * Returns an UpdateInfo object if there is an update downloaded which still needs to be applied.
 * You can pass the UpdateInfo object to waitExitThenApplyUpdate to apply the update.
 */
bool vpkc_update_pending_restart(vpkc_update_manager_t *p_manager, struct vpkc_asset_t **p_asset);

/**
 * Checks for updates, returning None if there are none available. If there are updates available, this method will return an
 * UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
 */
vpkc_update_check_t vpkc_check_for_updates(vpkc_update_manager_t *p_manager,
                                           struct vpkc_update_info_t **p_update);

/**
 * Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional callback.
 * This function will acquire a global update lock so may fail if there is already another update operation in progress.
 * - If the update contains delta packages and the delta feature is enabled
 *   this method will attempt to unpack and prepare them.
 * - If there is no delta update available, or there is an error preparing delta
 *   packages, this method will fall back to downloading the full version of the update.
 */
bool vpkc_download_updates(vpkc_update_manager_t *p_manager,
                           struct vpkc_update_info_t *p_update,
                           vpkc_progress_callback_t cb_progress,
                           void *p_user_data);

/**
 * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
 * You should then clean up any state and exit your app. The updater will apply updates and then
 * optionally restart your app. The updater will only wait for 60 seconds before giving up.
 */
bool vpkc_wait_exit_then_apply_updates(vpkc_update_manager_t *p_manager,
                                       struct vpkc_asset_t *p_asset,
                                       bool b_silent,
                                       bool b_restart,
                                       char **p_restart_args,
                                       size_t c_restart_args);

/**
 * This will launch the Velopack updater and optionally wait for a program to exit gracefully.
 * This method is unsafe because it does not necessarily wait for any / the correct process to exit
 * before applying updates. The `vpkc_wait_exit_then_apply_updates` method is recommended for most use cases.
 * If dw_wait_pid is 0, the updater will not wait for any process to exit before applying updates (Not Recommended).
 */
bool vpkc_unsafe_apply_updates(vpkc_update_manager_t *p_manager,
                               struct vpkc_asset_t *p_asset,
                               bool b_silent,
                               uint32_t dw_wait_pid,
                               bool b_restart,
                               char **p_restart_args,
                               size_t c_restart_args);

/**
 * Frees a vpkc_update_manager_t instance.
 */
void vpkc_free_update_manager(vpkc_update_manager_t *p_manager);

/**
 * Frees a vpkc_update_info_t instance.
 */
void vpkc_free_update_info(struct vpkc_update_info_t *p_update_info);

/**
 * Frees a vpkc_asset_t instance.
 */
void vpkc_free_asset(struct vpkc_asset_t *p_asset);

/**
 * VelopackApp helps you to handle app activation events correctly.
 * This should be used as early as possible in your application startup code.
 * (eg. the beginning of main() or wherever your entry point is)
 */
void vpkc_app_run(void *p_user_data);

/**
 * Set whether to automatically apply downloaded updates on startup. This is ON by default.
 */
void vpkc_app_set_auto_apply_on_startup(bool b_auto_apply);

/**
 * Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
 */
void vpkc_app_set_args(char **p_args, size_t c_args);

/**
 * VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
 */
void vpkc_app_set_locator(struct vpkc_locator_config_t *p_locator);

/**
 * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
 * Your code will be run and then the process will exit.
 * If your code has not completed within 30 seconds, it will be terminated.
 * Only supported on windows; On other operating systems, this will never be called.
 */
void vpkc_app_set_hook_after_install(vpkc_hook_callback_t cb_after_install);

/**
 * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
 * Your code will be run and then the process will exit.
 * If your code has not completed within 30 seconds, it will be terminated.
 * Only supported on windows; On other operating systems, this will never be called.
 */
void vpkc_app_set_hook_before_uninstall(vpkc_hook_callback_t cb_before_uninstall);

/**
 * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
 * Your code will be run and then the process will exit.
 * If your code has not completed within 30 seconds, it will be terminated.
 * Only supported on windows; On other operating systems, this will never be called.
 */
void vpkc_app_set_hook_before_update(vpkc_hook_callback_t cb_before_update);

/**
 * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
 * Your code will be run and then the process will exit.
 * If your code has not completed within 30 seconds, it will be terminated.
 * Only supported on windows; On other operating systems, this will never be called.
 */
void vpkc_app_set_hook_after_update(vpkc_hook_callback_t cb_after_update);

/**
 * This hook is triggered when the application is started for the first time after installation.
 */
void vpkc_app_set_hook_first_run(vpkc_hook_callback_t cb_first_run);

/**
 * This hook is triggered when the application is restarted by Velopack after installing updates.
 */
void vpkc_app_set_hook_restarted(vpkc_hook_callback_t cb_restarted);

/**
 * Get the last error message that occurred in the Velopack library.
 */
size_t vpkc_get_last_error(char *psz_error, size_t c_error);

/**
 * Set a custom log callback. This will be called for all log messages generated by the Velopack library.
 */
void vpkc_set_logger(vpkc_log_callback_t cb_log,
                     void *p_user_data);

#ifdef __cplusplus
}  // extern "C"
#endif  // __cplusplus

#endif  /* VELOPACK_H */
