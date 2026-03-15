use crate::types::*;
use lazy_static::lazy_static;
use libc::{c_void, size_t};
use std::{
    collections::HashMap,
    ffi::CString,
    path::Path,
    sync::{
        atomic::{AtomicUsize, Ordering},
        mpsc::Sender,
        RwLock,
    },
};
use velopack::{bundle::Manifest, sources::UpdateSource, Error, VelopackAsset, VelopackAssetFeed};

lazy_static! {
    static ref PROGRESS_CALLBACKS: RwLock<HashMap<size_t, Sender<i16>>> = RwLock::new(HashMap::new());
    static ref PROGRESS_ID: AtomicUsize = AtomicUsize::new(1);
}

pub fn report_csource_progress(callback_id: size_t, progress: i16) {
    let progress_callbacks = PROGRESS_CALLBACKS.read().unwrap();
    if let Some(sender) = progress_callbacks.get(&callback_id) {
        let _ = sender.send(progress);
    }
}

#[derive(Clone)]
pub struct CCallbackUpdateSource {
    pub p_user_data: *mut c_void,
    pub cb_get_release_feed: vpkc_release_feed_delegate_t,
    pub cb_free_release_feed: vpkc_free_release_feed_t,
    pub cb_download_release_entry: vpkc_download_asset_delegate_t,
}

unsafe impl Send for CCallbackUpdateSource {}
unsafe impl Sync for CCallbackUpdateSource {}

impl UpdateSource for CCallbackUpdateSource {
    fn get_release_feed(&self, channel: &str, _: &Manifest, _staged_user_id: &str) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let releases_name_cstr = CString::new(releases_name).unwrap();

        if let Some(cb_get_release_feed) = self.cb_get_release_feed {
            let json_cstr_ptr = (cb_get_release_feed)(self.p_user_data, releases_name_cstr.as_ptr());
            let json = c_to_String(json_cstr_ptr)
                .map_err(|_| Error::Other("User vpkc_release_feed_delegate_t returned a null pointer instead of an asset feed".to_string()))?;
            if let Some(cb_free_release_feed) = self.cb_free_release_feed {
                (cb_free_release_feed)(self.p_user_data, json_cstr_ptr); // Free the C string returned by the callback
            } else {
                log::error!("User vpkc_release_feed_delegate_t is null, this may be a memory leak");
            }
            let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
            Ok(feed)
        } else {
            Err(Error::Other("User vpkc_release_feed_delegate_t is null".to_string()))
        }
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &Path, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        if let Some(cb_download_release_entry) = self.cb_download_release_entry {
            let local_file = local_file.to_string_lossy().to_string();
            let local_file_cstr = CString::new(local_file).unwrap();
            let asset_ptr = unsafe { allocate_VelopackAsset(asset) };

            let progress_callback_id = PROGRESS_ID.fetch_add(1, Ordering::SeqCst);
            if let Some(progress_sender) = &progress_sender {
                let _ = progress_sender.send(0);
                PROGRESS_CALLBACKS.write().unwrap().insert(progress_callback_id, progress_sender.clone());
            }

            let success = (cb_download_release_entry)(self.p_user_data, asset_ptr, local_file_cstr.as_ptr(), progress_callback_id);
            unsafe { free_VelopackAsset(asset_ptr) };

            if let Some(sender) = PROGRESS_CALLBACKS.write().unwrap().remove(&progress_callback_id) {
                let _ = sender.send(100);
            }

            if !success {
                return Err(Error::Other(
                    "User vpkc_download_asset_delegate_t returned false to indicate download failed".to_owned(),
                ));
            }

            Ok(())
        } else {
            Err(Error::Other("User vpkc_download_asset_delegate_t is null".to_string()))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use libc::{c_char, c_void, size_t};
    use std::ffi::CString;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::mpsc;
    use velopack::sources::UpdateSource;

    fn test_manifest() -> Manifest {
        Manifest {
            id: "TestApp".to_string(),
            version: semver::Version::new(1, 0, 0),
            ..Default::default()
        }
    }

    fn test_asset() -> VelopackAsset {
        VelopackAsset {
            PackageId: "TestApp".to_string(),
            Version: "2.0.0".to_string(),
            Type: "Full".to_string(),
            FileName: "TestApp-2.0.0-full.nupkg".to_string(),
            SHA1: "abc123".to_string(),
            SHA256: "def456".to_string(),
            Size: 1048576,
            NotesMarkdown: String::new(),
            NotesHtml: String::new(),
        }
    }

    fn sample_feed_json_cstr() -> CString {
        CString::new(
            r#"{"Assets":[{"PackageId":"TestApp","Version":"2.0.0","Type":"Full","FileName":"TestApp-2.0.0-full.nupkg","SHA1":"abc123","SHA256":"def456","Size":1048576}]}"#,
        )
        .unwrap()
    }

    extern "C" fn mock_get_feed(_user_data: *mut c_void, _releases_name: *const c_char) -> *mut c_char {
        let json = sample_feed_json_cstr();
        json.into_raw()
    }

    extern "C" fn mock_free_feed(_user_data: *mut c_void, psz_feed: *mut c_char) {
        if !psz_feed.is_null() {
            unsafe {
                drop(CString::from_raw(psz_feed));
            }
        }
    }

    extern "C" fn mock_download_success(
        _user_data: *mut c_void,
        _asset: *const vpkc_asset_t,
        psz_local_path: *const c_char,
        _progress_callback_id: size_t,
    ) -> bool {
        let path = c_to_String(psz_local_path).unwrap();
        std::fs::write(&path, b"downloaded content").unwrap();
        true
    }

    extern "C" fn mock_download_failure(
        _user_data: *mut c_void,
        _asset: *const vpkc_asset_t,
        _psz_local_path: *const c_char,
        _progress_callback_id: size_t,
    ) -> bool {
        false
    }

    #[test]
    fn feed_success() {
        let source = CCallbackUpdateSource {
            p_user_data: std::ptr::null_mut(),
            cb_get_release_feed: Some(mock_get_feed),
            cb_free_release_feed: Some(mock_free_feed),
            cb_download_release_entry: None,
        };

        let manifest = test_manifest();
        let feed = source.get_release_feed("stable", &manifest, "").unwrap();
        assert_eq!(feed.Assets.len(), 1);
        assert_eq!(feed.Assets[0].PackageId, "TestApp");
        assert_eq!(feed.Assets[0].Version, "2.0.0");
    }

    #[test]
    fn feed_null_callback() {
        let source = CCallbackUpdateSource {
            p_user_data: std::ptr::null_mut(),
            cb_get_release_feed: None,
            cb_free_release_feed: None,
            cb_download_release_entry: None,
        };

        let manifest = test_manifest();
        let result = source.get_release_feed("stable", &manifest, "");
        assert!(result.is_err());
        let err = format!("{}", result.unwrap_err());
        assert!(err.contains("null"), "Unexpected error: {}", err);
    }

    #[test]
    fn free_callback_is_called() {
        static FREE_COUNT: AtomicUsize = AtomicUsize::new(0);

        extern "C" fn counting_free(_user_data: *mut c_void, psz_feed: *mut c_char) {
            FREE_COUNT.fetch_add(1, Ordering::SeqCst);
            if !psz_feed.is_null() {
                unsafe {
                    drop(CString::from_raw(psz_feed));
                }
            }
        }

        FREE_COUNT.store(0, Ordering::SeqCst);

        let source = CCallbackUpdateSource {
            p_user_data: std::ptr::null_mut(),
            cb_get_release_feed: Some(mock_get_feed),
            cb_free_release_feed: Some(counting_free),
            cb_download_release_entry: None,
        };

        let manifest = test_manifest();
        let _ = source.get_release_feed("stable", &manifest, "").unwrap();
        assert_eq!(FREE_COUNT.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn download_success() {
        let source = CCallbackUpdateSource {
            p_user_data: std::ptr::null_mut(),
            cb_get_release_feed: None,
            cb_free_release_feed: None,
            cb_download_release_entry: Some(mock_download_success),
        };

        let asset = test_asset();
        let dir = tempfile::tempdir().unwrap();
        let dest = dir.path().join("downloaded.nupkg");
        source.download_release_entry(&asset, &dest, None).unwrap();

        let content = std::fs::read(&dest).unwrap();
        assert_eq!(content, b"downloaded content");
    }

    #[test]
    fn download_failure() {
        let source = CCallbackUpdateSource {
            p_user_data: std::ptr::null_mut(),
            cb_get_release_feed: None,
            cb_free_release_feed: None,
            cb_download_release_entry: Some(mock_download_failure),
        };

        let asset = test_asset();
        let dir = tempfile::tempdir().unwrap();
        let dest = dir.path().join("downloaded.nupkg");
        let result = source.download_release_entry(&asset, &dest, None);
        assert!(result.is_err());
        let err = format!("{}", result.unwrap_err());
        assert!(err.contains("false") || err.contains("failed"), "Unexpected error: {}", err);
    }

    #[test]
    fn progress_reporting() {
        extern "C" fn download_with_progress(
            _user_data: *mut c_void,
            _asset: *const vpkc_asset_t,
            psz_local_path: *const c_char,
            progress_callback_id: size_t,
        ) -> bool {
            let path = c_to_String(psz_local_path).unwrap();
            // Report progress via the csource progress mechanism
            report_csource_progress(progress_callback_id, 50);
            std::fs::write(&path, b"data").unwrap();
            true
        }

        let source = CCallbackUpdateSource {
            p_user_data: std::ptr::null_mut(),
            cb_get_release_feed: None,
            cb_free_release_feed: None,
            cb_download_release_entry: Some(download_with_progress),
        };

        let asset = test_asset();
        let dir = tempfile::tempdir().unwrap();
        let dest = dir.path().join("downloaded.nupkg");
        let (tx, rx) = mpsc::channel();
        source.download_release_entry(&asset, &dest, Some(tx)).unwrap();

        let progress: Vec<i16> = rx.try_iter().collect();
        // Should have received: 0 (initial), 50 (from callback), 100 (final)
        assert!(progress.contains(&0), "Should contain initial 0 progress");
        assert!(progress.contains(&50), "Should contain 50 progress from callback");
        assert!(progress.contains(&100), "Should contain final 100 progress");
    }
}
