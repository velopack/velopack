mod common;

use common::*;
use std::sync::mpsc;
use velopack::sources::{HttpSource, UpdateSource};

#[test]
fn feed_success() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = HttpSource::new(&server.url());
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
    assert_eq!(feed.Assets[0].Version, "2.0.0");
}

#[test]
fn feed_server_error() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "releases.stable.json".into(),
        response_code: 500,
        response_body: b"Internal Server Error".to_vec(),
        expected_headers: vec![],
    });

    let source = HttpSource::new(&server.url());
    let manifest = test_manifest();
    let result = source.get_release_feed("stable", &manifest, "");
    assert!(result.is_err());
}

#[test]
fn download_success() {
    let body = vec![0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04];
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "TestApp-2.0.0-full.nupkg".into(),
        response_code: 200,
        response_body: body.clone(),
        expected_headers: vec![],
    });

    let source = HttpSource::new(&server.url());
    let asset = sample_asset();

    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    source.download_release_entry(&asset, &dest, None).unwrap();

    let downloaded = std::fs::read(&dest).unwrap();
    assert_eq!(downloaded, body);
}

#[test]
fn download_reports_progress() {
    // Create a body large enough to trigger progress reports (>5% chunks)
    let body = vec![0u8; 10240];
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "TestApp-2.0.0-full.nupkg".into(),
        response_code: 200,
        response_body: body,
        expected_headers: vec![],
    });

    let source = HttpSource::new(&server.url());
    let asset = sample_asset();

    let (tx, rx) = mpsc::channel();
    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    source.download_release_entry(&asset, &dest, Some(tx)).unwrap();

    let progress: Vec<i16> = rx.try_iter().collect();
    // With 10KB all in one chunk, it should report 100
    assert!(!progress.is_empty(), "Should receive at least one progress report");
    assert_eq!(*progress.last().unwrap(), 100);
}

#[test]
fn trailing_slash_handling() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    // Test with trailing slash
    let source_with_slash = HttpSource::new(&format!("{}/", server.url()));
    let manifest = test_manifest();
    let feed = source_with_slash.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
}
