mod common;

use common::*;
use sha2::Digest;
use std::{fs, path::PathBuf};
use velopack::{locator::VelopackLocatorConfig, sources::HttpSource, UpdateInfo, UpdateManager, VelopackAsset};

fn test_manifest_path() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("test")
        .join("fixtures")
        .join("Test.Squirrel-App.nuspec")
}

/// Creates an installed app layout under `temp_dir` (`root/packages` is the packages dir).
fn test_locator(temp_dir: &std::path::Path) -> VelopackLocatorConfig {
    let root_dir = temp_dir.join("root");
    let packages_dir = root_dir.join("packages");
    let current_binary_dir = root_dir.join("current");
    let update_exe_path = root_dir.join("Update.exe");

    fs::create_dir_all(&packages_dir).unwrap();
    fs::create_dir_all(&current_binary_dir).unwrap();
    fs::write(&update_exe_path, []).unwrap();

    VelopackLocatorConfig {
        RootAppDir: root_dir,
        UpdateExePath: update_exe_path,
        PackagesDir: packages_dir,
        ManifestPath: test_manifest_path(),
        CurrentBinaryDir: current_binary_dir,
        IsPortable: true,
    }
}

/// Downloads a full update whose feed FileName is `file_name` from a mock server into a fresh app layout,
/// returning the temp dir holding that layout and the package bytes that were served.
fn download_full_update(file_name: &str) -> (tempfile::TempDir, Vec<u8>) {
    let temp_dir = tempfile::tempdir().unwrap();
    let locator = test_locator(temp_dir.path());

    let body: Vec<u8> = (0..4096u32).map(|i| (i % 251) as u8).collect();
    let server = MockHttpServer::empty();
    server.add_route(MockRoute {
        path_contains: "Test.Squirrel-App-2.0.0-full.nupkg".to_string(),
        response_code: 200,
        response_body: body.clone(),
        expected_headers: vec![],
    });

    let update = UpdateInfo {
        TargetFullRelease: VelopackAsset {
            PackageId: "Test.Squirrel-App".to_string(),
            Version: "2.0.0".to_string(),
            Type: "Full".to_string(),
            FileName: file_name.to_string(),
            SHA256: sha2::Sha256::digest(&body).iter().map(|b| format!("{:02X}", b)).collect(),
            Size: body.len() as u64,
            ..Default::default()
        },
        ..Default::default()
    };

    let manager = UpdateManager::new(HttpSource::new(server.url()), None, Some(locator)).unwrap();
    manager.download_updates(&update, None).unwrap();
    (temp_dir, body)
}

#[test]
fn download_updates_stores_subfolder_asset_in_packages_dir() {
    // the feed may point at packages in a sub-folder next to releases.{channel}.json (velopack/velopack#596)
    let (temp_dir, body) = download_full_update("2.0.0/Test.Squirrel-App-2.0.0-full.nupkg");
    let packages_dir = temp_dir.path().join("root").join("packages");
    assert_eq!(fs::read(packages_dir.join("Test.Squirrel-App-2.0.0-full.nupkg")).unwrap(), body);
    assert!(!packages_dir.join("2.0.0").exists());
}

#[test]
fn download_updates_keeps_traversing_asset_names_inside_packages_dir() {
    let (temp_dir, body) = download_full_update("../../Test.Squirrel-App-2.0.0-full.nupkg");
    let packages_dir = temp_dir.path().join("root").join("packages");
    assert_eq!(fs::read(packages_dir.join("Test.Squirrel-App-2.0.0-full.nupkg")).unwrap(), body);
    // packages/../../ is the temp dir itself, nothing may be written there
    assert!(!temp_dir.path().join("Test.Squirrel-App-2.0.0-full.nupkg").exists());
}
