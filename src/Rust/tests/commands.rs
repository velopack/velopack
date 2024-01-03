#![allow(unused_imports)]

mod common;
use common::*;
use std::{fs, path::Path, path::PathBuf};
use tempfile::tempdir;
use velopack::*;

#[cfg(target_os = "windows")]
use winsafe::{self as w, co};

#[cfg(target_os = "windows")]
#[test]
pub fn test_install_apply_uninstall() {
    dialogs::set_silent(true);
    let fixtures = find_fixtures();

    let app_id = "AvaloniaCrossPlat";
    let pkg_name = "AvaloniaCrossPlat-1.0.11-win-full.nupkg";

    let startmenu = w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::StartMenu, co::KF::DONT_UNEXPAND, None).unwrap();
    let lnk_path = Path::new(&startmenu).join("Programs").join(format!("{}.lnk", app_id));
    if lnk_path.exists() {
        fs::remove_file(&lnk_path).unwrap();
    }

    let nupkg = fixtures.join(pkg_name);

    let tmp_dir = tempdir().unwrap();
    let tmp_buf = tmp_dir.path().to_path_buf();
    commands::install(Some(&nupkg), Some(&tmp_buf)).unwrap();

    assert!(lnk_path.exists());
    assert!(tmp_buf.join("Update.exe").exists());
    assert!(tmp_buf.join("current").join("AvaloniaCrossPlat.exe").exists());
    assert!(tmp_buf.join("current").join("sq.version").exists());

    let (root_dir, app) = shared::detect_manifest_from_update_path(&tmp_buf.join("Update.exe")).unwrap();
    assert_eq!(app_id, app.id);
    assert!(semver::Version::parse("1.0.11").unwrap() == app.version);

    let pkg_name_apply = "AvaloniaCrossPlat-1.0.15-win-full.nupkg";
    let nupkg_apply = fixtures.join(pkg_name_apply);
    commands::apply(&root_dir, &app, false, false, Some(&nupkg_apply), None, true).unwrap();

    let (root_dir, app) = shared::detect_manifest_from_update_path(&tmp_buf.join("Update.exe")).unwrap();
    assert!(semver::Version::parse("1.0.15").unwrap() == app.version);

    commands::uninstall(&root_dir, &app, false).unwrap();
    assert!(!tmp_buf.join("current").exists());
    assert!(tmp_buf.join(".dead").exists());
    assert!(!lnk_path.exists());
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
    let tmp_file = std::path::Path::new("temp.patch").to_path_buf();

    commands::patch(&old_file, &p1, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);

    commands::patch(&old_file, &p2, &tmp_file).unwrap();
    let tmp_sha1 = get_sha1(&tmp_file);
    fs::remove_file(&tmp_file).unwrap();
    assert_eq!(expected_sha1, tmp_sha1);
}
