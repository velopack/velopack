mod common;

use common::*;
use std::sync::mpsc;
use velopack::sources::{GiteaSource, UpdateSource};

fn gitea_releases_json(server_url: &str) -> String {
    format!(
        r#"[
  {{
    "name": "v2.0.0",
    "prerelease": false,
    "published_at": "2024-01-02T00:00:00Z",
    "assets": [
      {{
        "browser_download_url": "{server_url}/testuser/testrepo/releases/download/v2.0.0/releases.stable.json",
        "name": "releases.stable.json"
      }},
      {{
        "browser_download_url": "{server_url}/testuser/testrepo/releases/download/v2.0.0/TestApp-2.0.0-full.nupkg",
        "name": "TestApp-2.0.0-full.nupkg"
      }}
    ]
  }},
  {{
    "name": "v1.5.0-beta",
    "prerelease": true,
    "published_at": "2024-01-01T00:00:00Z",
    "assets": [
      {{
        "browser_download_url": "{server_url}/testuser/testrepo/releases/download/v1.5.0-beta/releases.stable.json",
        "name": "releases.stable.json"
      }}
    ]
  }}
]"#
    )
}

#[test]
fn feed_success() {
    let server = MockHttpServer::empty();
    let releases_json = gitea_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v1/repos/testuser/testrepo/releases?".into(),
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

    let source = GiteaSource::new(&format!("{}/testuser/testrepo", server.url()), None, false);
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
}

#[test]
fn filters_prereleases() {
    let server = MockHttpServer::empty();
    let releases_json = gitea_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v1/repos/testuser/testrepo/releases?".into(),
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

    let source = GiteaSource::new(
        &format!("{}/testuser/testrepo", server.url()),
        None,
        false, // prerelease=false
    );
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    // Only v2.0.0 should be included (v1.5.0-beta filtered)
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].Version, "2.0.0");
}

#[test]
fn uses_authorization_token_header() {
    let server = MockHttpServer::empty();
    let releases_json = gitea_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v1/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![("Authorization".into(), "token my-gitea-token".into())],
    });
    server.add_route(MockRoute {
        path_contains: "releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GiteaSource::new(&format!("{}/testuser/testrepo", server.url()), Some("my-gitea-token".to_string()), false);
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
}

#[test]
fn download_entry_success() {
    let body = vec![0x01, 0x02, 0x03, 0x04, 0x05];
    let server = MockHttpServer::empty();
    let releases_json = gitea_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/api/v1/repos/testuser/testrepo/releases?".into(),
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

    let source = GiteaSource::new(&format!("{}/testuser/testrepo", server.url()), None, false);
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
fn missing_browser_download_url() {
    let releases_json = r#"[
  {
    "name": "v2.0.0",
    "prerelease": false,
    "published_at": "2024-01-02T00:00:00Z",
    "assets": [
      {
        "browser_download_url": null,
        "name": "TestApp-2.0.0-full.nupkg"
      }
    ]
  }
]"#;

    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "/api/v1/repos/testuser/testrepo/releases?".into(),
        response_code: 200,
        response_body: releases_json.as_bytes().to_vec(),
        expected_headers: vec![],
    });

    let source = GiteaSource::new(&format!("{}/testuser/testrepo", server.url()), None, false);
    let asset = sample_asset();

    let dir = tempfile::tempdir().unwrap();
    let dest = dir.path().join("downloaded.nupkg");
    let result = source.download_release_entry(&asset, &dest, None);
    assert!(result.is_err());
}
