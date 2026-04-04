mod common;

use common::*;
use std::path::Path;
use velopack::sources::{NoneSource, UpdateSource};

#[test]
fn get_release_feed_returns_not_supported() {
    let source = NoneSource {};
    let manifest = test_manifest();
    let result = source.get_release_feed("stable", &manifest, "");
    assert!(result.is_err());
    let err = format!("{}", result.unwrap_err());
    assert!(err.contains("not supported") || err.contains("None source"), "Unexpected error: {}", err);
}

#[test]
fn download_release_entry_returns_not_supported() {
    let source = NoneSource {};
    let asset = sample_asset();
    let result = source.download_release_entry(&asset, Path::new("/tmp/test"), None);
    assert!(result.is_err());
    let err = format!("{}", result.unwrap_err());
    assert!(err.contains("not supported") || err.contains("None source"), "Unexpected error: {}", err);
}
