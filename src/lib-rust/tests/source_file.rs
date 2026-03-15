mod common;

use common::*;
use std::sync::mpsc;
use velopack::sources::{FileSource, UpdateSource};

#[test]
fn feed_success() {
    let dir = tempfile::tempdir().unwrap();
    let feed_path = dir.path().join("releases.stable.json");
    std::fs::write(&feed_path, sample_feed_json()).unwrap();

    let source = FileSource::new(dir.path());
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
    assert_eq!(feed.Assets[0].Version, "2.0.0");
    assert_eq!(feed.Assets[0].Type, "Full");
}

#[test]
fn feed_missing_file() {
    let dir = tempfile::tempdir().unwrap();
    let source = FileSource::new(dir.path());
    let manifest = test_manifest();
    let result = source.get_release_feed("stable", &manifest, "");
    assert!(result.is_err());
}

#[test]
fn feed_invalid_json() {
    let dir = tempfile::tempdir().unwrap();
    let feed_path = dir.path().join("releases.stable.json");
    std::fs::write(&feed_path, "not valid json {{{").unwrap();

    let source = FileSource::new(dir.path());
    let manifest = test_manifest();
    let result = source.get_release_feed("stable", &manifest, "");
    assert!(result.is_err());
}

#[test]
fn download_success() {
    let dir = tempfile::tempdir().unwrap();
    let content = b"hello world package data";
    let asset_file = dir.path().join("TestApp-2.0.0-full.nupkg");
    std::fs::write(&asset_file, content).unwrap();

    let source = FileSource::new(dir.path());
    let asset = sample_asset();

    let dest = dir.path().join("downloaded.nupkg");
    source.download_release_entry(&asset, &dest, None).unwrap();

    let downloaded = std::fs::read(&dest).unwrap();
    assert_eq!(downloaded, content);
}

#[test]
fn download_reports_progress() {
    let dir = tempfile::tempdir().unwrap();
    let content = b"test data";
    let asset_file = dir.path().join("TestApp-2.0.0-full.nupkg");
    std::fs::write(&asset_file, content).unwrap();

    let source = FileSource::new(dir.path());
    let asset = sample_asset();

    let (tx, rx) = mpsc::channel();
    let dest = dir.path().join("downloaded.nupkg");
    source.download_release_entry(&asset, &dest, Some(tx)).unwrap();

    let progress: Vec<i16> = rx.try_iter().collect();
    assert_eq!(progress, vec![50, 100]);
}

#[test]
fn download_missing_file() {
    let dir = tempfile::tempdir().unwrap();
    let source = FileSource::new(dir.path());
    let asset = sample_asset();

    let dest = dir.path().join("downloaded.nupkg");
    let result = source.download_release_entry(&asset, &dest, None);
    assert!(result.is_err());
}
