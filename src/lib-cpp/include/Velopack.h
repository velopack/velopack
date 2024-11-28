#ifndef VELOPACK_H
#define VELOPACK_H

/* Generated with cbindgen:0.27.0 */

/* THIS FILE IS AUTO-GENERATED - DO NOT EDIT */

#include <stdarg.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

enum vpkc_update_check_t
#ifdef __cplusplus
  : int16_t
#endif // __cplusplus
 {
  UPDATE_ERROR = -1,
  UPDATE_AVAILABLE = 0,
  NO_UPDATE_AVAILABLE = 1,
  REMOTE_IS_EMPTY = 2,
};
#ifndef __cplusplus
typedef int16_t vpkc_update_check_t;
#endif // __cplusplus

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
   * **This option should usually be left None**. <br/>
   * Overrides the default channel used to fetch updates.
   * The default channel will be whatever channel was specified on the command line when building this release.
   * For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
   * This allows users to automatically receive updates from the same channel they installed from. This options
   * allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
   * without having to reinstall the application.
   */
  char *ExplicitChannel;
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

typedef void vpkc_update_manager_t;

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
 * Holds information about the current version and pending updates, such as how many there are, and access to release notes.
 */
typedef struct vpkc_update_info_t {
  /**
   * The available version that we are updating to.
   */
  struct vpkc_asset_t TargetFullRelease;
  /**
   * True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
   * In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
   * deleted.
   */
  bool IsDowngrade;
} vpkc_update_info_t;

typedef void (*vpkc_progress_callback_t)(void *p_user_data, size_t progress);

typedef void (*vpkc_hook_callback_t)(void *p_user_data, const char *psz_app_version);

typedef void (*vpkc_log_callback_t)(void *p_user_data,
                                    const char *psz_level,
                                    const char *psz_message);

#ifdef __cplusplus
extern "C" {
#endif // __cplusplus

bool vpkc_new_update_manager(const char *psz_url_or_path,
                             struct vpkc_update_options_t *p_options,
                             struct vpkc_locator_config_t *p_locator,
                             vpkc_update_manager_t **p_manager);

size_t vpkc_get_current_version(vpkc_update_manager_t *p_manager,
                                char *psz_version,
                                size_t c_version);

size_t vpkc_get_app_id(vpkc_update_manager_t *p_manager, char *psz_id, size_t c_id);

bool vpkc_is_portable(vpkc_update_manager_t *p_manager);

bool vpkc_update_pending_restart(vpkc_update_manager_t *p_manager, struct vpkc_asset_t *p_asset);

vpkc_update_check_t vpkc_check_for_updates(vpkc_update_manager_t *p_manager,
                                           struct vpkc_update_info_t *p_update);

bool vpkc_download_updates(vpkc_update_manager_t *p_manager,
                           struct vpkc_update_info_t *p_update,
                           vpkc_progress_callback_t cb_progress,
                           void *p_user_data);

bool vpkc_wait_exit_then_apply_update(vpkc_update_manager_t *p_manager,
                                      struct vpkc_asset_t *p_asset,
                                      bool b_silent,
                                      bool b_restart,
                                      char **p_restart_args,
                                      size_t c_restart_args);

void vpkc_free_update_manager(vpkc_update_manager_t *p_manager);

void vpkc_free_update_info(struct vpkc_update_info_t *p_update_info);

void vpkc_free_asset(struct vpkc_asset_t *p_asset);

void vpkc_app_run(void *p_user_data);

void vpkc_app_set_auto_apply_on_startup(bool b_auto_apply);

void vpkc_app_set_args(char **p_args, size_t c_args);

void vpkc_app_set_locator(struct vpkc_locator_config_t *p_locator);

void vpkc_app_set_hook_after_install(vpkc_hook_callback_t cb_after_install);

void vpkc_app_set_hook_before_uninstall(vpkc_hook_callback_t cb_before_uninstall);

void vpkc_app_set_hook_before_update(vpkc_hook_callback_t cb_before_update);

void vpkc_app_set_hook_after_update(vpkc_hook_callback_t cb_after_update);

void vpkc_app_set_hook_first_run(vpkc_hook_callback_t cb_first_run);

void vpkc_app_set_hook_restarted(vpkc_hook_callback_t cb_restarted);

size_t vpkc_get_last_error(char *psz_error, size_t c_error);

void vpkc_set_logger(vpkc_log_callback_t cb_log, void *p_user_data);

#ifdef __cplusplus
}  // extern "C"
#endif  // __cplusplus

#endif  /* VELOPACK_H */
