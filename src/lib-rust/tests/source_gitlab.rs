mod common;

use common::*;
use std::sync::mpsc;
use velopack::sources::{GitlabSource, UpdateSource};

fn gitlab_releases_json(server_url: &str) -> String {
    format!(
        r#"[
  {{
    "name": "v2.0.0",
    "upcoming_release": false,
    "released_at": "2024-01-02T00:00:00Z",
    "assets": {{
      "count": 2,
      "links": [
        {{
          "name": "releases.stable.json",
          "url": "{server_url}/api/url/releases.stable.json",
          "direct_asset_url": "{server_url}/direct/releases.stable.json"
        }},
        {{
          "name": "TestApp-2.0.0-full.nupkg",
          "url": "{server_url}/api/url/TestApp-2.0.0-full.nupkg",
          "direct_asset_url": "{server_url}/direct/TestApp-2.0.0-full.nupkg"
        }}
      ]
    }}
  }},
  {{
    "name": "v3.0.0-rc1",
    "upcoming_release": true,
    "released_at": "2024-01-03T00:00:00Z",
    "assets": {{
      "count": 1,
      "links": [
        {{
          "name": "releases.stable.json",
          "url": "{server_url}/api/url/upcoming/releases.stable.json",
          "direct_asset_url": "{server_url}/direct/upcoming/releases.stable.json"
        }}
      ]
    }}
  }}
]"#
    )
}

#[test]
fn feed_success() {
    let server = MockHttpServer::empty();
    let releases_json = gitlab_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/releases?per_page=".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "/direct/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GitlabSource::new(&format!("{}/api/v4/projects/12345", server.url()), None, false);
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
    assert_eq!(feed.Assets[0].PackageId, "TestApp");
}

#[test]
fn filters_upcoming_releases() {
    let server = MockHttpServer::empty();
    let releases_json = gitlab_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/releases?per_page=".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    server.add_route(MockRoute {
        path_contains: "/direct/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GitlabSource::new(
        &format!("{}/api/v4/projects/12345", server.url()),
        None,
        false, // prerelease=false, filters upcoming_release=true
    );
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    // Only v2.0.0 (non-upcoming) should be included
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].Version, "2.0.0");
}

#[test]
fn uses_private_token_header() {
    let server = MockHttpServer::empty();
    let releases_json = gitlab_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/releases?per_page=".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![("PRIVATE-TOKEN".into(), "glpat-xxxxxxxxxxxx".into())],
    });
    server.add_route(MockRoute {
        path_contains: "/api/url/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GitlabSource::new(
        &format!("{}/api/v4/projects/12345", server.url()),
        Some("glpat-xxxxxxxxxxxx".to_string()),
        false,
    );
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
}

#[test]
fn uses_direct_asset_url_without_token() {
    let server = MockHttpServer::empty();
    let releases_json = gitlab_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/releases?per_page=".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    // Without token, should hit /direct/ path (direct_asset_url)
    server.add_route(MockRoute {
        path_contains: "/direct/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GitlabSource::new(
        &format!("{}/api/v4/projects/12345", server.url()),
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
    let releases_json = gitlab_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/releases?per_page=".into(),
        response_code: 200,
        response_body: releases_json.into_bytes(),
        expected_headers: vec![],
    });
    // With token, should hit /api/url/ path (url field)
    server.add_route(MockRoute {
        path_contains: "/api/url/releases.stable.json".into(),
        response_code: 200,
        response_body: sample_feed_json().into_bytes(),
        expected_headers: vec![],
    });

    let source = GitlabSource::new(&format!("{}/api/v4/projects/12345", server.url()), Some("my-token".to_string()), false);
    let manifest = test_manifest();
    let feed = source.get_release_feed("stable", &manifest, "").unwrap();
    assert!(!feed.Assets.is_empty());
}

#[test]
fn download_entry_success() {
    let body = vec![0xDE, 0xAD, 0xBE, 0xEF];
    let server = MockHttpServer::empty();
    let releases_json = gitlab_releases_json(&server.url());

    server.add_route(MockRoute {
        path_contains: "/releases?per_page=".into(),
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

    let source = GitlabSource::new(&format!("{}/api/v4/projects/12345", server.url()), None, false);
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
