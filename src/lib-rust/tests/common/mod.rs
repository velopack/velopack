#[allow(dead_code, unused_imports)]
pub mod mock_server;

#[allow(unused_imports)]
pub use mock_server::{MockHttpServer, MockRoute};

use semver::Version;
use velopack::bundle::Manifest;
use velopack::VelopackAsset;

#[allow(dead_code)]
pub fn test_manifest() -> Manifest {
    Manifest {
        id: "TestApp".to_string(),
        version: Version::new(1, 0, 0),
        ..Default::default()
    }
}

#[allow(dead_code)]
pub fn sample_feed_json() -> String {
    serde_json::json!({
        "Assets": [{
            "PackageId": "TestApp",
            "Version": "2.0.0",
            "Type": "Full",
            "FileName": "TestApp-2.0.0-full.nupkg",
            "SHA1": "abc123",
            "SHA256": "def456",
            "Size": 1048576,
            "NotesMarkdown": "# v2",
            "NotesHtml": "<h1>v2</h1>"
        }]
    })
    .to_string()
}

#[allow(dead_code)]
pub fn sample_asset() -> VelopackAsset {
    VelopackAsset {
        PackageId: "TestApp".to_string(),
        Version: "2.0.0".to_string(),
        Type: "Full".to_string(),
        FileName: "TestApp-2.0.0-full.nupkg".to_string(),
        SHA1: "abc123".to_string(),
        SHA256: "def456".to_string(),
        Size: 1048576,
        NotesMarkdown: "# v2".to_string(),
        NotesHtml: "<h1>v2</h1>".to_string(),
    }
}
