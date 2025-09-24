package velopack

/*
#include <stdlib.h>
#include "../../lib-cpp/include/Velopack.h"

#ifdef _WIN32
#define GO_EXPORT __declspec(dllexport)
#else
#define GO_EXPORT
#endif

extern GO_EXPORT char *go_release_feed_callback(uintptr_t user_data, char *psz_releases_name);
extern GO_EXPORT void go_free_release_feed_callback(uintptr_t user_data, char *psz_feed);
extern GO_EXPORT bool go_download_asset_callback(uintptr_t user_data, struct vpkc_asset_t *asset, char *psz_local_path, size_t progress_callback_id);
extern GO_EXPORT void go_progress_callback(uintptr_t user_data, size_t progress);
extern GO_EXPORT void go_after_install_callback(uintptr_t user_data, char *psz_app_version);
extern GO_EXPORT void go_before_uninstall_callback(uintptr_t user_data, char *psz_app_version);
extern GO_EXPORT void go_before_update_callback(uintptr_t user_data, char *psz_app_version);
extern GO_EXPORT void go_after_update_callback(uintptr_t user_data, char *psz_app_version);
extern GO_EXPORT void go_first_run_callback(uintptr_t user_data, char *psz_app_version);
extern GO_EXPORT void go_restarted_callback(uintptr_t user_data, char *psz_app_version);
extern GO_EXPORT void go_log_callback(uintptr_t user_data, char *level, char *psz_message);

static vpkc_update_source_t *go_vpkc_new_source_custom_callback(uintptr_t user_data) {
    return vpkc_new_source_custom_callback(
        (vpkc_release_feed_delegate_t)go_release_feed_callback,
        (vpkc_free_release_feed_t)go_free_release_feed_callback,
        (vpkc_download_asset_delegate_t)go_download_asset_callback,
        (void *)user_data
    );
}

static bool go_vpkc_download_updates(vpkc_update_manager_t *p_manager,
                                     struct vpkc_update_info_t *p_update,
                                     uintptr_t user_data) {
    return vpkc_download_updates(p_manager, p_update, (vpkc_progress_callback_t)go_progress_callback, (void *)user_data);
}

static void go_set_logger() {
    vpkc_set_logger((vpkc_log_callback_t)go_log_callback, 0);
}

static void go_callbacks() {
    vpkc_app_set_hook_after_install((vpkc_hook_callback_t)go_after_install_callback);
    vpkc_app_set_hook_before_uninstall((vpkc_hook_callback_t)go_before_uninstall_callback);
    vpkc_app_set_hook_before_update((vpkc_hook_callback_t)go_before_update_callback);
    vpkc_app_set_hook_after_update((vpkc_hook_callback_t)go_after_update_callback);
    vpkc_app_set_hook_first_run((vpkc_hook_callback_t)go_first_run_callback);
    vpkc_app_set_hook_restarted((vpkc_hook_callback_t)go_restarted_callback);
}

*/
import "C"
import (
	"encoding/json"
	"errors"
	"runtime"
	"runtime/cgo"
	"unsafe"
)

type SourceCustomCallbacks struct {
	source *UpdateSource // Keep a reference to the UpdateSource for constructing the progress callback.

	// ReleaseFeedFunc should return the raw JSON string of the release.json feed.
	ReleaseFeedFunc func(psz_releases_name string) json.RawMessage
	/*
	   DownloadAssetFunc is expected to download the provided asset to the provided local file path.
	   Througout, you can use the progress callback to write progress reports (0-100). The function should
	   return true if the download was successful, false otherwise.
	*/
	DownloadAssetFunc func(asset *Asset, local_path string, progress_callback func(progress int16)) bool
}

//export go_release_feed_callback
func go_release_feed_callback(user_data uintptr, psz_releases_name *C.char) *C.char {
	return C.CString(string(cgo.Handle(user_data).Value().(SourceCustomCallbacks).ReleaseFeedFunc(C.GoString(psz_releases_name))))
}

//export go_free_release_feed_callback
func go_free_release_feed_callback(_ uintptr, psz_feed *C.char) {
	C.free(unsafe.Pointer(psz_feed))
}

//export go_download_asset_callback
func go_download_asset_callback(user_data uintptr, asset *C.vpkc_asset_t, psz_local_path *C.char, progress_callback_id C.size_t) C.bool {
	callbacks := cgo.Handle(user_data).Value().(SourceCustomCallbacks)
	success := callbacks.DownloadAssetFunc(
		toAsset(asset),
		C.GoString(psz_local_path),
		func(progress int16) {
			C.vpkc_source_report_progress(progress_callback_id, C.int16_t(progress))
		},
	)
	return C.bool(success)
}

//export go_progress_callback
func go_progress_callback(user_data uintptr, progress C.size_t) {
	cgo.Handle(user_data).Value().(func(uint))(uint(progress))
}

//export go_after_install_callback
func go_after_install_callback(_ uintptr, psz_app_version *C.char) {
	if app.WindowsHookAfterInstall == nil {
		return
	}
	app.WindowsHookAfterInstall(C.GoString(psz_app_version))
}

//export go_before_uninstall_callback
func go_before_uninstall_callback(_ uintptr, psz_app_version *C.char) {
	if app.WindowsHookBeforeUninstall == nil {
		return
	}
	app.WindowsHookBeforeUninstall(C.GoString(psz_app_version))
}

//export go_before_update_callback
func go_before_update_callback(_ uintptr, psz_app_version *C.char) {
	if app.WindowsHookBeforeUpdate == nil {
		return
	}
	app.WindowsHookBeforeUpdate(C.GoString(psz_app_version))
}

//export go_after_update_callback
func go_after_update_callback(_ uintptr, psz_app_version *C.char) {
	if app.WindowsHookAfterUpdate == nil {
		return
	}
	app.WindowsHookAfterUpdate(C.GoString(psz_app_version))
}

//export go_first_run_callback
func go_first_run_callback(_ uintptr, psz_app_version *C.char) {
	if app.HookFirstRun == nil {
		return
	}
	app.HookFirstRun(C.GoString(psz_app_version))
}

//export go_restarted_callback
func go_restarted_callback(_ uintptr, psz_app_version *C.char) {
	if app.HookRestarted == nil {
		return
	}
	app.HookRestarted(C.GoString(psz_app_version))
}

//export go_log_callback
func go_log_callback(_ uintptr, level, psz_message *C.char) {
	app.Logger(C.GoString(level), C.GoString(psz_message))
}

/*
Create a new _CUSTOM_ update source with user-provided callbacks to fetch release feeds and download assets.
You can report download progress using [UpdateSource.ReportProgress].
*/
func NewSourceCustomCallback(callbacks SourceCustomCallbacks) (*UpdateSource, error) {
	if callbacks.ReleaseFeedFunc == nil {
		return nil, errors.New("ReleaseFeedFunc must not be nil")
	}
	if callbacks.DownloadAssetFunc == nil {
		return nil, errors.New("DownloadAssetFunc must not be nil")
	}
	var source = new(UpdateSource)
	source.handle = C.go_vpkc_new_source_custom_callback(C.uintptr_t(cgo.NewHandle(callbacks)))
	if source.handle == nil {
		return nil, get_last_error()
	}
	runtime.AddCleanup(source, func(handle unsafe.Pointer) {
		C.vpkc_free_source(handle)
	}, source.handle)
	return source, nil
}

// DownloadUpdates downloads the specified updates to the local app packages directory.
// Progress is reported back to the caller via an optional callback. This function will
// acquire a global update lock so may fail if there is already another update operation
// in progress.
//   - If the update contains delta packages and the delta feature is enabled
//   - this method will attempt to unpack and prepare them.
//   - If there is no delta update available, or there is an error preparing delta
//     packages, this method will fall back to downloading the full version of the update.
func (up *UpdateManager) DownloadUpdates(update_info *UpdateInfo, progress func(progress uint)) error {
	var info_handle *C.vpkc_update_info_t
	if update_info != nil {
		info_handle = update_info.handle
	}
	if !C.go_vpkc_download_updates(up.handle,
		info_handle,
		C.uintptr_t(cgo.NewHandle(progress)),
	) {
		return get_last_error()
	}
	update_info.load(update_info.handle)
	return nil
}

// Run helps you to handle app activation events correctly.
// This should be used as early as possible in your application startup code.
// (eg. the beginning of main() or wherever your entry point is).
// This function will not return in some cases.
func Run(a App) {
	app = a
	C.vpkc_app_set_auto_apply_on_startup(C.bool(app.AutoApplyOnStartup))
	if app.Args != nil {
		var args = make([]*C.char, len(app.Args)+1) // +1 for null terminator
		for i, arg := range app.Args {
			arg_cstr := C.CString(arg)
			defer C.free(unsafe.Pointer(arg_cstr))
			args[i] = arg_cstr
		}
		C.vpkc_app_set_args(&args[0], C.size_t(len(app.Args)))
	}
	if app.Locator != nil {
		RootAppDir := C.CString(app.Locator.RootAppDir)
		defer C.free(unsafe.Pointer(RootAppDir))
		UpdateExePath := C.CString(app.Locator.UpdateExePath)
		defer C.free(unsafe.Pointer(UpdateExePath))
		PackagesDir := C.CString(app.Locator.PackagesDir)
		defer C.free(unsafe.Pointer(PackagesDir))
		ManifestPath := C.CString(app.Locator.ManifestPath)
		defer C.free(unsafe.Pointer(ManifestPath))
		CurrentBinaryDir := C.CString(app.Locator.CurrentBinaryDir)
		defer C.free(unsafe.Pointer(CurrentBinaryDir))
		C.vpkc_app_set_locator(&C.vpkc_locator_config_t{
			RootAppDir:       RootAppDir,
			UpdateExePath:    UpdateExePath,
			PackagesDir:      PackagesDir,
			ManifestPath:     ManifestPath,
			CurrentBinaryDir: CurrentBinaryDir,
			IsPortable:       C.bool(app.Locator.IsPortable),
		})
	}
	C.go_callbacks()
	if app.Logger != nil {
		C.go_set_logger()
	}
	C.vpkc_app_run(nil)
}
