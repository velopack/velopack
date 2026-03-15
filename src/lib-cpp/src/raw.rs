use std::sync::Arc;

use crate::types::*;
use velopack::{sources::UpdateSource, UpdateManager};

/// Wrapper around `Arc<dyn UpdateSource>` that implements `UpdateSource` by delegation.
/// This allows sharing a single source across multiple `UpdateManager` instances
/// without requiring `clone_boxed` on the trait.
struct SharedSource(Arc<dyn UpdateSource>);

impl UpdateSource for SharedSource {
    fn get_release_feed(
        &self,
        channel: &str,
        app: &velopack::bundle::Manifest,
        staged_user_id: &str,
    ) -> Result<velopack::VelopackAssetFeed, velopack::Error> {
        self.0.get_release_feed(channel, app, staged_user_id)
    }

    fn download_release_entry(
        &self,
        asset: &velopack::VelopackAsset,
        local_file: &std::path::Path,
        progress_sender: Option<std::sync::mpsc::Sender<i16>>,
    ) -> Result<(), velopack::Error> {
        self.0.download_release_entry(asset, local_file, progress_sender)
    }
}

pub trait RawPtrExt<'a, T>: Sized {
    fn to_opaque_ref(self) -> Option<&'a T>;
}

impl<'a> RawPtrExt<'a, UpdateManager> for *mut vpkc_update_manager_t {
    fn to_opaque_ref(self) -> Option<&'a UpdateManager> {
        if self.is_null() {
            return None;
        }

        let opaque = unsafe { &*(self as *mut UpdateManager) };
        Some(opaque)
    }
}

pub struct UpdateManagerRawPtr;

impl UpdateManagerRawPtr {
    pub fn new(obj: UpdateManager) -> *mut vpkc_update_manager_t {
        log::debug!("vpkc_update_manager_t allocated");
        let boxed = Box::new(obj);
        Box::into_raw(boxed) as *mut vpkc_update_manager_t
    }

    pub fn free(p_manager: *mut vpkc_update_manager_t) {
        if p_manager.is_null() {
            return;
        }

        // Convert the raw pointer back into a Box to deallocate it properly
        log::debug!("vpkc_update_manager_t freed");
        let _ = unsafe { Box::from_raw(p_manager as *mut UpdateManager) };
    }
}

pub struct UpdateSourceContainer {
    source: Arc<dyn UpdateSource>,
}

pub struct UpdateSourceRawPtr;

impl UpdateSourceRawPtr {
    pub fn new(source: Box<dyn UpdateSource>) -> *mut vpkc_update_source_t {
        log::debug!("vpkc_update_source_t allocated");
        let boxed = Box::new(UpdateSourceContainer { source: Arc::from(source) });
        Box::into_raw(boxed) as *mut vpkc_update_source_t
    }

    pub fn free(p_source: *mut vpkc_update_source_t) {
        if p_source.is_null() {
            return;
        }

        // Convert the raw pointer back into a Box to deallocate it properly
        log::debug!("vpkc_update_source_t freed");
        let _ = unsafe { Box::from_raw(p_source as *mut UpdateSourceContainer) };
    }

    pub fn get_source_boxed(p_source: *mut vpkc_update_source_t) -> Option<Box<dyn UpdateSource>> {
        if p_source.is_null() {
            return None;
        }

        let opaque = unsafe { &*(p_source as *mut UpdateSourceContainer) };
        Some(Box::new(SharedSource(Arc::clone(&opaque.source))))
    }
}
