use crate::types::*;
use lazy_static::lazy_static;
use libc::{c_void, size_t};
use std::{
    collections::HashMap,
    ffi::CString,
    sync::{
        atomic::{AtomicUsize, Ordering},
        mpsc::Sender,
        RwLock,
    },
};
use velopack::{bundle::Manifest, sources::UpdateSource, Error, VelopackAsset, VelopackAssetFeed};

lazy_static! {
    static ref PROGRESS_CALLBACKS: RwLock<HashMap<size_t, Sender<i16>>> = RwLock::new(HashMap::new());
    static ref PROGRESS_ID: AtomicUsize = AtomicUsize::new(0);
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
    fn get_release_feed(&self, channel: &str, _: &Manifest) -> Result<VelopackAssetFeed, Error> {
        let releases_name = format!("releases.{}.json", channel);
        let releases_name_cstr = CString::new(releases_name).unwrap();
        let json_cstr_ptr = (self.cb_get_release_feed)(self.p_user_data, releases_name_cstr.as_ptr());
        let json = c_to_string_opt(json_cstr_ptr)
            .ok_or(Error::Generic("User vpkc_release_feed_delegate_t returned a null pointer instead of an asset feed".to_string()))?;
        (self.cb_free_release_feed)(self.p_user_data, json_cstr_ptr); // Free the C string returned by the callback
        let feed: VelopackAssetFeed = serde_json::from_str(&json)?;
        Ok(feed)
    }

    fn download_release_entry(&self, asset: &VelopackAsset, local_file: &str, progress_sender: Option<Sender<i16>>) -> Result<(), Error> {
        let local_file_cstr = CString::new(local_file).unwrap();
        let asset_ptr: *mut vpkc_asset_t = std::ptr::null_mut();
        unsafe { allocate_velopackasset(asset.clone(), asset_ptr) };

        let progress_callback_id = PROGRESS_ID.fetch_add(1, Ordering::SeqCst);
        if let Some(progress_sender) = &progress_sender {
            let _ = progress_sender.send(0);
            PROGRESS_CALLBACKS.write().unwrap().insert(progress_callback_id, progress_sender.clone());
        }

        let success = (self.cb_download_release_entry)(self.p_user_data, asset_ptr, local_file_cstr.as_ptr(), progress_callback_id);

        unsafe { free_velopackasset(asset_ptr) };

        if let Some(sender) = PROGRESS_CALLBACKS.write().unwrap().remove(&progress_callback_id) {
            let _ = sender.send(100);
        }

        if !success {
            return Err(Error::Generic("User vpkc_download_asset_delegate_t returned false to indicate download failed".to_owned()));
        }

        Ok(())
    }

    fn clone_boxed(&self) -> Box<dyn UpdateSource> {
        Box::new(self.clone())
    }
}
