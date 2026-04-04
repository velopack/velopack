mod common;

use common::*;
use std::sync::mpsc;
use velopack::sources::{GithubSource, UpdateSource};

fn github_releases_json(server_url: &str) -> String {
    format!(
        r#"[
  {{
    "name": "v2.0.0",
    "prerelease": false,
    "published_at": "2024-01-02T00:00:00Z",
    "assets": [
      {{
        "url": "{server_url}/api/v3/repos/testuser/testrepo/releases/assets/1",
        "browser_download_url": "{server_url}/testuser/testrepo/releases/download/v2.0.0/releases.stable.json",
        "name": "releases.stable.json"
      }},
      {{
        "url": "{server_url}/api/v3/repos/testuser/testrepo/releases/assets/2",
        "browser_download_url": "{server_url}/testuser/testrepo/releases/download/v2.0.0/TestApp-2.0.0-full.nupkg",
        "name": "TestApp-2.0.0-full.nupkg"
      }}
    ]
  }},
  {{
    "name": "v3.0.0-beta",
    "prerelease": true,
    "published_at": "2024-01-03T00:00:00Z",
    "assets": [
      {{
        "url": "{server_url}/api/v3/repos/testuser/testrepo/releases/assets/3",
        "browser_download_url": "{server_url}/testuser/testrepo/releases/download/v3.0.0-beta/releases.stable.json",
        "name": "releases.stable.json"
      }}
    ]
  }}
]"#
    )
}

fn github_prerelease_feed_json() -> String {
    serde_json::json!({"Assets":[{"PackageId":"TestApp","Version":"3.0.0-beta","Type":"Full","FileName":"TestApp-3.0.0-beta-full.nupkg","SHA1":"","SHA256":"","Size":0}]}).to_string()
}

#[test]
fn feed_success() {
    // Use 127.0.0.1 to trigger enterprise path (non-github.com)
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GithubSource::new(&format!("{}/testuser/testrepo", server.url()), None, false);
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
}

#[test]
fn filters_prereleases() {
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "v2.0.0/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GithubSource::new(
        &format!("{}/testuser/testrepo", server.url()),
        None,
        false, // prerelease=false
    );
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    // Should only contain assets from the non-prerelease v2.0.0
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].Version, "2.0.0");
}

#[test]
fn includes_prereleases() {
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "v3.0.0-beta/releases.stable.json".into(),
        response_code: 200,
        response_body: github_prerelease_feed_json().into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "v2.0.0/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GithubSource::new(
        &format!("{}/testuser/testrepo", server.url()),
        None,
        true, // prerelease=true
    );
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    // Should contain assets from both releases
    assert!(feed.Assets.len() >= 2);
}

#[test]
fn uses_browser_download_url_without_token() {
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    // The browser_download_url path contains the version directory
    server.add_route(MockRoute {
        path_contains: "/testuser/testrepo/releases/download/".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GithubSource::new(
        &format!("{}/testuser/testrepo", server.url()),
        None, // no token
        false,
    );
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
}

#[test]
fn uses_api_url_with_token() {
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![("Authorization".into(), "Bearer my-secret-token".into())],
    });
    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases/assets/".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![("Authorization".into(), "Bearer my-secret-token".into())],
    });

    let source = GithubSource::new(&format!("{}/testuser/testrepo", server.url()), Some("my-secret-token".to_string()), false);
    let manifest = test_manifest();
    // With token, should use the api `url` field and include Authorization header
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
}

#[test]
fn download_entry_success() {
    let body = vec![0xCA, 0xFE, 0xBA, 0xBE];
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "TestApp-2.0.0-full.nupkg".into(),
        response_code: 200,
        response_body: body.clone(),
        expected_headers: vec![],
    });

    let source = GithubSource::new(&format!("{}/testuser/testrepo", server.url()), None, false);
    let asset = sample_asset();

    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    let (tx, rx) = mpsc::channel();
    source.download_release_entry(&asset, &dest, Some(tx)).unwrap();

    let downloaded = std::fs::read(&dest).unwrap();
    assert_eq!(downloaded, body);

    let progress: Vec<i16> = rx.try_iter().collect();
    assert!(!progress.is_empty());
}

#[test]
fn no_matching_asset() {
    let server = MockHttpServer::empty();
    let releases_json = github_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v3/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });

    let source = GithubSource::new(&format!("{}/testuser/testrepo", server.url()), None, false);
    let mut asset = sample_asset();
    asset.FileName = "nonexistent-file.nupkg".to_string();

    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    let result = source.download_release_entry(&asset, &dest, None);
    assert!(result.is_err());
    let err = format!("{}", result.unwrap_err());
    assert!(err.contains("Could not find asset"), "Unexpected error: {}", err);
}
