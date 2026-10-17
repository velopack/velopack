mod common;

use common::index_writer::*;
use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use velopack::chunks::{self, assemble, staging, AssembleContext, AssembleError, ChunkBlob, StagedLayout};
use velopack::VelopackAsset;

const CHUNK: usize = 64 * 1024;

fn opts() -> TestIndexOptions {
    TestIndexOptions {
        chunk_size: CHUNK,
        ..Default::default()
    }
}

fn v1_files() -> Vec<TestFile> {
    vec![
        TestFile::file("app.exe", random_bytes(1, 300_000)),
        TestFile::file("lib/big.bin", random_bytes(2, 500_000)),
        TestFile::file("small.txt", random_bytes(3, 100)),
        TestFile::file("sq.version", b"<version>1.0.0</version>".to_vec()),
        TestFile::file("gone.bin", random_bytes(4, 150_000)),
        TestFile::updater(random_bytes(5, 70_000)),
        TestFile::stub("App", random_bytes(6, 1000)),
    ]
}

fn v2_files() -> Vec<TestFile> {
    let mut big = random_bytes(2, 500_000);
    big.splice(200_000..200_000, random_bytes(9, 1000));
    vec![
        TestFile::file("app.exe", random_bytes(1, 300_000)),
        TestFile::file("lib/big.bin", big),
        TestFile::file("small.txt", random_bytes(3, 100)),
        TestFile::file("sq.version", b"<version>2.0.0</version>".to_vec()),
        TestFile::file("renamed.bin", random_bytes(4, 150_000)),
        TestFile::file("new.bin", random_bytes(7, 100_000)),
        TestFile::file("empty.txt", Vec::new()),
        TestFile::updater(random_bytes(8, 70_000)),
        TestFile::stub("App", random_bytes(6, 1000)),
        TestFile::stub("Other", random_bytes(10, 500)),
    ]
}

struct Setup {
    _dir: tempfile::TempDir,
    root: PathBuf,
    packages: PathBuf,
    v1: BuiltIndex,
    v2: BuiltIndex,
    layout: StagedLayout,
    ctx: AssembleContext,
}

fn asset_for(built: &BuiltIndex, version: &str) -> VelopackAsset {
    VelopackAsset {
        PackageId: "App".to_string(),
        Version: version.to_string(),
        Type: "Full".to_string(),
        FileName: format!("App-{}-full.nupkg", version),
        Size: 1_000_000,
        ChunkIndexOffset: 900_000,
        ChunkIndexLength: built.trailer().len() as u64,
        ChunkIndexSHA256: built.digest_hex(),
        ..Default::default()
    }
}

/// Writes `asset.json` + `index.bin` for a built index under `packages/staged/<id>` and returns the layout.
fn stage(packages: &Path, built: &BuiltIndex, version: &str) -> StagedLayout {
    let layout = StagedLayout::new(packages, &built.staged_id());
    fs::create_dir_all(&layout.dir).unwrap();
    staging::write_asset_json(&layout, &asset_for(built, version)).unwrap();
    fs::write(layout.index_bin(), built.trailer()).unwrap();
    layout
}

fn fill_blob(layout: &StagedLayout, chunks: impl Iterator<Item = BuiltChunk>) {
    let mut blob = ChunkBlob::open(layout.chunks_bin()).unwrap();
    for c in chunks {
        blob.append(&c.sha256, &c.plain).unwrap();
    }
    blob.flush().unwrap();
}

/// v1 installed at root, v2 staged with only the chunks v1 cannot provide in the blob.
fn setup() -> Setup {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("app");
    let packages = root.join("packages");
    let v1 = build_index(&v1_files(), &opts());
    let v2 = build_index(&v2_files(), &opts());
    write_installed_view(&root, &v1_files(), &v1);
    let layout = stage(&packages, &v2, "2.0.0");
    let have: HashSet<[u8; 32]> = v1.chunk_hashes().into_iter().collect();
    fill_blob(&layout, v2.chunks.iter().filter(|c| !have.contains(&c.sha256)).cloned());
    let ctx = AssembleContext {
        staged_dir: layout.dir.clone(),
        current_dir: root.join("current"),
        updater_local_source: root.join("Update.exe"),
        root_dir: root.clone(),
    };
    Setup {
        _dir: dir,
        root,
        packages,
        v1,
        v2,
        layout,
        ctx,
    }
}

fn no_progress() -> impl Fn(i16) + Sync {
    |_| {}
}

fn assert_tree_equals(layout: &StagedLayout, files: &[TestFile], built: &BuiltIndex) {
    for f in files {
        match (f.kind, f.disposition) {
            (0, 0) => assert_eq!(fs::read(layout.current_dir().join(&f.path)).unwrap(), f.data, "{}", f.path),
            (0, 1) => assert_eq!(fs::read(layout.updater()).unwrap(), f.data),
            (0, 2) => {
                let name = format!("{}.exe", f.path.strip_suffix(chunks::STUB_SUFFIX).unwrap());
                assert_eq!(fs::read(layout.stubs_dir().join(name)).unwrap(), f.data);
                assert!(!layout.current_dir().join(&f.path).exists());
            }
            (2, _) => assert!(layout.current_dir().join(&f.path).is_dir()),
            _ => {}
        }
    }
    assert_eq!(
        fs::read(layout.current_dir().join("sq.chunks")).unwrap(),
        built.a,
        "sq.chunks is A verbatim"
    );
    assert!(!layout.current_dir().join("Squirrel.exe").exists());
    // nothing else ends up in current/
    let mut on_disk = Vec::new();
    fn walk(root: &Path, dir: &Path, out: &mut Vec<String>) {
        for e in fs::read_dir(dir).unwrap() {
            let p = e.unwrap().path();
            if p.is_dir() {
                walk(root, &p, out);
            } else {
                out.push(p.strip_prefix(root).unwrap().to_string_lossy().replace('\\', "/"));
            }
        }
    }
    walk(&layout.current_dir(), &layout.current_dir(), &mut on_disk);
    let mut expected: Vec<String> = files
        .iter()
        .filter(|f| f.kind != 2 && f.disposition == 0)
        .map(|f| f.path.clone())
        .collect();
    expected.push("sq.chunks".to_string());
    on_disk.sort();
    expected.sort();
    assert_eq!(on_disk, expected);
}

#[test]
fn assembles_from_local_and_blob() {
    let s = setup();
    let seen = Mutex::new(Vec::new());
    let progress = |p: i16| seen.lock().unwrap().push(p);
    assemble(&s.ctx, &progress).unwrap();

    let seen = seen.into_inner().unwrap();
    assert_eq!(*seen.last().unwrap(), 100);
    assert!(seen.windows(2).all(|w| w[0] < w[1]), "progress is monotonic: {:?}", seen);
    assert_tree_equals(&s.layout, &v2_files(), &s.v2);

    let marker = staging::read_ready_marker(&s.layout.dir).expect("ready.json is valid");
    assert_eq!(marker.Format, 1);
    assert_eq!(marker.StagedId, s.v2.staged_id());
    assert_eq!(marker.IndexSHA256, s.v2.digest_hex());
    assert_eq!(
        marker.UpdaterSHA256,
        chunks::sha256_hex(&chunks::sha256_file(s.layout.updater()).unwrap())
    );
    assert_eq!(marker.Asset.Version, "2.0.0");
    assert_eq!(marker.Asset.ChunkIndexSHA256, s.v2.digest_hex());
    assert!(marker.CreatedUtc.ends_with('Z') && marker.CreatedUtc.len() == 20, "{}", marker.CreatedUtc);
    assert!(marker.matches_asset(&asset_for(&s.v2, "2.0.0")));
    assert!(
        !marker.matches_asset(&asset_for(&s.v1, "2.0.0")),
        "identity is the index digest, not the version"
    );
    let (dir, found) = staging::find_ready_staged_for(&s.packages, &asset_for(&s.v2, "2.0.0")).unwrap();
    assert_eq!(dir, s.layout.dir);
    assert_eq!(found.StagedId, marker.StagedId);
    assert_eq!(staging::find_ready_staged(&s.packages).unwrap().0, s.layout.dir);
    assert!(staging::find_ready_staged_for(&s.packages, &asset_for(&s.v1, "2.0.0")).is_none());
    assert!(!s.layout.missing_bin().exists());
    // nothing outside the staged dir was touched
    assert_eq!(fs::read(s.root.join("current").join("sq.version")).unwrap(), b"<version>1.0.0</version>");
    assert_eq!(fs::read(s.root.join("current").join("sq.chunks")).unwrap(), s.v1.a);

    // a second run is a no-op resume and still succeeds
    assemble(&s.ctx, &no_progress()).unwrap();
    assert!(staging::read_ready_marker(&s.layout.dir).is_some());
}

#[test]
fn corrupt_local_chunk_reported_missing() {
    let s = setup();
    // flip a byte inside chunk 2 of app.exe (unchanged between v1 and v2, so it must come from the local file)
    let app = s.root.join("current").join("app.exe");
    let mut data = fs::read(&app).unwrap();
    data[2 * CHUNK + 10] ^= 0xFF;
    fs::write(&app, &data).unwrap();

    let err = assemble(&s.ctx, &no_progress()).unwrap_err();
    assert!(matches!(err, AssembleError::Missing(1)), "{}", err);
    assert_eq!(err.exit_code(), 3);
    let missing = staging::read_missing_bin(&s.layout.missing_bin()).unwrap();
    let app_chunks: Vec<_> = s.v2.chunks.iter().filter(|c| c.file_path == "app.exe").collect();
    assert_eq!(missing, vec![app_chunks[2].sha256]);
    assert!(!s.layout.current_dir().join("app.exe").exists());
    assert!(!s.layout.current_dir().join("app.exe.vpkpart").exists());
    assert!(!s.layout.ready_json().exists());
    // files that could be completed are kept
    assert_eq!(fs::read(s.layout.current_dir().join("lib").join("big.bin")).unwrap(), v2_files()[1].data);

    // the missing round: the fetched chunk lands in the blob, the next run completes
    let mut blob = ChunkBlob::open(s.layout.chunks_bin()).unwrap();
    blob.append(&app_chunks[2].sha256, &app_chunks[2].plain).unwrap();
    drop(blob);
    assemble(&s.ctx, &no_progress()).unwrap();
    assert!(!s.layout.missing_bin().exists());
    assert_tree_equals(&s.layout, &v2_files(), &s.v2);
}

#[test]
fn deleted_local_file_reported_missing() {
    let s = setup();
    fs::remove_file(s.root.join("current").join("app.exe")).unwrap();
    fs::remove_file(s.root.join("App.exe")).unwrap(); // the stub, unchanged in v2
    let err = assemble(&s.ctx, &no_progress()).unwrap_err();
    let app_chunks: HashSet<[u8; 32]> =
        s.v2.chunks
            .iter()
            .filter(|c| c.file_path == "app.exe" || c.file_path == "App_ExecutionStub.exe")
            .map(|c| c.sha256)
            .collect();
    assert!(matches!(err, AssembleError::Missing(n) if n == app_chunks.len()), "{}", err);
    let missing: HashSet<[u8; 32]> = staging::read_missing_bin(&s.layout.missing_bin()).unwrap().into_iter().collect();
    assert_eq!(missing, app_chunks);
    assert!(!s.layout.stubs_dir().join("App.exe").exists());
    assert!(s.layout.stubs_dir().join("Other.exe").exists());
}

#[test]
fn modified_whole_file_heals_per_chunk() {
    let s = setup();
    // lib/big.bin is modified in v2; v1's copy is additionally damaged in chunk 1 locally, but that
    // chunk is also available from the blob (as a fetched chunk would be), so assembly heals per chunk
    // instead of giving up on the whole file
    let big_v1 = s.root.join("current").join("lib").join("big.bin");
    let mut data = fs::read(&big_v1).unwrap();
    data[CHUNK + 5] ^= 0xFF;
    fs::write(&big_v1, &data).unwrap();
    let big_v2: Vec<_> = s.v2.chunks.iter().filter(|c| c.file_path == "lib/big.bin").collect();
    let mut blob = ChunkBlob::open(s.layout.chunks_bin()).unwrap();
    blob.append(&big_v2[1].sha256, &big_v2[1].plain).unwrap();
    drop(blob);
    // app.exe is unchanged in v2 but its local copy was modified in chunk 0: the whole-file copy is
    // discarded and the chunks are sourced individually (chunk 0 from the blob)
    let app = s.root.join("current").join("app.exe");
    let mut data = fs::read(&app).unwrap();
    data[7] ^= 0xFF;
    fs::write(&app, &data).unwrap();
    let app_v2: Vec<_> = s.v2.chunks.iter().filter(|c| c.file_path == "app.exe").collect();
    let mut blob = ChunkBlob::open(s.layout.chunks_bin()).unwrap();
    blob.append(&app_v2[0].sha256, &app_v2[0].plain).unwrap();
    drop(blob);

    assemble(&s.ctx, &no_progress()).unwrap();
    assert_tree_equals(&s.layout, &v2_files(), &s.v2);
}

#[test]
fn resume_keeps_complete_files() {
    let s = setup();
    fs::remove_file(s.root.join("current").join("app.exe")).unwrap();
    assert!(matches!(assemble(&s.ctx, &no_progress()), Err(AssembleError::Missing(_))));
    let big = s.layout.current_dir().join("lib").join("big.bin");
    let before = fs::metadata(&big).unwrap().modified().unwrap();
    // a partial file from an interrupted run is rebuilt, a damaged complete file is rebuilt too
    fs::write(s.layout.current_dir().join("new.bin.vpkpart"), b"partial").unwrap();
    let small = s.layout.current_dir().join("small.txt");
    fs::write(&small, b"damaged but same len, no.").unwrap();

    std::thread::sleep(std::time::Duration::from_millis(50));
    let app_chunks = s.v2.chunks.iter().filter(|c| c.file_path == "app.exe").cloned();
    fill_blob(&s.layout, app_chunks);
    assemble(&s.ctx, &no_progress()).unwrap();

    assert_eq!(
        fs::metadata(&big).unwrap().modified().unwrap(),
        before,
        "complete files are kept, not rewritten"
    );
    assert!(!s.layout.current_dir().join("new.bin.vpkpart").exists());
    assert_tree_equals(&s.layout, &v2_files(), &s.v2);
}

#[test]
fn no_marker_without_success() {
    let s = setup();
    // a stale marker from an earlier run is removed at the start
    fs::write(s.layout.ready_json(), b"{}").unwrap();
    fs::remove_file(s.root.join("current").join("app.exe")).unwrap();
    assert!(matches!(assemble(&s.ctx, &no_progress()), Err(AssembleError::Missing(_))));
    assert!(!s.layout.ready_json().exists());
    assert!(staging::find_ready_staged(&s.packages).is_none());

    // invalid input never produces a marker either
    let mut asset = asset_for(&s.v2, "2.0.0");
    asset.ChunkIndexSHA256 = s.v1.digest_hex();
    staging::write_asset_json(&s.layout, &asset).unwrap();
    let err = assemble(&s.ctx, &no_progress()).unwrap_err();
    assert!(matches!(err, AssembleError::Invalid(_)), "{}", err);
    assert_eq!(err.exit_code(), 2);
    assert!(!s.layout.ready_json().exists());

    // directory name must be the staged id
    staging::write_asset_json(&s.layout, &asset_for(&s.v2, "2.0.0")).unwrap();
    let renamed = s.packages.join("staged").join("0123456789abcdef0123456789abcdef");
    fs::rename(&s.layout.dir, &renamed).unwrap();
    let ctx = AssembleContext {
        staged_dir: renamed.clone(),
        ..s.ctx.clone()
    };
    assert!(matches!(assemble(&ctx, &no_progress()), Err(AssembleError::Invalid(_))));
    fs::rename(&renamed, &s.layout.dir).unwrap();

    // an index that fails validation is invalid input
    let mut t = s.v2.trailer();
    t[4] = 2;
    fs::write(s.layout.index_bin(), &t).unwrap();
    let mut asset = asset_for(&s.v2, "2.0.0");
    asset.ChunkIndexSHA256 = chunks::sha256_hex(&chunks::sha256(&t));
    staging::write_asset_json(&s.layout, &asset).unwrap();
    let renamed = s.packages.join("staged").join(chunks::staged_id(&asset.ChunkIndexSHA256));
    fs::rename(&s.layout.dir, &renamed).unwrap();
    let ctx = AssembleContext {
        staged_dir: renamed.clone(),
        ..s.ctx.clone()
    };
    assert!(matches!(assemble(&ctx, &no_progress()), Err(AssembleError::Invalid(_))));
    assert!(!renamed.join("ready.json").exists());
}

#[test]
fn index_changed_underneath_is_invalid() {
    let s = setup();
    let index_bin = s.layout.index_bin();
    let other = s.v1.trailer();
    let progress = move |_: i16| {
        // a concurrent GC/refetch replaces the index while we work
        fs::write(&index_bin, &other).unwrap();
    };
    let err = assemble(&s.ctx, &progress).unwrap_err();
    assert!(matches!(err, AssembleError::Invalid(ref m) if m.contains("changed")), "{}", err);
    assert!(!s.layout.ready_json().exists());
    assert!(!s.layout.current_dir().join("sq.chunks").exists());
}

#[test]
fn updater_and_stubs_go_to_sidecars() {
    let s = setup();
    assemble(&s.ctx, &no_progress()).unwrap();
    assert_eq!(fs::read(s.layout.updater()).unwrap(), random_bytes(8, 70_000));
    assert_eq!(fs::read(s.layout.stubs_dir().join("App.exe")).unwrap(), random_bytes(6, 1000));
    assert_eq!(fs::read(s.layout.stubs_dir().join("Other.exe")).unwrap(), random_bytes(10, 500));
    assert!(!s.layout.current_dir().join("Squirrel.exe").exists());
    assert!(!s.layout.current_dir().join("Update.exe").exists());
    assert!(!s.layout.current_dir().join("App_ExecutionStub.exe").exists());
    assert_tree_equals(&s.layout, &v2_files(), &s.v2);
    // the unchanged stub came from root/App.exe, the changed updater from the blob
    let marker = staging::read_ready_marker(&s.layout.dir).unwrap();
    assert_eq!(marker.UpdaterSHA256, chunks::sha256_hex(&chunks::sha256(&random_bytes(8, 70_000))));

    // a package without an updater cannot be staged
    let files: Vec<TestFile> = v2_files().into_iter().filter(|f| f.disposition != 1).collect();
    let built = build_index(&files, &opts());
    let layout = stage(&s.packages, &built, "3.0.0");
    fill_blob(&layout, built.chunks.iter().cloned());
    let ctx = AssembleContext {
        staged_dir: layout.dir.clone(),
        ..s.ctx.clone()
    };
    assert!(matches!(assemble(&ctx, &no_progress()), Err(AssembleError::Invalid(_))));
}

#[test]
fn symlink_containment_enforced() {
    let s = setup();
    for target in ["../../escape.txt", "/etc/passwd", "C:\\Windows\\x", "links/../../../x"] {
        let mut files = v2_files();
        files.push(TestFile::symlink("links/l", target));
        let built = build_index(&files, &opts());
        assert!(chunks::ChunkIndex::parse_trailer(&built.trailer()).is_err(), "{}", target);
        let layout = stage(&s.packages, &built, "3.0.0");
        let ctx = AssembleContext {
            staged_dir: layout.dir.clone(),
            ..s.ctx.clone()
        };
        let err = assemble(&ctx, &no_progress()).unwrap_err();
        assert!(matches!(err, AssembleError::Invalid(_)), "{}: {}", target, err);
        assert!(!layout.current_dir().join("links").exists());
    }
}

#[test]
#[cfg_attr(windows, ignore = "symlink creation needs Developer Mode or admin on Windows")]
fn assembles_symlink_and_empty_dir() {
    let s = setup();
    let mut files = v2_files();
    files.push(TestFile::symlink("links/cfg", "../small.txt"));
    files.push(TestFile::symlink("links/dir", "../lib/"));
    files.push(TestFile::empty_dir("plugins/empty"));
    let built = build_index(&files, &opts());
    let layout = stage(&s.packages, &built, "3.0.0");
    fill_blob(&layout, built.chunks.iter().cloned());
    let ctx = AssembleContext {
        staged_dir: layout.dir.clone(),
        ..s.ctx.clone()
    };
    assemble(&ctx, &no_progress()).unwrap();
    assert!(layout.current_dir().join("plugins").join("empty").is_dir());
    assert_eq!(
        fs::read_link(layout.current_dir().join("links").join("cfg")).unwrap(),
        PathBuf::from("../small.txt")
    );
    assert_eq!(fs::read(layout.current_dir().join("links").join("cfg")).unwrap(), random_bytes(3, 100));
    assert!(layout.current_dir().join("links").join("dir").join("big.bin").exists());
    // resume re-creates symlinks without failing
    assemble(&ctx, &no_progress()).unwrap();
}

#[test]
fn readback_mismatch_is_io_error() {
    let s = setup();
    // make new.bin the only job so the progress callback reliably fires while it is partial
    let files: Vec<TestFile> = vec![
        TestFile::file("new.bin", random_bytes(7, 100_000)),
        TestFile::updater(random_bytes(8, 70_000)),
    ];
    let built = build_index(&files, &opts());
    let layout = stage(&s.packages, &built, "3.0.0");
    fill_blob(&layout, built.chunks.iter().cloned());
    let ctx = AssembleContext {
        staged_dir: layout.dir.clone(),
        ..s.ctx.clone()
    };
    let part = layout.current_dir().join("new.bin.vpkpart");
    let corrupted = Mutex::new(false);
    let progress = |p: i16| {
        // after the first chunk of the file is written, damage the partial file through another handle
        // (what a failing disk would do); the readback must catch it. Only while the file is still partial:
        // the updater's worker also reports progress and could otherwise fire after new.bin's readback.
        let partial = fs::metadata(&part).map(|m| m.len() > 0 && m.len() < 100_000).unwrap_or(false);
        if p > 0 && partial && !*corrupted.lock().unwrap() {
            use std::io::{Seek, SeekFrom, Write};
            let mut f = fs::OpenOptions::new().write(true).open(&part).unwrap();
            f.seek(SeekFrom::Start(0)).unwrap();
            f.write_all(&[0xAB; 16]).unwrap();
            *corrupted.lock().unwrap() = true;
        }
    };
    let err = assemble(&ctx, &progress).unwrap_err();
    assert!(*corrupted.lock().unwrap(), "the test must have damaged the partial file");
    assert!(matches!(err, AssembleError::Io(_)), "{}", err);
    assert_eq!(err.exit_code(), 4);
    assert!(!layout.current_dir().join("new.bin").exists());
    assert!(!layout.ready_json().exists());
}
