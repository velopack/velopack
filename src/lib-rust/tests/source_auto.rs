mod common;

use common::*;
use velopack::sources::{AutoSource, UpdateSource};

#[test]
fn detects_file_path() {
    let dir = tempfile::tempdir().unwrap();
    let feed_path = dir.path().join("releases.stable.json");
    std::fs::write(&feed_path, sample_feed_json()).unwrap();

    let source = AutoSource::new(&dir.path().to_string_lossy());
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
}

#[test]
fn detects_github_com() {
    let source = AutoSource::new("https://github.com/testuser/testrepo");
    let manifest = test_manifest();
    // Should fail with a network error (trying to reach github.com), not a local file error
    let result = source.get_release_feed("stable", &manifest, "");
    assert!(result.is_err());
    let err = format!("{}", result.unwrap_err());
    // Should be a network-level error, not "file not found"
    assert!(!err.contains("File does not exist"), "Should not be a file error: {}", err);
}

#[test]
fn detects_generic_http() {
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    // Non-github/gitlab/gitea URL should be treated as HttpSource
    let source = AutoSource::new(&server.url());
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert_eq!(feed.Assets.len(), 1);
}

#[test]
fn case_insensitive_domain() {
    // GITHUB.COM should still be detected as GitHub
    let source = AutoSource::new("https://GITHUB.COM/testuser/testrepo");
    let manifest = test_manifest();
    let result = source.get_release_feed("stable", &manifest, "");
    assert!(result.is_err());
    let err = format!("{}", result.unwrap_err());
    // Should be a network error (GitHub API), not a file error
    assert!(!err.contains("File does not exist"), "Should not be a file error: {}", err);
}
