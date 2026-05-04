use std::{fs, path::PathBuf};

use velopack::{locator::VelopackLocatorConfig, UpdateManager};

fn test_manifest_path() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("test")
        .join("fixtures")
        .join("Test.Squirrel-App.nuspec")
}

#[test]
fn new_default_uses_flow_source_without_explicit_source() {
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

    let manager = UpdateManager::new_default(None, Some(locator)).unwrap();

    assert_eq!(manager.get_app_id(), "Test.Squirrel-App");
    assert_eq!(manager.get_current_version_as_string(), "1.0.0");
}
