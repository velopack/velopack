//! UpdateManager's chunk path end to end (§8.4): a v1 installed view, a v2 package served by the mock server,
//! download_updates stages the tree, falls back to the full download when it must, and apply picks the
//! staged tree by id.

mod common;

use common::index_writer::*;
use common::mock_server::MockHttpServer;
use common::package::{build_package, compressible, Package};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::mpsc;
use velopack::chunks::{self, staging, StagedLayout, StagedReadyMarker};
use velopack::locator::VelopackLocatorConfig;
use velopack::sources::HttpSource;
use velopack::{ApplyWaitMode, UpdateInfo, UpdateManager, UpdateOptions, VelopackAsset, VelopackAssetFeed};

fn nuspec(version: &str) -> Vec<u8> {
    let fixture = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("test")
        .join("fixtures")
        .join("Test.Squirrel-App.nuspec");
    fs::read_to_string(fixture)
        .unwrap()
        .replace("<version>1.0.0</version>", &format!("<version>{}</version>", version))
        .into_bytes()
}

fn v1_files() -> Vec<TestFile> {
    vec![
        TestFile::file("app.exe", compressible(1, 300_000)),
        TestFile::file("lib/big.bin", compressible(2, 500_000)),
        TestFile::file("small.txt", compressible(3, 100)),
        TestFile::file("sq.version", nuspec("1.0.0")),
        TestFile::file("gone.bin", compressible(4, 150_000)),
        TestFile::updater(compressible(5, 70_000)),
        TestFile::stub("App", compressible(6, 1000)),
    ]
}

fn v2_files() -> Vec<TestFile> {
    let mut big = compressible(2, 500_000);
    big.splice(200_000..200_000, compressible(9, 1000));
    vec![
        TestFile::file("app.exe", compressible(1, 300_000)),
        TestFile::file("lib/big.bin", big),
        TestFile::file("small.txt", compressible(3, 100)),
        TestFile::file("sq.version", nuspec("2.0.0")),
        TestFile::file("renamed.bin", compressible(4, 150_000)),
        TestFile::file("new.bin", compressible(7, 100_000)),
        TestFile::updater(compressible(8, 70_000)),
        TestFile::stub("App", compressible(6, 1000)),
    ]
}

const V2_NAME: &str = "App-2.0.0-full.nupkg";

struct App {
    _dir: tempfile::TempDir,
    root: PathBuf,
    packages: PathBuf,
    config: VelopackLocatorConfig,
}

fn install_v1() -> App {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("app");
    let packages = root.join("packages");
    let v1 = build_index(&v1_files(), &TestIndexOptions::default());
    write_installed_view(&root, &v1_files(), &v1);
    fs::create_dir_all(&packages).unwrap();
    let config = VelopackLocatorConfig {
        RootAppDir: root.clone(),
        UpdateExePath: root.join("Update.exe"),
        PackagesDir: packages.clone(),
        ManifestPath: root.join("current").join("sq.version"),
        CurrentBinaryDir: root.join("current"),
        IsPortable: true,
    };
    App {
        _dir: dir,
        root,
        packages,
        config,
    }
}

fn serve(server: &MockHttpServer, files: &[TestFile]) -> Package {
    let pkg = build_package(files, V2_NAME, "2.0.0");
    server.add_file(&format!("/{}", V2_NAME), pkg.bytes.clone());
    pkg
}

fn update_info(pkg: &Package) -> UpdateInfo {
    UpdateInfo {
        TargetFullRelease: pkg.asset.clone(),
        IsDowngrade: false,
    }
}

fn manager(app: &App, server: &MockHttpServer, options: Option<UpdateOptions>) -> UpdateManager {
    UpdateManager::new(HttpSource::new(server.url()), options, Some(app.config.clone())).unwrap()
}

/// Runs download_updates with a progress channel and returns the reported values.
fn download(um: &UpdateManager, info: &UpdateInfo) -> Vec<i16> {
    let (tx, rx) = mpsc::channel();
    um.download_updates(info, Some(tx)).unwrap();
    rx.iter().collect()
}

fn assert_monotonic_to_100(progress: &[i16]) {
    assert!(!progress.is_empty());
    assert!(progress.windows(2).all(|w| w[0] < w[1]), "progress never decreases: {:?}", progress);
    assert_eq!(*progress.last().unwrap(), 100);
}

fn nupkgs(packages: &Path) -> Vec<PathBuf> {
    fs::read_dir(packages)
        .unwrap()
        .filter_map(|e| e.ok())
        .map(|e| e.path())
        .filter(|p| p.extension().map(|x| x == "nupkg").unwrap_or(false))
        .collect()
}

fn staged_dirs(packages: &Path) -> Vec<PathBuf> {
    match fs::read_dir(StagedLayout::root(packages)) {
        Ok(rd) => rd.filter_map(|e| e.ok()).map(|e| e.path()).collect(),
        Err(_) => Vec::new(),
    }
}

fn assert_staged_tree(layout: &StagedLayout, files: &[TestFile], built: &BuiltIndex) {
    for f in files {
        match f.disposition {
            0 => assert_eq!(fs::read(layout.current_dir().join(&f.path)).unwrap(), f.data, "{}", f.path),
            1 => assert_eq!(fs::read(layout.updater()).unwrap(), f.data),
            _ => {
                let name = format!("{}.exe", f.path.strip_suffix(chunks::STUB_SUFFIX).unwrap());
                assert_eq!(fs::read(layout.stubs_dir().join(name)).unwrap(), f.data);
            }
        }
    }
    assert_eq!(fs::read(layout.current_dir().join(chunks::LOCAL_INDEX_NAME)).unwrap(), built.a);
    assert!(staging::read_ready_marker(&layout.dir).is_some());
}

#[cfg(windows)]
#[test]
fn chunk_update_stages_tree_from_http_source() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    let pkg = serve(&server, &v2_files());
    let um = manager(&app, &server, None);
    let info = update_info(&pkg);

    let progress = download(&um, &info);
    assert_monotonic_to_100(&progress);

    let layout = StagedLayout::new(&app.packages, &pkg.built.staged_id());
    assert_staged_tree(&layout, &v2_files(), &pkg.built);
    assert!(nupkgs(&app.packages).is_empty(), "nothing was downloaded in full");
    assert_eq!(
        fs::read(app.root.join("Update.exe")).unwrap(),
        fs::read(layout.updater()).unwrap(),
        "the new updater was copied next to the app"
    );

    // only ranged requests, and far fewer bytes than the package
    let requests = server.requests();
    assert!(!requests.is_empty());
    assert!(
        requests.iter().all(|r| r.range.is_some()),
        "every package request is ranged: {:?}",
        requests
    );
    let fetched: u64 = requests.iter().filter_map(|r| r.range).map(|(s, e)| e - s + 1).sum();
    assert!(fetched < pkg.asset.Size / 2, "fetched {} of {} bytes", fetched, pkg.asset.Size);

    // the pending asset is the staged one, pointer included
    let pending = um.get_update_pending_restart().expect("staged update is pending");
    assert_eq!(pending.Version, "2.0.0");
    assert_eq!(pending.ChunkIndexSHA256, pkg.built.digest_hex());

    // a second download finds the ready tree and does nothing
    let before = server.requests().len();
    let progress = download(&um, &info);
    assert_eq!(progress, vec![100]);
    assert_eq!(server.requests().len(), before);

    // apply routes to apply-staged through the target's updater: the sidecar is copied over a stale one
    fs::write(app.root.join("Update.exe"), b"stale updater").unwrap();
    let result = um.unsafe_apply_updates(&pending, true, ApplyWaitMode::NoWait, false, Vec::<String>::new());
    assert!(result.is_err(), "the fake updater cannot be launched");
    assert_eq!(fs::read(app.root.join("Update.exe")).unwrap(), fs::read(layout.updater()).unwrap());
    assert!(layout.ready_json().exists());

    // a sidecar that does not match the marker invalidates the staged tree
    fs::write(layout.updater(), b"tampered").unwrap();
    let result = um.unsafe_apply_updates(&pending, true, ApplyWaitMode::NoWait, false, Vec::<String>::new());
    assert!(result.is_err());
    assert!(!layout.dir.exists(), "the staged directory was deleted");
}

#[cfg(windows)]
#[test]
fn server_ignoring_range_falls_back_to_full_download() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    server.set_always_ignore_range(true);
    let pkg = serve(&server, &v2_files());
    let um = manager(&app, &server, None);
    let progress = download(&um, &update_info(&pkg));
    assert_monotonic_to_100(&progress);

    assert_eq!(fs::read(app.packages.join(V2_NAME)).unwrap(), pkg.bytes);
    assert!(staged_dirs(&app.packages).is_empty(), "the classic path deletes staged/*");
    let requests = server.requests();
    assert!(requests.iter().any(|r| r.range.is_some()), "the chunk path was attempted");
    assert_eq!(
        requests.iter().filter(|r| r.range.is_none()).count(),
        1,
        "then the package was downloaded once"
    );
    // the test package is not a zip, so get_update_pending_restart cannot read its manifest; the classic
    // pending lookup itself is unchanged and covered by pending_restart_prefers_the_higher_version_of_either_kind
}

#[cfg(windows)]
#[test]
fn ready_staged_matched_by_id_not_version() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    let pkg = serve(&server, &v2_files());
    let um = manager(&app, &server, None);
    download(&um, &update_info(&pkg));
    let first = StagedLayout::new(&app.packages, &pkg.built.staged_id());
    assert!(first.ready_json().exists());

    // the same version and file name re-published with different content: a different id
    let mut files = v2_files();
    files.push(TestFile::file("extra.bin", compressible(11, 50_000)));
    let server2 = MockHttpServer::empty();
    let pkg2 = serve(&server2, &files);
    assert_ne!(pkg2.built.staged_id(), pkg.built.staged_id());
    assert_eq!(pkg2.asset.FileName, pkg.asset.FileName);
    let um2 = manager(&app, &server2, None);
    let progress = download(&um2, &update_info(&pkg2));
    assert_monotonic_to_100(&progress);
    assert!(!server2.requests().is_empty(), "the new tree was fetched");
    let second = StagedLayout::new(&app.packages, &pkg2.built.staged_id());
    assert_staged_tree(&second, &files, &pkg2.built);
    assert!(!first.dir.exists(), "the stale same-version tree was collected");
    assert_eq!(um2.get_update_pending_restart().unwrap().ChunkIndexSHA256, pkg2.built.digest_hex());
}

#[cfg(windows)]
#[test]
fn locally_modified_file_heals_through_missing_round() {
    let app = install_v1();
    // a large local file the plan trusts by length: flip a byte inside its first chunk
    let big = app.root.join("current").join("lib").join("big.bin");
    let mut bytes = fs::read(&big).unwrap();
    bytes[10] ^= 0xFF;
    fs::write(&big, &bytes).unwrap();

    let server = MockHttpServer::empty();
    let pkg = serve(&server, &v2_files());
    let um = manager(&app, &server, None);
    let progress = download(&um, &update_info(&pkg));
    assert_monotonic_to_100(&progress);
    let layout = StagedLayout::new(&app.packages, &pkg.built.staged_id());
    assert_staged_tree(&layout, &v2_files(), &pkg.built);
    assert!(!layout.missing_bin().exists());

    // the corrupt chunk's range was fetched in the extra round
    let corrupt = pkg
        .built
        .chunks
        .iter()
        .find(|c| c.file_path == "lib/big.bin" && c.plain_offset == 0)
        .unwrap();
    let hit = server
        .requests()
        .iter()
        .filter_map(|r| r.range)
        .any(|(s, e)| s <= corrupt.offset && corrupt.offset + corrupt.length as u64 - 1 <= e);
    assert!(hit, "the chunk the local file could not provide was fetched");
}

#[test]
fn disable_chunked_updates_downloads_full_package() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    let pkg = serve(&server, &v2_files());
    let options = UpdateOptions {
        DisableChunkedUpdates: true,
        ..Default::default()
    };
    let um = manager(&app, &server, Some(options));
    let progress = download(&um, &update_info(&pkg));
    assert_monotonic_to_100(&progress);
    assert_eq!(fs::read(app.packages.join(V2_NAME)).unwrap(), pkg.bytes);
    assert!(server.requests().iter().all(|r| r.range.is_none()), "no ranged request was made");
    assert!(staged_dirs(&app.packages).is_empty());
}

#[test]
fn missing_local_index_downloads_full_package() {
    let app = install_v1();
    fs::remove_file(app.root.join("current").join(chunks::LOCAL_INDEX_NAME)).unwrap();
    let server = MockHttpServer::empty();
    let pkg = serve(&server, &v2_files());
    let um = manager(&app, &server, None);
    let progress = download(&um, &update_info(&pkg));
    assert_monotonic_to_100(&progress);
    assert_eq!(fs::read(app.packages.join(V2_NAME)).unwrap(), pkg.bytes);
    assert!(server.requests().iter().all(|r| r.range.is_none()));
}

#[test]
fn asset_without_pointer_downloads_full_package() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    let pkg = serve(&server, &v2_files());
    let mut info = update_info(&pkg);
    info.TargetFullRelease.ChunkIndexLength = 0;
    info.TargetFullRelease.ChunkIndexSHA256 = String::new();
    let um = manager(&app, &server, None);
    let progress = download(&um, &info);
    assert_monotonic_to_100(&progress);
    assert_eq!(fs::read(app.packages.join(V2_NAME)).unwrap(), pkg.bytes);
    assert!(server.requests().iter().all(|r| r.range.is_none()));
}

#[test]
fn accepts_null_pointer_fields() {
    let json = r#"{"Assets":[{"PackageId":"App","Version":"2.0.0","Type":"Full","FileName":"App-2.0.0-full.nupkg",
        "SHA1":"a","SHA256":"b","Size":10,"ChunkIndexOffset":null,"ChunkIndexLength":null,"ChunkIndexSHA256":null}]}"#;
    let feed: VelopackAssetFeed = serde_json::from_str(json).unwrap();
    let asset = &feed.Assets[0];
    assert_eq!(asset.ChunkIndexOffset, 0);
    assert_eq!(asset.ChunkIndexLength, 0);
    assert_eq!(asset.ChunkIndexSHA256, "");
    assert_eq!(asset.Size, 10);
    let asset: VelopackAsset = serde_json::from_str(r#"{"FileName":"x","NotesMarkdown":null,"Size":null}"#).unwrap();
    assert_eq!(asset.FileName, "x");
    assert_eq!(asset.Size, 0);
}

fn write_marker(packages: &Path, built: &BuiltIndex, version: &str) {
    let layout = StagedLayout::new(packages, &built.staged_id());
    fs::create_dir_all(&layout.dir).unwrap();
    let marker = StagedReadyMarker {
        Format: 1,
        StagedId: built.staged_id(),
        IndexSHA256: built.digest_hex(),
        UpdaterSHA256: "00".repeat(32),
        Asset: VelopackAsset {
            PackageId: "App".to_string(),
            Version: version.to_string(),
            Type: "Full".to_string(),
            FileName: format!("App-{}-full.nupkg", version),
            ChunkIndexLength: built.trailer().len() as u64,
            ChunkIndexSHA256: built.digest_hex(),
            ..Default::default()
        },
        CreatedUtc: "2026-10-17T00:00:00Z".to_string(),
    };
    staging::write_ready_marker(&layout, &marker).unwrap();
}

fn write_nupkg(packages: &Path, version: &str) -> PathBuf {
    use std::io::Write;
    let path = packages.join(format!("App-{}-full.nupkg", version));
    let mut zip = zip::ZipWriter::new(fs::File::create(&path).unwrap());
    zip.start_file("App.nuspec", zip::write::SimpleFileOptions::default()).unwrap();
    zip.write_all(&nuspec(version)).unwrap();
    zip.finish().unwrap();
    path
}

#[test]
fn pending_restart_prefers_the_higher_version_of_either_kind() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    let um = manager(&app, &server, None);
    assert!(um.get_update_pending_restart().is_none());

    let staged = build_index(&v2_files(), &TestIndexOptions::default());
    write_marker(&app.packages, &staged, "2.0.0");
    let pending = um.get_update_pending_restart().unwrap();
    assert_eq!(pending.Version, "2.0.0");
    assert_eq!(pending.ChunkIndexSHA256, staged.digest_hex(), "the staged asset carries its pointer");

    write_nupkg(&app.packages, "3.0.0");
    let pending = um.get_update_pending_restart().unwrap();
    assert_eq!(pending.Version, "3.0.0");
    assert_eq!(pending.ChunkIndexSHA256, "");

    let mut files = v2_files();
    files.push(TestFile::file("later.bin", compressible(12, 10)));
    let staged4 = build_index(&files, &TestIndexOptions::default());
    write_marker(&app.packages, &staged4, "4.0.0");
    let pending = um.get_update_pending_restart().unwrap();
    assert_eq!(pending.Version, "4.0.0");
    assert_eq!(pending.ChunkIndexSHA256, staged4.digest_hex());

    // a staged tree at or below the installed version is never pending
    let app2 = install_v1();
    let um2 = manager(&app2, &server, None);
    write_marker(&app2.packages, &staged, "1.0.0");
    assert!(um2.get_update_pending_restart().is_none());
}

#[test]
fn staged_path_helpers() {
    let app = install_v1();
    let server = MockHttpServer::empty();
    let um = manager(&app, &server, None);
    let locator = velopack::locator::VelopackLocator::new(&app.config).unwrap();
    assert_eq!(locator.get_staged_root_dir(), app.packages.join("staged"));
    assert_eq!(locator.get_staged_dir("abc"), app.packages.join("staged").join("abc"));
    drop(um);
}
