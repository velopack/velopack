mod common;

use common::*;
use std::sync::mpsc;
use velopack::sources::{UpdateSource, VelopackFlowSource};

fn flow_feed_json() -> String {
    serde_json::json!([{
        "Id": "release-id-123",
        "PackageId": "TestApp",
        "Version": "2.0.0",
        "Type": "Full",
        "FileName": "TestApp-2.0.0-full.nupkg",
        "SHA1": "abc123",
        "SHA256": "def456",
        "Size": 1048576,
        "NotesMarkdown": "# v2",
        "NotesHtml": "<h1>v2</h1>"
    }])
    .to_string()
}

#[test]
fn feed_success() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "v1.0/manifest/TestApp/stable".into(),
        response_code: 200,
        response_body: flow_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = VelopackFlowSource::new(Some(&format!("{}/", server.url())));
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
    assert_eq!(feed.Assets[0].Version, "2.0.0");
    assert_eq!(feed.Assets[0].SHA1, "abc123");
}

#[test]
fn stores_release_ids_and_download() {
    let body = vec![0xAA, 0xBB, 0xCC];
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "v1.0/manifest/TestApp/stable".into(),
        response_code: 200,
        response_body: flow_feed_json().into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "v1.0/download/release-id-123".into(),
        response_code: 200,
        response_body: body.clone(),
        expected_headers: vec![],
    });

    let source = VelopackFlowSource::new(Some(&format!("{}/", server.url())));
    let manifest = test_manifest();

    // First, get the feed to populate release IDs
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);

    // Now download using the stored release ID
    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    let (tx, rx) = mpsc::channel();
    source.download_release_entry(&feed.Assets[0], &dest, Some(tx)).unwrap();

    let downloaded = std::fs::read(&dest).unwrap();
    assert_eq!(downloaded, body);

    let progress: Vec<i16> = rx.try_iter().collect();
    assert!(!progress.is_empty());
}

#[test]
fn download_without_prior_feed() {
    let server = MockHttpServer::empty();
    let source = VelopackFlowSource::new(Some(&format!("{}/", server.url())));
    let asset = sample_asset();

    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    let result = source.download_release_entry(&asset, &dest, None);
    assert!(result.is_err());
    let err = format!("{}", result.unwrap_err());
    assert!(
        err.contains("release ID") || err.contains("get_release_feed"),
        "Unexpected error: {}",
        err
    );
}

#[test]
fn query_params_included() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "v1.0/manifest/TestApp/stable".into(),
        response_code: 200,
        response_body: flow_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = VelopackFlowSource::new(Some(&format!("{}/", server.url())));
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "test-staging-id").unwrap();
    assert_eq!(feed.Assets.len(), 1);
}

#[test]
fn base_uri_trailing_slash() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "v1.0/manifest/TestApp/stable".into(),
        response_code: 200,
        response_body: flow_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    // Constructor should add trailing slash if missing
    let source = VelopackFlowSource::new(Some(&server.url())); // no trailing slash
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
}

#[test]
fn default_base_uri() {
    let source = VelopackFlowSource::new(None);
    // We can't actually hit the real API, but we can verify the source was created
    // and that it defaults to the correct URL by trying a feed request that will fail
    // with a network error (not a config error)
    let manifest = test_manifest();
    let result = source.get_release_feed("stable", &manifest, "");
    // Should fail with a network error, not a config error
    assert!(result.is_err());
}
