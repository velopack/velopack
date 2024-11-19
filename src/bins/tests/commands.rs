#![allow(unused_imports)]

mod common;
use common::*;
use std::{fs, path::Path, path::PathBuf};
use tempfile::tempdir;
use velopack_bins::*;

#[cfg(target_os = "windows")]
use winsafe::{self as w, co};
use velopack::bundle::load_bundle_from_file;
use velopack::locator::{auto_locate_app_manifest, LocationContext};

#[cfg(target_os = "windows")]
#[test]
pub fn test_install_apply_uninstall() {
    dialogs::set_silent(true);

    let fixtures = find_fixtures();

    let app_id = "AvaloniaCrossPlat";
    let pkg_name = "AvaloniaCrossPlat-1.0.11-win-full.nupkg";

    let start_menu = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None).unwrap();
    let start_menu = Path::new(&start_menu).join("Programs");
    let desktop = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::Desktop, co::KF::DONT_UNEXPAND, None).unwrap();
    let desktop = Path::new(&desktop);

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
    commands::install(&mut tmp_zip, (tmp_buf.clone(), false), None).unwrap();

    assert!(!lnk_desktop_1.exists()); // desktop is created during update
    assert!(lnk_start_1.exists());

    assert!(tmp_buf.join("Update.exe").exists());
    assert!(tmp_buf.join("current").join("AvaloniaCrossPlat.exe").exists());
    assert!(tmp_buf.join("current").join("sq.version").exists());

    let locator = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(tmp_buf.clone())).unwrap();
    assert_eq!(app_id, locator.get_manifest_id());
    assert_eq!(semver::Version::parse("1.0.11").unwrap(), locator.get_manifest_version());

    let pkg_name_apply = "AvaloniaCrossPlat-1.0.15-win-full.nupkg";
    let nupkg_apply = fixtures.join(pkg_name_apply);
    commands::apply(&locator, false, shared::OperationWait::NoWait, Some(&nupkg_apply), None, false).unwrap();

    // shortcuts are renamed, and desktop is created
    assert!(!lnk_desktop_1.exists());
    assert!(!lnk_start_1.exists());
    assert!(lnk_desktop_2.exists());
    assert!(lnk_start_2.exists());

    let locator = auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(tmp_buf.clone())).unwrap();
    assert_eq!(semver::Version::parse("1.0.15").unwrap(), locator.get_manifest_version());

    commands::uninstall(&locator, false).unwrap();
    assert!(!tmp_buf.join("current").exists());
    assert!(tmp_buf.join(".dead").exists());

    assert!(!lnk_desktop_1.exists());
    assert!(!lnk_start_1.exists());
    assert!(!lnk_desktop_2.exists());
    assert!(!lnk_start_2.exists());
}

#[cfg(target_os = "windows")]
#[test]
pub fn test_install_preserve_symlinks() {
    dialogs::set_silent(true);
    let fixtures = find_fixtures();
    let pkg_name = "Test.Squirrel-App-1.0.0-symlinks-full.nupkg";
    let nupkg = fixtures.join(pkg_name);

    let tmp_dir = tempdir().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    let mut tmp_zip = load_bundle_from_file(nupkg).unwrap();
    
    commands::install(&mut tmp_zip, (tmp_buf.clone(), false), None).unwrap();

    assert!(tmp_buf.join("current").join("actual").join("file.txt").exists());
    assert!(tmp_buf.join("current").join("other").join("syml").exists());
    assert!(tmp_buf.join("current").join("other").join("sym.txt").exists());
    assert!(tmp_buf.join("current").join("other").join("syml").join("file.txt").exists());

    assert_eq!("hello", fs::read_to_string(tmp_buf.join("current").join("actual").join("file.txt")).unwrap());
    assert_eq!("hello", fs::read_to_string(tmp_buf.join("current").join("other").join("sym.txt")).unwrap());
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

    velopack::delta::zstd_patch_single(&old_file, &p1, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);

    velopack::delta::zstd_patch_single(&old_file, &p2, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);
} 
