mod common;

use common::*;
use std::{fs, path::PathBuf, sync::mpsc};
use velopack::{locator::VelopackLocatorConfig, sources::HttpSource, UpdateInfo, UpdateManager, VelopackAsset};

fn test_manifest_path() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("test")
        .join("fixtures")
        .join("Test.Squirrel-App.nuspec")
}

#[test]
fn download_updates_reports_progress_while_downloading_deltas() {
    let temp_dir = tempfile::tempdir().unwrap();
    let root_dir = temp_dir.path().join("root");
    let packages_dir = root_dir.join("packages");
    let current_binary_dir = root_dir.join("current");
    let update_exe_path = root_dir.join("Update.exe");

    fs::create_dir_all(&packages_dir).unwrap();
    fs::create_dir_all(&current_binary_dir).unwrap();
    fs::write(&update_exe_path, []).unwrap();

    let locator = VelopackLocatorConfig {
        RootAppDir: root_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: test_manifest_path(),
        CurrentBinaryDir: current_binary_dir,
        IsPortable: true,
    };

    let size = 1024 * 1024;
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "-delta.nupkg".to_string(),
        response_code: 200,
        response_body: vec![0u8; size],
        expected_headers: vec![],
    });

    let asset = |file_name: &str, version: &str, asset_type: &str| VelopackAsset {
        PackageId: "Test.Squirrel-App".to_string(),
        Version: version.to_string(),
        Type: asset_type.to_string(),
        FileName: file_name.to_string(),
        Size: size as u64,
        ..Default::default()
    };
    let update = UpdateInfo {
        TargetFullRelease: asset("Test.Squirrel-App-2.0.0-full.nupkg", "2.0.0", "Full"),
        BaseRelease: Some(asset("Test.Squirrel-App-1.0.0-full.nupkg", "1.0.0", "Full")),
        DeltasToTarget: vec![asset("Test.Squirrel-App-2.0.0-delta.nupkg", "2.0.0", "Delta")],
        IsDowngrade: false,
    };

    let manager = UpdateManager::new(HttpSource::new(server.url()), None, Some(locator)).unwrap();
    let (sender, receiver) = mpsc::channel();
    // the delta fails its checksum once downloaded and the full package isn't served, so this
    // errors, and only the progress reported along the way matters here
    let _ = manager.download_updates(&update, Some(sender));

    // ends once every sender is dropped, including the forwarding thread's, which may outlive the call
    let progress: Vec<i16> = receiver.iter().collect();
    assert!(
        progress.iter().any(|p| *p > 0 && *p < 70),
        "no progress while downloading the delta: {:?}",
        progress
    );
}
