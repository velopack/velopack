#![allow(unused_imports)]

mod common;
use common::*;
use std::hint::assert_unchecked;
use std::sync::Mutex;
use std::{fs, path::Path, path::PathBuf};
use tempfile::tempdir;

use velopack::bundle::load_bundle_from_file;
use velopack::locator::{auto_locate_app_manifest, LocationContext};
use velopack_bins::*;

// Tests that call commands::install create shortcuts in shared OS directories (Start Menu, Desktop).
// This mutex prevents them from running in parallel and interfering with each other.
#[cfg(target_os = "windows")]
static SHORTCUT_MUTEX: Mutex<()> = Mutex::new(());

#[cfg(target_os = "windows")]
#[test]
pub fn test_install_apply_uninstall() {
    use velopack_bins::windows::known_path;

    let _lock = SHORTCUT_MUTEX.lock().unwrap();
    dialogs::set_silent(true);

    let fixtures = find_fixtures();

    let app_id = "AvaloniaCrossPlat";
    let pkg_name = "AvaloniaCrossPlat-1.0.11-win-full.nupkg";

    let start_menu = PathBuf::from(known_path::get_start_menu().unwrap());
    let desktop = PathBuf::from(known_path::get_user_desktop().unwrap());

    let lnk_start_1 = start_menu.join(format!("{}.lnk", app_id));
    let lnk_desktop_1 = desktop.join(format!("{}.lnk", app_id));
    let lnk_start_2 = start_menu.join(format!("{}.lnk", "AvaloniaCross Updated"));
    let lnk_desktop_2 = desktop.join(format!("{}.lnk", "AvaloniaCross Updated"));
    let _ = fs::remove_file(&lnk_start_1);
    let _ = fs::remove_file(&lnk_desktop_1);
    let _ = fs::remove_file(&lnk_start_2);
    let _ = fs::remove_file(&lnk_desktop_2);

    let nupkg = fixtures.join(pkg_name);

    let tmp_dir = tempdir().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let mut tmp_zip = load_bundle_from_file(nupkg).unwrap();
    commands::install(&mut tmp_zip, Some(&tmp_buf), None).unwrap();

    assert!(!lnk_desktop_1.exists()); // desktop is created during update
    assert!(lnk_start_1.exists());

    assert!(tmp_buf.join("Update.exe").exists());
    assert!(tmp_buf.join("current").join("AvaloniaCrossPlat.exe").exists());
    assert!(tmp_buf.join("current").join("sq.version").exists());

    let locator = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(tmp_buf.clone(), None)).unwrap();
    assert_eq!(app_id, locator.get_manifest_id());
    assert_eq!(semver::Version::parse("1.0.11").unwrap(), locator.get_manifest_version());

    let pkg_name_apply = "AvaloniaCrossPlat-1.0.15-win-full.nupkg";
    let nupkg_apply = fixtures.join(pkg_name_apply);
    commands::apply(
        &locator,
        false,
        shared::OperationWait::NoWait,
        Some(&nupkg_apply),
        None,
        commands::HookRunMode::None,
    )
    .unwrap();

    // shortcuts are renamed, and desktop is created
    assert!(!lnk_desktop_1.exists());
    assert!(!lnk_start_1.exists());
    assert!(lnk_desktop_2.exists());
    assert!(lnk_start_2.exists());

    let locator = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(tmp_buf.clone(), None)).unwrap();
    assert_eq!(semver::Version::parse("1.0.15").unwrap(), locator.get_manifest_version());

    commands::uninstall(&locator, false).unwrap();
    assert!(!tmp_buf.join("current").exists());

    assert!(!lnk_desktop_1.exists());
    assert!(!lnk_start_1.exists());
    assert!(!lnk_desktop_2.exists());
    assert!(!lnk_start_2.exists());
}

#[cfg(target_os = "windows")]
#[test]
pub fn test_install_preserve_symlinks() {
    let _lock = SHORTCUT_MUTEX.lock().unwrap();
    dialogs::set_silent(true);
    let fixtures = find_fixtures();
    let pkg_name = "Test.Squirrel-App-1.0.0-symlinks-full.nupkg";
    let nupkg = fixtures.join(pkg_name);

    let tmp_dir = tempdir().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let mut tmp_zip = load_bundle_from_file(nupkg).unwrap();

    commands::install(&mut tmp_zip, Some(&tmp_buf), None).unwrap();

    assert!(tmp_buf.join("current").join("actual").join("file.txt").exists());
    assert!(tmp_buf.join("current").join("other").join("syml").exists());
    assert!(tmp_buf.join("current").join("other").join("sym.txt").exists());
    assert!(tmp_buf.join("current").join("other").join("syml").join("file.txt").exists());

    assert_eq!(
        "hello",
        fs::read_to_string(tmp_buf.join("current").join("actual").join("file.txt")).unwrap()
    );
    assert_eq!(
        "hello",
        fs::read_to_string(tmp_buf.join("current").join("other").join("sym.txt")).unwrap()
    );
}

#[test]
pub fn test_patch_apply() {
    dialogs::set_silent(true);
    let fixtures = find_fixtures();

    let old_file = fixtures.join("obs29.1.2.dll");
    let new_file = fixtures.join("obs30.0.2.dll");
    let p1 = fixtures.join("obs-size.patch");
    let p2 = fixtures.join("obs-speed.patch");

    fn get_sha1(file: &PathBuf) -> String {
        let file_bytes = fs::read(file).unwrap();
        let mut sha1 = sha1_smol::Sha1::new();
        sha1.update(&file_bytes);
        sha1.digest().to_string()
    }

    let expected_sha1 = get_sha1(&new_file);
    let tmp_file = Path::new("temp.patch").to_path_buf();

    velopack_bins::commands::zstd_patch_single(&old_file, &p1, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);

    velopack_bins::commands::zstd_patch_single(&old_file, &p2, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);
}

#[cfg(target_os = "windows")]
#[test]
pub fn test_apply_corrupt_package_is_deleted() {
    let _lock = SHORTCUT_MUTEX.lock().unwrap();
    dialogs::set_silent(true);
    let fixtures = find_fixtures();

    // 1. Install a valid app so we have a working locator
    let nupkg = fixtures.join("AvaloniaCrossPlat-1.0.11-win-full.nupkg");
    let tmp_dir = tempdir().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let mut tmp_zip = load_bundle_from_file(nupkg).unwrap();
    commands::install(&mut tmp_zip, Some(&tmp_buf), None).unwrap();

    let locator = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(tmp_buf.clone(), None)).unwrap();

    // 2. Create a corrupt package file (not a valid zip)
    let corrupt_pkg = tmp_buf.join("packages").join("corrupt-1.0.0-full.nupkg");
    fs::create_dir_all(corrupt_pkg.parent().unwrap()).unwrap();
    fs::write(&corrupt_pkg, b"THIS_IS_NOT_A_VALID_ZIP_FILE").unwrap();
    assert!(corrupt_pkg.exists());

    // 3. Apply should fail because the package is corrupt
    let result = commands::apply(
        &locator,
        false,
        shared::OperationWait::NoWait,
        Some(&corrupt_pkg),
        None,
        commands::HookRunMode::None,
    );
    assert!(result.is_err(), "Apply should fail with a corrupt package");

    // 4. The corrupt package should have been deleted to prevent an update loop
    assert!(!corrupt_pkg.exists(), "Corrupt package should be deleted after failed apply");
}

#[cfg(target_os = "windows")]
#[test]
pub fn test_apply_locked_dir_does_not_delete_package() {
    let _lock = SHORTCUT_MUTEX.lock().unwrap();
    dialogs::set_silent(true);
    let fixtures = find_fixtures();

    // 1. Install a valid app so we have a working locator
    let nupkg = fixtures.join("AvaloniaCrossPlat-1.0.11-win-full.nupkg");
    let tmp_dir = tempdir().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let mut tmp_zip = load_bundle_from_file(nupkg).unwrap();
    commands::install(&mut tmp_zip, Some(&tmp_buf), None).unwrap();

    let locator = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(tmp_buf.clone(), None)).unwrap();

    // 2. Copy the update package to a temp location (don't use the fixture directly)
    let update_pkg = tmp_buf.join("packages").join("AvaloniaCrossPlat-1.0.15-win-full.nupkg");
    fs::create_dir_all(update_pkg.parent().unwrap()).unwrap();
    fs::copy(fixtures.join("AvaloniaCrossPlat-1.0.15-win-full.nupkg"), &update_pkg).unwrap();
    assert!(update_pkg.exists());

    // 3. Hold a file handle open in the current dir to prevent the rename during install phase.
    //    On Windows, an open handle without FILE_SHARE_DELETE prevents the parent directory rename.
    let locked_file = tmp_buf.join("current").join("AvaloniaCrossPlat.exe");
    let _handle = fs::File::open(&locked_file).expect("should be able to open file to lock it");

    // 4. Apply should fail because the directory rename is blocked by the locked file
    let result = commands::apply(
        &locator,
        false,
        shared::OperationWait::NoWait,
        Some(&update_pkg),
        None,
        commands::HookRunMode::None,
    );
    assert!(result.is_err(), "Apply should fail when current dir is locked");

    // 5. The package should NOT be deleted — the failure was in the install phase, not staging
    assert!(update_pkg.exists(), "Valid package should be preserved when install phase fails");
}

#[test]
pub fn test_delta_apply_legacy() {
    dialogs::set_silent(true);
    let fixtures = find_fixtures();
    let base = fixtures.join("Clowd-3.4.287-full.nupkg");
    let d1 = fixtures.join("Clowd-3.4.288-delta.nupkg");
    let d2 = fixtures.join("Clowd-3.4.291-delta.nupkg");
    let d3 = fixtures.join("Clowd-3.4.292-delta.nupkg");
    let d4 = fixtures.join("Clowd-3.4.293-delta.nupkg");

    let deltas = vec![&d1, &d2, &d3, &d4];

    let tmp_dir = tempdir().unwrap();
    let temp_output = tmp_dir.path().join("Clowd-3.4.293-full.nupkg");
    commands::delta(&base, deltas, tmp_dir.path(), &temp_output).unwrap();

    let mut bundle = load_bundle_from_file(temp_output).unwrap();
    let manifest = bundle.read_manifest().unwrap();

    assert_eq!(manifest.id, "Clowd");
    assert_eq!(manifest.version, semver::Version::parse("3.4.293").unwrap());

    #[cfg(not(target_os = "linux"))]
    {
        let extract_dir = tmp_dir.path().join("_extracted");
        bundle.extract_lib_contents_to_path(&extract_dir, |_| {}).unwrap();

        let extracted = extract_dir.join("Clowd.dll");
        assert!(extracted.exists());
    }
}
