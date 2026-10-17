//! Tests for the `assemble` and `apply-staged` commands and the swap recovery (spec §7, §12.4).
#![allow(unused_imports, dead_code)]

mod common;
use common::index_writer::*;
use common::*;

use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use tempfile::tempdir;
use velopack::chunks::{self, staging, AssembleContext, ChunkBlob, StagedLayout};
use velopack::locator::{auto_locate_app_manifest, LocationContext, VelopackLocator};
use velopack::lockfile::LockFile;
use velopack::VelopackAsset;
use velopack_bins::*;

const APP_ID: &str = "StagedTestApp";

fn nuspec(version: &str) -> Vec<u8> {
    format!(
        r#"<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
<metadata>
<id>{}</id>
<title>Staged Test App</title>
<description>Staged Test App</description>
<authors>test</authors>
<version>{}</version>
<mainExe>app.exe</mainExe>
<os>win</os>
<rid>win-x64</rid>
<machineArchitecture>x64</machineArchitecture>
</metadata>
</package>"#,
        APP_ID, version
    )
    .into_bytes()
}

/// A harmless main executable that exits immediately.
fn main_exe_bytes() -> Vec<u8> {
    fs::read(PathBuf::from(std::env::var("SystemRoot").unwrap()).join("System32").join("whoami.exe")).unwrap()
}

fn update_exe_bytes() -> Vec<u8> {
    fs::read(env!("CARGO_BIN_EXE_update")).unwrap()
}

fn v1_files() -> Vec<TestFile> {
    vec![
        TestFile::file("sq.version", nuspec("1.0.0")),
        TestFile::file("app.exe", main_exe_bytes()),
        TestFile::file("data/x.bin", random_bytes(1, 200_000)),
        TestFile::file("lib/readme.txt", b"v1 readme".to_vec()),
        TestFile::updater(random_bytes(2, 50_000)),
        TestFile::stub("App", random_bytes(3, 2000)),
    ]
}

fn v2_files(updater: Vec<u8>) -> Vec<TestFile> {
    let mut x = random_bytes(1, 200_000);
    x.splice(100_000..100_000, random_bytes(4, 777));
    vec![
        TestFile::file("sq.version", nuspec("2.0.0")),
        TestFile::file("app.exe", main_exe_bytes()),
        TestFile::file("data/x.bin", x),
        TestFile::file("data/new.bin", random_bytes(5, 50_000)),
        TestFile::updater(updater),
        TestFile::stub("App", random_bytes(6, 2000)),
    ]
}

fn opts() -> TestIndexOptions {
    TestIndexOptions {
        chunk_size: 32 * 1024,
        ..Default::default()
    }
}

/// Installs the synthetic v1 app at `root` (portable, so no shortcuts or registry are touched).
fn install_v1(root: &Path) -> BuiltIndex {
    let built = build_index(&v1_files(), &opts());
    write_installed_view(root, &v1_files(), &built);
    fs::write(root.join(".portable"), b"").unwrap();
    built
}

fn asset_for(built: &BuiltIndex, version: &str) -> VelopackAsset {
    VelopackAsset {
        PackageId: APP_ID.to_string(),
        Version: version.to_string(),
        Type: "Full".to_string(),
        FileName: format!("{}-{}-full.nupkg", APP_ID, version),
        Size: 10_000_000,
        ChunkIndexOffset: 9_000_000,
        ChunkIndexLength: built.trailer().len() as u64,
        ChunkIndexSHA256: built.digest_hex(),
        ..Default::default()
    }
}

/// Writes index.bin + asset.json + a blob holding every chunk under `packages/staged/<id>` (no assembly).
fn prepare_staged(packages: &Path, files: &[TestFile], version: &str, with_chunks: bool) -> (StagedLayout, BuiltIndex) {
    let built = build_index(files, &opts());
    let layout = StagedLayout::new(packages, &built.staged_id());
    let _ = fs::remove_dir_all(&layout.dir);
    fs::create_dir_all(&layout.dir).unwrap();
    staging::write_asset_json(&layout, &asset_for(&built, version)).unwrap();
    fs::write(layout.index_bin(), built.trailer()).unwrap();
    let mut blob = ChunkBlob::open(layout.chunks_bin()).unwrap();
    if with_chunks {
        for c in &built.chunks {
            blob.append(&c.sha256, &c.plain).unwrap();
        }
    }
    blob.flush().unwrap();
    (layout, built)
}

/// Prepares and assembles v2 in-process, returning a ready staged dir.
fn stage_v2(root: &Path, packages: &Path, updater: Vec<u8>) -> (StagedLayout, BuiltIndex) {
    let (layout, built) = prepare_staged(packages, &v2_files(updater), "2.0.0", true);
    let ctx = AssembleContext {
        staged_dir: layout.dir.clone(),
        current_dir: root.join("current"),
        updater_local_source: root.join("Update.exe"),
        root_dir: root.to_path_buf(),
    };
    chunks::assemble(&ctx, &|_| {}).unwrap();
    assert!(staging::read_ready_marker(&layout.dir).is_some());
    (layout, built)
}

fn locator_for(root: &Path, packages: Option<PathBuf>) -> VelopackLocator {
    auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(root.to_path_buf(), packages)).unwrap()
}

fn apply_in_process(locator: &VelopackLocator, staged: &Path) -> anyhow::Result<VelopackLocator> {
    commands::apply_staged(
        locator,
        staged,
        false,
        shared::OperationWait::NoWait,
        None,
        commands::HookRunMode::None,
        &commands::ElevationFlags::default(),
    )
}

fn apply_as_elevated_child(locator: &VelopackLocator, staged: &Path) -> anyhow::Result<VelopackLocator> {
    let flags = commands::ElevationFlags {
        elevated_child: true,
        ..Default::default()
    };
    commands::apply_staged(
        locator,
        staged,
        false,
        shared::OperationWait::NoWait,
        None,
        commands::HookRunMode::None,
        &flags,
    )
}

fn assert_current_is(root: &Path, files: &[TestFile]) {
    for f in files.iter().filter(|f| f.disposition == 0) {
        assert_eq!(fs::read(root.join("current").join(&f.path)).unwrap(), f.data, "{}", f.path);
    }
}

fn leftovers(root: &Path) -> Vec<String> {
    fs::read_dir(root)
        .unwrap()
        .filter_map(|e| e.ok())
        .map(|e| e.file_name().to_string_lossy().to_string())
        .filter(|n| n.starts_with("current.old-") || n.starts_with("current.new-"))
        .collect()
}

fn run_update(root: &Path, args: &[&str]) -> (i32, String) {
    let log = root.join("update-test.log");
    let output = Command::new(env!("CARGO_BIN_EXE_update"))
        .arg("--silent")
        .args(args)
        .arg("--rootDir")
        .arg(root)
        .arg("--log")
        .arg(&log)
        .output()
        .unwrap();
    let stdout = String::from_utf8_lossy(&output.stdout).to_string();
    (output.status.code().unwrap_or(-1), stdout)
}

fn protocol_lines<'a>(stdout: &'a str, kind: &str) -> Vec<&'a str> {
    stdout
        .lines()
        .filter(|l| l.starts_with(&format!("VPK:{} ", kind)))
        .map(|l| l.split_once(' ').unwrap().1)
        .collect()
}

#[cfg(target_os = "windows")]
#[test]
fn assemble_cli_protocol() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let packages = root.join("packages");
    let (layout, built) = prepare_staged(&packages, &v2_files(random_bytes(7, 60_000)), "2.0.0", true);

    let pid = std::process::id().to_string();
    let (code, stdout) = run_update(&root, &["assemble", "--staged", layout.dir.to_str().unwrap(), "--parentPid", &pid]);
    assert_eq!(code, 0, "stdout:\n{}", stdout);
    let progress: Vec<i32> = protocol_lines(&stdout, "PROGRESS").iter().map(|p| p.parse().unwrap()).collect();
    assert!(!progress.is_empty());
    assert!(progress.windows(2).all(|w| w[0] < w[1]), "monotonic: {:?}", progress);
    assert_eq!(*progress.last().unwrap(), 100);
    assert!(protocol_lines(&stdout, "ERROR").is_empty());
    assert!(protocol_lines(&stdout, "MISSING").is_empty());
    // a VPK: prefix is only recognised at the start of a line
    assert!(stdout.lines().filter(|l| l.contains("VPK:")).all(|l| l.starts_with("VPK:")));

    let marker = staging::read_ready_marker(&layout.dir).unwrap();
    assert_eq!(marker.StagedId, built.staged_id());
    assert_eq!(marker.Asset.Version, "2.0.0");
    assert_eq!(fs::read(layout.current_dir().join("sq.chunks")).unwrap(), built.a);
    assert_eq!(fs::read(layout.updater()).unwrap(), random_bytes(7, 60_000));

    // re-running resumes and succeeds again
    let (code, stdout) = run_update(&root, &["assemble", "--staged", layout.dir.to_str().unwrap(), "--parentPid", &pid]);
    assert_eq!(code, 0, "stdout:\n{}", stdout);
    assert_eq!(protocol_lines(&stdout, "PROGRESS").last(), Some(&"100"));
}

#[cfg(target_os = "windows")]
#[test]
fn assemble_exit_codes() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let packages = root.join("packages");
    let pid = std::process::id().to_string();

    // 2: missing --parentPid / --staged
    let (layout, _) = prepare_staged(&packages, &v2_files(random_bytes(7, 60_000)), "2.0.0", true);
    let (code, stdout) = run_update(&root, &["assemble", "--staged", layout.dir.to_str().unwrap()]);
    assert_eq!(code, 2, "{}", stdout);
    assert_eq!(protocol_lines(&stdout, "ERROR").len(), 1);
    assert!(stdout.trim_end().lines().last().unwrap().starts_with("VPK:ERROR "));
    assert!(protocol_lines(&stdout, "PROGRESS").is_empty());
    let (code, stdout) = run_update(&root, &["assemble", "--parentPid", &pid]);
    assert_eq!(code, 2, "{}", stdout);
    assert!(!layout.ready_json().exists());

    // 2: invalid input (staged dir missing / digest mismatch)
    let (code, stdout) = run_update(&root, &["assemble", "--staged", root.join("nope").to_str().unwrap(), "--parentPid", &pid]);
    assert_eq!(code, 2, "{}", stdout);
    assert!(stdout.trim_end().lines().last().unwrap().starts_with("VPK:ERROR "));
    fs::write(layout.index_bin(), b"VPKA garbage").unwrap();
    let (code, stdout) = run_update(&root, &["assemble", "--staged", layout.dir.to_str().unwrap(), "--parentPid", &pid]);
    assert_eq!(code, 2, "{}", stdout);

    // 3: chunks missing (empty blob, new file not available locally)
    let (layout, built) = prepare_staged(&packages, &v2_files(random_bytes(7, 60_000)), "2.0.0", false);
    let (code, stdout) = run_update(&root, &["assemble", "--staged", layout.dir.to_str().unwrap(), "--parentPid", &pid]);
    assert_eq!(code, 3, "{}", stdout);
    let missing = staging::read_missing_bin(&layout.missing_bin()).unwrap();
    assert_eq!(protocol_lines(&stdout, "MISSING"), vec![missing.len().to_string().as_str()]);
    assert!(stdout.trim_end().lines().last().unwrap().starts_with("VPK:ERROR "));
    assert_ne!(protocol_lines(&stdout, "PROGRESS").last(), Some(&"100"));
    assert!(!layout.ready_json().exists());
    let have: HashSet<[u8; 32]> = build_index(&v1_files(), &opts()).chunk_hashes().into_iter().collect();
    let expected: HashSet<[u8; 32]> = built.chunks.iter().map(|c| c.sha256).filter(|h| !have.contains(h)).collect();
    assert_eq!(missing.into_iter().collect::<HashSet<_>>(), expected);

    // 4: IO error (the staged current/ path is a file)
    let (layout, _) = prepare_staged(&packages, &v2_files(random_bytes(8, 60_000)), "2.0.0", true);
    fs::write(layout.current_dir(), b"not a dir").unwrap();
    let (code, stdout) = run_update(&root, &["assemble", "--staged", layout.dir.to_str().unwrap(), "--parentPid", &pid]);
    assert_eq!(code, 4, "{}", stdout);
    assert!(stdout.trim_end().lines().last().unwrap().starts_with("VPK:ERROR "));
}

#[cfg(target_os = "windows")]
#[test]
fn assemble_dies_with_parent() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let (layout, _) = prepare_staged(&root.join("packages"), &v2_files(random_bytes(7, 60_000)), "2.0.0", true);

    // a parent that has already exited
    let dead_pid = {
        let mut c = Command::new("cmd").args(["/c", "exit", "0"]).spawn().unwrap();
        let id = c.id();
        c.wait().unwrap();
        id
    };
    let start = std::time::Instant::now();
    let (code, stdout) = run_update(
        &root,
        &["assemble", "--staged", layout.dir.to_str().unwrap(), "--parentPid", &dead_pid.to_string()],
    );
    assert_eq!(code, 1, "{}", stdout);
    assert!(start.elapsed().as_secs() < 20, "exits promptly");
    assert_ne!(protocol_lines(&stdout, "PROGRESS").last(), Some(&"100"));
    assert!(!layout.ready_json().exists());
}

#[cfg(target_os = "windows")]
#[test]
#[serial_test::file_serial(shortcuts)]
fn apply_staged_success() {
    use velopack::bundle::load_bundle_from_file;
    use velopack_bins::windows::known_path;
    dialogs::set_silent(true);
    let fixtures = find_fixtures();

    let app_id = "AvaloniaCrossPlat";
    let start_menu = PathBuf::from(known_path::get_start_menu().unwrap());
    let desktop = PathBuf::from(known_path::get_user_desktop().unwrap());
    let lnk_start_1 = start_menu.join(format!("{}.lnk", app_id));
    let lnk_start_2 = start_menu.join("AvaloniaCross Updated.lnk");
    let lnk_desktop_2 = desktop.join("AvaloniaCross Updated.lnk");
    let _ = fs::remove_file(&lnk_start_1);
    let _ = fs::remove_file(&lnk_start_2);
    let _ = fs::remove_file(&lnk_desktop_2);

    // install v1 from the real fixture
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    let mut v1_zip = load_bundle_from_file(fixtures.join("AvaloniaCrossPlat-1.0.11-win-full.nupkg")).unwrap();
    commands::install(&mut v1_zip, Some(&root), None, None).unwrap();
    assert!(lnk_start_1.exists());
    let locator = locator_for(&root, None);
    assert_eq!(locator.get_manifest_version(), semver::Version::parse("1.0.11").unwrap());

    // the v2 tree is the 1.0.15 fixture; its updater is the real update.exe under test, so the swap
    // is performed by the target's own updater exactly as in production
    let v2_tree = tmp.path().join("v2tree");
    let v2_zip = load_bundle_from_file(fixtures.join("AvaloniaCrossPlat-1.0.15-win-full.nupkg")).unwrap();
    v2_zip.extract_lib_contents_to_path(&v2_tree, |_| {}).unwrap();
    let mut files = files_from_dir(&v2_tree);
    files.push(TestFile::updater(update_exe_bytes()));
    files.push(TestFile::stub("AvaloniaCrossPlat", random_bytes(1, 3000)));
    let (layout, built) = prepare_staged(&root.join("packages"), &files, "1.0.15", true);
    let ctx = AssembleContext {
        staged_dir: layout.dir.clone(),
        current_dir: root.join("current"),
        updater_local_source: root.join("Update.exe"),
        root_dir: root.clone(),
    };
    chunks::assemble(&ctx, &|_| {}).unwrap();
    fs::write(root.join("packages").join("AvaloniaCrossPlat-1.0.15-win-full.nupkg"), b"stale package").unwrap();
    fs::create_dir_all(root.join("packages").join("staged").join("00000000000000000000000000000000")).unwrap();

    let (code, stdout) = run_update(&root, &["apply-staged", "--staged", layout.dir.to_str().unwrap(), "--norestart"]);
    assert_eq!(
        code,
        0,
        "stdout:\n{}\nlog:\n{}",
        stdout,
        fs::read_to_string(root.join("update-test.log")).unwrap_or_default()
    );

    // current == v2, root updater == sidecar, stubs synced
    let new_locator = locator_for(&root, None);
    assert_eq!(new_locator.get_manifest_version(), semver::Version::parse("1.0.15").unwrap());
    for f in files.iter().filter(|f| f.disposition == 0) {
        assert_eq!(fs::read(root.join("current").join(&f.path)).unwrap(), f.data, "{}", f.path);
    }
    assert_eq!(fs::read(root.join("current").join("sq.chunks")).unwrap(), built.a);
    assert_eq!(fs::read(root.join("Update.exe")).unwrap(), update_exe_bytes());
    assert_eq!(fs::read(root.join("AvaloniaCrossPlat.exe")).unwrap(), random_bytes(1, 3000));
    // staged dirs, backup and packages gone
    assert!(!layout.dir.exists());
    assert!(!root.join("packages").join("staged").join("00000000000000000000000000000000").exists());
    assert!(!root.join("packages").join("AvaloniaCrossPlat-1.0.15-win-full.nupkg").exists());
    assert!(leftovers(&root).is_empty(), "{:?}", leftovers(&root));
    // shortcuts renamed and registry updated
    assert!(!lnk_start_1.exists());
    assert!(lnk_start_2.exists());
    assert!(lnk_desktop_2.exists());
    let hkcu = winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER);
    let key = hkcu
        .open_subkey(format!("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{}", app_id))
        .unwrap();
    let display_version: String = key.get_value("DisplayVersion").unwrap();
    assert_eq!(display_version, "1.0.15");

    commands::uninstall(&new_locator, false).unwrap();
    assert!(!lnk_start_2.exists());
    assert!(!lnk_desktop_2.exists());
}

#[cfg(target_os = "windows")]
#[test]
fn apply_staged_locked_file_restores_old() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let (layout, _) = stage_v2(&root, &root.join("packages"), random_bytes(7, 60_000));
    let locator = locator_for(&root, None);

    // an open handle inside current/ blocks the first rename
    let _handle = fs::File::open(root.join("current").join("data").join("x.bin")).unwrap();
    let result = apply_in_process(&locator, &layout.dir);
    assert!(result.is_err(), "apply must fail while current/ is locked");

    assert_current_is(&root, &v1_files());
    assert!(leftovers(&root).is_empty(), "{:?}", leftovers(&root));
    assert!(layout.ready_json().exists(), "a swap failure keeps the staged tree for a retry");
    drop(_handle);

    // the retry succeeds once the file is released
    apply_in_process(&locator, &layout.dir).unwrap();
    assert_current_is(&root, &v2_files(random_bytes(7, 60_000)));
    assert!(!layout.dir.exists());
    assert!(leftovers(&root).is_empty());
}

#[cfg(target_os = "windows")]
#[test]
fn apply_staged_rejects_bad_marker() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let (layout, _) = stage_v2(&root, &root.join("packages"), random_bytes(7, 60_000));
    let locator = locator_for(&root, None);

    let marker = staging::read_ready_marker(&layout.dir).unwrap();
    let json = fs::read_to_string(layout.ready_json())
        .unwrap()
        .replace(&marker.UpdaterSHA256, &"00".repeat(32));
    fs::write(layout.ready_json(), json).unwrap();
    assert_ne!(staging::read_ready_marker(&layout.dir).unwrap().UpdaterSHA256, marker.UpdaterSHA256);
    assert!(apply_in_process(&locator, &layout.dir).is_err());
    assert!(!layout.dir.exists(), "an invalid staged dir is deleted to prevent an update loop");
    assert_current_is(&root, &v1_files());
    assert!(leftovers(&root).is_empty());

    // a missing marker, a tampered file and a tampered sq.chunks are rejected the same way
    for tamper in 0..3 {
        let (layout, _) = stage_v2(&root, &root.join("packages"), random_bytes(7, 60_000));
        match tamper {
            0 => fs::remove_file(layout.ready_json()).unwrap(),
            1 => fs::write(layout.current_dir().join("data").join("new.bin"), b"short").unwrap(),
            _ => fs::write(layout.current_dir().join("sq.chunks"), b"VPKA").unwrap(),
        }
        assert!(apply_in_process(&locator, &layout.dir).is_err(), "tamper {}", tamper);
        assert!(!layout.dir.exists(), "tamper {}", tamper);
        assert_current_is(&root, &v1_files());
    }
}

#[cfg(target_os = "windows")]
#[test]
fn apply_staged_rejects_id_mismatch() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let (layout, _) = stage_v2(&root, &root.join("packages"), random_bytes(7, 60_000));
    let locator = locator_for(&root, None);

    let renamed = layout.dir.with_file_name("0123456789abcdef0123456789abcdef");
    fs::rename(&layout.dir, &renamed).unwrap();
    assert!(apply_in_process(&locator, &renamed).is_err());
    assert!(!renamed.exists());
    assert_current_is(&root, &v1_files());

    // a marker whose IndexSHA256 does not match index.bin
    let (layout, _) = stage_v2(&root, &root.join("packages"), random_bytes(7, 60_000));
    let mut t = fs::read(layout.index_bin()).unwrap();
    t[8] ^= 1; // flags: still a valid index, different digest
    fs::write(layout.index_bin(), &t).unwrap();
    assert!(apply_in_process(&locator, &layout.dir).is_err());
    assert!(!layout.dir.exists());
    assert_current_is(&root, &v1_files());
}

/// The elevated child of `apply-staged` runs as admin while the packages directory belongs to the
/// unelevated user: it must never delete anything there (nor take the packages lock, which the parent holds).
#[cfg(target_os = "windows")]
#[test]
fn elevated_child_never_touches_the_packages_dir() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let packages = root.join("packages");
    let (layout, _) = stage_v2(&root, &packages, random_bytes(7, 60_000));
    let other_staged = packages.join("staged").join("0".repeat(32));
    fs::create_dir_all(&other_staged).unwrap();
    let package = packages.join("StagedTestApp-9.0.0-full.nupkg");
    fs::write(&package, b"package").unwrap();
    let locator = locator_for(&root, None);

    // the parent holds the packages lock while the child runs
    let parent_lock = locator.try_get_exclusive_lock().unwrap();

    // a tree that fails validation in the child is left for the unelevated parent
    fs::rename(layout.ready_json(), layout.dir.join("ready.json.bak")).unwrap();
    assert!(apply_as_elevated_child(&locator, &layout.dir).is_err());
    assert!(layout.dir.exists(), "the elevated child never deletes a staged directory");
    fs::rename(layout.dir.join("ready.json.bak"), layout.ready_json()).unwrap();

    // a successful swap leaves every staged dir, package and temp dir to the parent
    apply_as_elevated_child(&locator, &layout.dir).unwrap();
    assert_current_is(&root, &v2_files(random_bytes(7, 60_000)));
    assert!(leftovers(&root).is_empty(), "{:?}", leftovers(&root));
    assert!(layout.dir.exists());
    assert!(other_staged.exists());
    assert!(package.exists());
    drop(parent_lock);
}

fn make_backup(root: &Path, name: &str, version: &str) {
    let dir = root.join(name);
    fs::create_dir_all(dir.join("data")).unwrap();
    fs::write(dir.join("sq.version"), nuspec(version)).unwrap();
    fs::write(dir.join("app.exe"), main_exe_bytes()).unwrap();
    fs::write(dir.join("data").join("x.bin"), version).unwrap();
}

#[cfg(target_os = "windows")]
#[test]
fn recover_current_from_backup() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    // a crash between the two renames: current is gone, the newest backup holds the app
    fs::remove_dir_all(root.join("current")).unwrap();
    make_backup(&root, "current.old-1700000100-aaaa", "1.0.1");
    make_backup(&root, "current.old-1700000200-bbbb", "1.0.2");
    make_backup(&root, "current.old-1600000300-cccc", "1.0.3");
    make_backup(&root, "current.new-1700000150-dddd", "9.9.9");

    commands::start(
        shared::OperationWait::NoWait,
        LocationContext::FromSpecifiedRootDir(root.clone(), None),
        None,
        None,
        None,
    )
    .unwrap();

    assert_eq!(fs::read(root.join("current").join("data").join("x.bin")).unwrap(), b"1.0.2");
    assert!(leftovers(&root).is_empty(), "{:?}", leftovers(&root));
    let locator = locator_for(&root, None);
    assert_eq!(locator.get_manifest_version(), semver::Version::parse("1.0.2").unwrap());

    // with current present, recovery only removes leftovers
    make_backup(&root, "current.old-1700000900-eeee", "1.0.9");
    make_backup(&root, "current.new-1700000950-ffff", "9.9.9");
    commands::recover_current_dir(&root).unwrap();
    assert_eq!(fs::read(root.join("current").join("data").join("x.bin")).unwrap(), b"1.0.2");
    assert!(leftovers(&root).is_empty());

    // nothing to restore from: nothing is touched
    fs::remove_dir_all(root.join("current")).unwrap();
    make_backup(&root, "current.new-1700000950-ffff", "9.9.9");
    commands::recover_current_dir(&root).unwrap();
    assert!(!root.join("current").exists());
    assert_eq!(leftovers(&root), vec!["current.new-1700000950-ffff".to_string()]);
}

#[cfg(target_os = "windows")]
#[test]
fn recover_skips_when_locked() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    fs::remove_dir_all(root.join("current")).unwrap();
    make_backup(&root, "current.old-1700000200-bbbb", "1.0.2");
    make_backup(&root, "current.new-1700000150-dddd", "9.9.9");

    let lock = LockFile::try_acquire_lock(root.join(chunks::SWAP_LOCK_NAME)).unwrap();
    commands::recover_current_dir(&root).unwrap();
    assert!(!root.join("current").exists());
    let mut left = leftovers(&root);
    left.sort();
    assert_eq!(
        left,
        vec!["current.new-1700000150-dddd".to_string(), "current.old-1700000200-bbbb".to_string()]
    );
    drop(lock);

    commands::recover_current_dir(&root).unwrap();
    assert_eq!(fs::read(root.join("current").join("data").join("x.bin")).unwrap(), b"1.0.2");
    assert!(leftovers(&root).is_empty());
}

#[cfg(target_os = "windows")]
#[test]
fn apply_staged_copy_under_root_when_outside_root() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let packages = tmp.path().join("elsewhere").join("packages");
    fs::create_dir_all(&packages).unwrap();
    let (layout, built) = stage_v2(&root, &packages, random_bytes(7, 60_000));
    let locator = locator_for(&root, Some(packages.clone()));
    assert_eq!(locator.get_packages_dir(), packages);

    apply_in_process(&locator, &layout.dir).unwrap();
    assert_current_is(&root, &v2_files(random_bytes(7, 60_000)));
    assert_eq!(fs::read(root.join("current").join("sq.chunks")).unwrap(), built.a);
    assert_eq!(fs::read(root.join("App.exe")).unwrap(), random_bytes(6, 2000));
    assert!(!layout.dir.exists());
    assert!(leftovers(&root).is_empty(), "{:?}", leftovers(&root));
    assert_eq!(
        locator_for(&root, Some(packages)).get_manifest_version(),
        semver::Version::parse("2.0.0").unwrap()
    );
}

#[cfg(target_os = "windows")]
#[test]
fn apply_staged_copy_failure_leaves_app_untouched() {
    use std::os::windows::fs::OpenOptionsExt;
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    let packages = tmp.path().join("elsewhere").join("packages");
    fs::create_dir_all(&packages).unwrap();
    let (layout, _) = stage_v2(&root, &packages, random_bytes(7, 60_000));
    let locator = locator_for(&root, Some(packages.clone()));

    // an exclusively opened staged file makes the copy fail
    let _exclusive = fs::OpenOptions::new()
        .read(true)
        .share_mode(0)
        .open(layout.current_dir().join("data").join("new.bin"))
        .unwrap();
    assert!(apply_in_process(&locator, &layout.dir).is_err());
    assert_current_is(&root, &v1_files());
    assert_eq!(fs::read(root.join("App.exe")).unwrap(), random_bytes(3, 2000));
    assert!(leftovers(&root).is_empty(), "{:?}", leftovers(&root));
    assert!(layout.ready_json().exists());
}

/// The ACE set of a file as icacls prints it, with the inheritance flag stripped.
fn ace_set(path: &Path) -> HashSet<String> {
    let output = Command::new("icacls").arg(path).output().unwrap();
    let text = String::from_utf8_lossy(&output.stdout).to_string();
    let path_str = path.to_string_lossy().to_string();
    text.lines()
        .map(|l| l.trim().strip_prefix(&path_str).unwrap_or(l).trim().to_string())
        .filter(|l| l.contains(":("))
        .map(|l| l.replace("(I)", ""))
        .collect()
}

#[cfg(target_os = "windows")]
#[test]
fn copy_under_root_inherits_root_acl() {
    dialogs::set_silent(true);
    let tmp = tempdir().unwrap();
    let root = tmp.path().join("app");
    install_v1(&root);
    // root gets its own non-inherited ACL (Administrators, SYSTEM, Users RX and the test user)
    let user = std::env::var("USERNAME").unwrap();
    let status = Command::new("icacls")
        .arg(&root)
        .args([
            "/inheritance:r",
            "/grant:r",
            "*S-1-5-32-544:(OI)(CI)F",
            "*S-1-5-18:(OI)(CI)F",
            "*S-1-5-32-545:(OI)(CI)RX",
        ])
        .arg(format!("{}:(OI)(CI)F", user))
        .status()
        .unwrap();
    assert!(status.success());

    // the staged tree lives in the user's profile, where files carry the profile's ACL
    let profile = PathBuf::from(std::env::var("LOCALAPPDATA").unwrap()).join(format!("velopack_acl_test_{}", std::process::id()));
    let packages = profile.join("packages");
    fs::create_dir_all(&packages).unwrap();
    let result = std::panic::catch_unwind(|| {
        let (layout, _) = stage_v2(&root, &packages, random_bytes(7, 60_000));
        let staged_file = layout.current_dir().join("data").join("new.bin");
        let locator = locator_for(&root, Some(packages.clone()));
        let probe = root.join("acl-probe.txt");
        fs::write(&probe, b"probe").unwrap();
        let expected = ace_set(&probe);
        assert!(!expected.is_empty());
        assert_ne!(
            ace_set(&staged_file),
            expected,
            "the profile ACL must differ from root's for this test to mean anything"
        );

        apply_in_process(&locator, &layout.dir).unwrap();
        assert_current_is(&root, &v2_files(random_bytes(7, 60_000)));
        for entry in walkdir::WalkDir::new(root.join("current")) {
            let entry = entry.unwrap();
            if entry.file_type().is_file() {
                let aces = ace_set(entry.path());
                assert_eq!(aces, expected, "{:?} must carry exactly root's inherited ACL", entry.path());
                let raw = String::from_utf8_lossy(&Command::new("icacls").arg(entry.path()).output().unwrap().stdout).to_string();
                assert!(
                    raw.lines().filter(|l| l.contains(":(")).all(|l| l.contains("(I)")),
                    "{:?}: {}",
                    entry.path(),
                    raw
                );
            }
        }
    });
    let _ = fs::remove_dir_all(&profile);
    if let Err(e) = result {
        std::panic::resume_unwind(e);
    }
}
