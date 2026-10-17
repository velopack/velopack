//! The cross-language contract: the Rust reader against the golden fixtures in `test/fixtures/delta-v2/`, which
//! are produced by the C# generator (`index_*.bin`, `plan_vectors.json`, `feed_null_pointer.json`) and by the
//! vpk chunk-aligned writer (`ChunkTestApp-*.nupkg`, `releases.win.json`).

use serde_json::Value;
use std::collections::HashSet;
use std::fs;
use std::io::Read;
use std::path::{Path, PathBuf};
use velopack::chunks::{self, coalesce, exceeds_transport_ceiling, plan_with, ChunkIndex, Disposition, FileKind};
use velopack::{VelopackAsset, VelopackAssetFeed};

fn fixture_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("test")
        .join("fixtures")
        .join("delta-v2")
}

fn fixture(name: &str) -> Vec<u8> {
    fs::read(fixture_dir().join(name)).unwrap_or_else(|e| panic!("fixture {} could not be read: {}", name, e))
}

fn hex(s: &str) -> Vec<u8> {
    assert!(s.len() % 2 == 0, "odd hex length");
    (0..s.len()).step_by(2).map(|i| u8::from_str_radix(&s[i..i + 2], 16).unwrap()).collect()
}

fn hash(s: &str) -> [u8; 32] {
    chunks::parse_sha256_hex(s).unwrap_or_else(|| panic!("{} is not a SHA-256", s))
}

/// The Section A prefix of a trailer (its length is derived from the A header).
fn section_a(trailer: &[u8]) -> &[u8] {
    let u32_at = |o: usize| u32::from_le_bytes(trailer[o..o + 4].try_into().unwrap()) as usize;
    let len = 48 + u32_at(32) + 64 * u32_at(24) + 36 * u32_at(28);
    &trailer[..len]
}

#[test]
fn golden_sample_parses() {
    let trailer = fixture("index_sample.bin");
    let index = ChunkIndex::parse_trailer(&trailer).unwrap();
    assert!(index.has_transport());
    assert_eq!(index.file_count(), 8);
    assert_eq!(index.chunk_count(), 8);
    let paths: Vec<&str> = (0..index.file_count()).map(|i| index.path(i)).collect();
    assert_eq!(
        paths,
        vec![
            "MyApp.exe",
            "MyApp_ExecutionStub.exe",
            "Squirrel.exe",
            "data/empty.txt",
            "data/link.txt",
            "plugins",
            "sq.version",
            "Übersicht/ü.txt"
        ]
    );

    let app = index.file(0);
    assert_eq!(app.disposition, Disposition::AppFile);
    assert_eq!(app.kind, FileKind::File);
    assert_eq!(app.length, 150_000);
    assert_eq!((app.first_chunk, app.chunk_count), (0, 2));
    assert_eq!(index.chunk(0).length, 100_000);
    assert_eq!(index.chunk(1).length, 50_000);
    let app_b = index.b_file(0).unwrap();
    assert_eq!(app_b.method, 8);
    let (c0, c1) = (index.b_range(0).unwrap(), index.b_range(1).unwrap());
    assert_eq!(c0.offset, app_b.data_offset);
    assert_eq!(c0.offset + c0.length as u64, c1.offset);
    assert_eq!(app_b.data_offset + app_b.compressed_size - 2, c1.offset + c1.length as u64);

    let stub = index.file(1);
    assert_eq!(stub.disposition, Disposition::Stub);
    assert_eq!(index.installed_name(&stub), "MyApp.exe");
    assert_eq!(index.b_file(1).unwrap().method, 0);
    assert_eq!(index.file(2).disposition, Disposition::Updater);
    assert_eq!(index.updater_record().unwrap().index, 2);
    assert_eq!(
        (index.file(3).kind, index.file(3).length, index.file(3).chunk_count),
        (FileKind::File, 0, 0)
    );
    assert_eq!(index.file(4).kind, FileKind::Symlink);
    assert_eq!(index.symlink_target(4).as_deref(), Some("../MyApp.exe"));
    assert_eq!(index.file(5).kind, FileKind::EmptyDir);
    assert_eq!(index.find_file("Übersicht/ü.txt"), Some(7));

    // the local content map is the A prefix, and it carries no transport map
    let local = ChunkIndex::parse_local(section_a(&trailer)).unwrap();
    assert!(!local.has_transport());
    assert_eq!(local.file_count(), 8);
    assert_eq!(local.a_bytes(), index.a_bytes());
}

#[test]
fn golden_invalid_fixtures_are_rejected() {
    let mut names: Vec<String> = fs::read_dir(fixture_dir())
        .unwrap()
        .filter_map(|e| e.ok())
        .map(|e| e.file_name().to_string_lossy().into_owned())
        .filter(|n| n.starts_with("index_invalid_") && n.ends_with(".bin"))
        .collect();
    names.sort();
    for expected in [
        "truncated",
        "count_overflow",
        "noncontiguous_chunks",
        "unsorted",
        "bad_path",
        "duplicate_path",
        "fold_duplicate",
        "dotdot",
        "reserved_name",
        "nested_squirrel",
        "nested_stub",
        "symlink_escape",
        "hash_algo",
    ] {
        assert!(
            names.contains(&format!("index_invalid_{}.bin", expected)),
            "missing fixture for {}",
            expected
        );
    }
    let accepted: Vec<&String> = names.iter().filter(|n| ChunkIndex::parse_trailer(&fixture(n)).is_ok()).collect();
    assert!(accepted.is_empty(), "the Rust reader accepted invalid fixtures: {:?}", accepted);
}

#[test]
fn golden_unknown_cdc_algo_is_accepted() {
    let trailer = fixture("index_unknown_cdc_algo.bin");
    let index = ChunkIndex::parse_trailer(&trailer).unwrap();
    assert_eq!(index.file_count(), 8);
    assert_eq!(trailer[7], 77);
}

#[test]
fn golden_unicode_fold_is_accepted() {
    let trailer = fixture("index_unicode_fold.bin");
    let index = ChunkIndex::parse_trailer(&trailer).unwrap();
    let paths: Vec<&str> = (0..index.file_count()).map(|i| index.path(i)).collect();
    assert_eq!(paths, vec!["i.txt", "k.txt", "s.txt", "\u{0130}.txt", "\u{017F}.txt", "\u{212A}.txt"]);
    assert_eq!(ChunkIndex::parse_local(section_a(&trailer)).unwrap().file_count(), 6);
}

#[test]
fn golden_plan_vectors() {
    let doc: Value = serde_json::from_slice(&fixture("plan_vectors.json")).unwrap();
    let constants = &doc["constants"];
    assert_eq!(constants["max_request_length"].as_u64().unwrap(), chunks::REQUEST_CAP);
    assert_eq!(constants["max_gap"].as_u64().unwrap(), chunks::COALESCE_GAP);
    assert_eq!(constants["transport_ceiling"].as_f64().unwrap(), chunks::TRANSPORT_CEILING);
    assert_eq!(constants["planning_hash_threshold"].as_u64().unwrap(), chunks::PLANNING_HASH_THRESHOLD);

    let vectors = doc["vectors"].as_array().unwrap();
    assert!(vectors.len() >= 10);
    for v in vectors {
        let name = v["name"].as_str().unwrap();
        let asset_size = v["asset_size"].as_u64().unwrap();
        let remote = ChunkIndex::parse_trailer(&hex(v["remote_trailer_hex"].as_str().unwrap())).unwrap();
        remote.validate_asset_size(asset_size).unwrap();
        let local = v["local_index_hex"].as_str().map(|h| ChunkIndex::parse_local(&hex(h)).unwrap());
        let unusable: HashSet<String> = v["local_unusable_paths"]
            .as_array()
            .unwrap()
            .iter()
            .map(|p| p.as_str().unwrap().to_string())
            .collect();
        let blob: HashSet<[u8; 32]> = v["blob_hashes"].as_array().unwrap().iter().map(|h| hash(h.as_str().unwrap())).collect();

        let p = plan_with(&remote, local.as_ref(), &|l, r| !unusable.contains(l.path(r.index)), &|h| {
            blob.contains(h)
        });
        let ranges = coalesce(&p);

        let e = &v["expected"];
        let needed = e["needed"].as_array().unwrap();
        assert_eq!(needed.len(), p.needed.len(), "{}: needed count", name);
        for (i, (want, got)) in needed.iter().zip(&p.needed).enumerate() {
            assert_eq!(want["chunk"].as_u64().unwrap() as usize, got.chunk_index, "{}: needed[{}].chunk", name, i);
            let file = remote.file(want["file"].as_u64().unwrap() as usize);
            assert!(
                remote.file_chunks(&file).any(|(c, _)| c == got.chunk_index),
                "{}: needed[{}].file",
                name,
                i
            );
            assert_eq!(hash(want["sha256"].as_str().unwrap()), got.sha256, "{}: needed[{}].sha256", name, i);
            assert_eq!(want["method"].as_u64().unwrap() as u8, got.method, "{}: needed[{}].method", name, i);
            assert_eq!(want["offset"].as_u64().unwrap(), got.offset, "{}: needed[{}].offset", name, i);
            assert_eq!(
                want["length"].as_u64().unwrap() as u32,
                got.compressed_length,
                "{}: needed[{}].length",
                name,
                i
            );
            assert_eq!(
                want["plain_length"].as_u64().unwrap() as u32,
                got.length,
                "{}: needed[{}].plain_length",
                name,
                i
            );
        }
        assert_eq!(
            e["needed_compressed_bytes"].as_u64().unwrap(),
            p.bytes_needed,
            "{}: compressed bytes",
            name
        );
        assert_eq!(
            e["needed_plaintext_bytes"].as_u64().unwrap(),
            p.plaintext_needed,
            "{}: plaintext bytes",
            name
        );
        assert_eq!(e["remote_file_bytes"].as_u64().unwrap(), p.bytes_total, "{}: remote bytes", name);
        assert_eq!(
            e["whole_file_copies"].as_u64().unwrap() as usize,
            p.whole_file_copies,
            "{}: whole copies",
            name
        );
        assert_eq!(
            e["classic_fallback"].as_bool().unwrap(),
            exceeds_transport_ceiling(&p, asset_size),
            "{}: fallback",
            name
        );

        let want_ranges = e["ranges"].as_array().unwrap();
        assert_eq!(want_ranges.len(), ranges.len(), "{}: range count", name);
        for (i, (want, got)) in want_ranges.iter().zip(&ranges).enumerate() {
            assert_eq!(want["offset"].as_u64().unwrap(), got.offset, "{}: ranges[{}].offset", name, i);
            assert_eq!(want["length"].as_u64().unwrap(), got.length, "{}: ranges[{}].length", name, i);
            let want_chunks: Vec<usize> = want["chunks"].as_array().unwrap().iter().map(|c| c.as_u64().unwrap() as usize).collect();
            let got_chunks: Vec<usize> = got.chunks.iter().map(|c| c.chunk_index).collect();
            assert_eq!(want_chunks, got_chunks, "{}: ranges[{}].chunks", name, i);
        }
    }
}

#[test]
fn golden_feed_with_null_pointer_parses() {
    let feed: VelopackAssetFeed = serde_json::from_slice(&fixture("feed_null_pointer.json")).unwrap();
    assert_eq!(feed.Assets.len(), 3);
    let nulls = &feed.Assets[0];
    assert_eq!(
        (nulls.ChunkIndexOffset, nulls.ChunkIndexLength, nulls.ChunkIndexSHA256.as_str()),
        (0, 0, "")
    );
    let real = &feed.Assets[1];
    assert!(real.ChunkIndexLength > 0 && real.ChunkIndexSHA256.len() == 64);
    let absent = &feed.Assets[2];
    assert_eq!(
        (absent.ChunkIndexOffset, absent.ChunkIndexLength, absent.ChunkIndexSHA256.as_str()),
        (0, 0, "")
    );
}

fn fixture_feed() -> VelopackAssetFeed {
    serde_json::from_slice(&fixture("releases.win.json")).unwrap()
}

fn zip_entry(archive: &mut zip::ZipArchive<fs::File>, name: &str) -> Vec<u8> {
    let mut entry = archive.by_name(name).unwrap();
    let mut out = Vec::new();
    entry.read_to_end(&mut out).unwrap();
    out
}

#[test]
fn vpk_packages_carry_a_trailer_matching_the_feed() {
    let feed = fixture_feed();
    for version in ["1.0.0", "1.1.0"] {
        let asset = feed.Assets.iter().find(|a| a.Version == version).unwrap();
        let path = fixture_dir().join(&asset.FileName);
        assert_eq!(fs::metadata(&path).unwrap().len(), asset.Size);
        let mut archive = zip::ZipArchive::new(fs::File::open(&path).unwrap()).unwrap();
        let last = archive.by_index(archive.len() - 1).unwrap().name().unwrap().into_owned();
        assert_eq!(last, chunks::TRAILER_ENTRY_NAME, "the trailer is the last entry");
        {
            let entry = archive.by_name(chunks::TRAILER_ENTRY_NAME).unwrap();
            assert_eq!(entry.compression(), zip::CompressionMethod::Stored);
            assert_eq!(entry.data_start(), Some(asset.ChunkIndexOffset));
            assert_eq!(entry.compressed_size(), asset.ChunkIndexLength);
        }
        let trailer = zip_entry(&mut archive, chunks::TRAILER_ENTRY_NAME);
        let all = fs::read(&path).unwrap();
        let at_pointer = &all[asset.ChunkIndexOffset as usize..(asset.ChunkIndexOffset + asset.ChunkIndexLength) as usize];
        assert_eq!(at_pointer, &trailer[..]);
        assert!(chunks::sha256_hex(&chunks::sha256(&trailer)).eq_ignore_ascii_case(&asset.ChunkIndexSHA256));

        let index = ChunkIndex::parse_trailer(&trailer).unwrap();
        index.validate_asset_size(asset.Size).unwrap();
        let sq_chunks = zip_entry(&mut archive, "lib/app/sq.chunks");
        assert_eq!(index.a_bytes(), &sq_chunks[..], "lib/app/sq.chunks is the trailer's Section A");
        ChunkIndex::parse_local(&sq_chunks).unwrap();
        assert!(index.find_file("sq.chunks").is_none());

        // every record's bytes, sliced out of the package through its B ranges, rebuild the zip entry's content
        for record in index.files().filter(|r| r.kind == FileKind::File) {
            let entry_name = format!("lib/app/{}", index.path(record.index));
            let expected = zip_entry(&mut archive, &entry_name);
            let method = index.b_file(record.index).unwrap().method;
            let mut rebuilt = Vec::new();
            for (c, chunk) in index.file_chunks(&record) {
                let b = index.b_range(c).unwrap();
                let comp = &all[b.offset as usize..(b.offset + b.length as u64) as usize];
                let plain = chunks::inflate_chunk(comp, method, chunk.length as usize).unwrap();
                assert_eq!(chunks::sha256(&plain), chunk.sha256, "{} chunk {}", entry_name, c);
                rebuilt.extend_from_slice(&plain);
            }
            assert_eq!(rebuilt, expected, "{}", entry_name);
            assert_eq!(chunks::sha256(&expected), record.sha256, "{}", entry_name);
        }
    }
}

/// Every regular file under `dir`, as sorted relative `/` paths.
fn tree(dir: &Path) -> Vec<String> {
    fn walk(base: &Path, dir: &Path, out: &mut Vec<String>) {
        for entry in fs::read_dir(dir).unwrap() {
            let path = entry.unwrap().path();
            if path.is_dir() {
                walk(base, &path, out);
            } else {
                out.push(path.strip_prefix(base).unwrap().to_string_lossy().replace('\\', "/"));
            }
        }
    }
    let mut out = Vec::new();
    walk(dir, dir, &mut out);
    out.sort();
    out
}

#[cfg(windows)]
fn classic_install(nupkg: &Path, root: &Path) {
    let bundle = velopack::bundle::load_bundle_from_file(nupkg).unwrap();
    bundle.extract_lib_contents_to_path(root.join("current"), |_| {}).unwrap();
    bundle.extract_stubs_to_dir(root).unwrap();
    let mut archive = zip::ZipArchive::new(fs::File::open(nupkg).unwrap()).unwrap();
    fs::write(root.join("Update.exe"), zip_entry(&mut archive, "lib/app/Squirrel.exe")).unwrap();
}

/// 1.0.0 installed by the classic extractor, then UpdateManager over a FileSource (ranged reads through
/// `FileRangeReader`) stages 1.1.0 through the chunk path. The feed's full-package checksums are corrupted, so a
/// classic fallback could not succeed. The staged tree must equal the classic extraction of 1.1.0.
#[cfg(windows)]
#[test]
fn vpk_packages_assemble_1_0_0_to_1_1_0_through_file_ranges() {
    use std::sync::mpsc;
    use velopack::locator::VelopackLocatorConfig;
    use velopack::sources::FileSource;
    use velopack::{UpdateCheck, UpdateManager};

    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("app");
    let packages = root.join("packages");
    let feed_dir = dir.path().join("feed");
    fs::create_dir_all(&packages).unwrap();
    fs::create_dir_all(&feed_dir).unwrap();
    classic_install(&fixture_dir().join("ChunkTestApp-1.0.0-win-full.nupkg"), &root);
    assert!(root.join("current").join("sq.chunks").exists());
    assert!(root.join("ChunkTestApp.exe").exists(), "the stub was installed at the root");

    let mut feed = fixture_feed();
    for asset in feed.Assets.iter_mut() {
        fs::copy(fixture_dir().join(&asset.FileName), feed_dir.join(&asset.FileName)).unwrap();
        asset.SHA1 = "0".repeat(40);
        asset.SHA256 = "0".repeat(64);
    }
    fs::write(feed_dir.join("releases.win.json"), serde_json::to_vec(&feed).unwrap()).unwrap();

    let config = VelopackLocatorConfig {
        RootAppDir: root.clone(),
        UpdateExePath: root.join("Update.exe"),
        PackagesDir: packages.clone(),
        ManifestPath: root.join("current").join("sq.version"),
        CurrentBinaryDir: root.join("current"),
        IsPortable: true,
    };
    let um = UpdateManager::new(FileSource::new(&feed_dir), None, Some(config)).unwrap();
    let info = match um.check_for_updates().unwrap() {
        UpdateCheck::UpdateAvailable(info) => *info,
        _ => panic!("expected an update"),
    };
    let target: VelopackAsset = info.TargetFullRelease.clone();
    assert_eq!(target.Version, "1.1.0");
    assert!(target.ChunkIndexLength > 0);

    let (tx, rx) = mpsc::channel();
    um.download_updates(&info, Some(tx)).unwrap();
    let progress: Vec<i16> = rx.iter().collect();
    assert!(progress.windows(2).all(|w| w[0] < w[1]), "progress never decreases: {:?}", progress);
    assert_eq!(progress.last(), Some(&100));

    let (staged, marker) = chunks::find_ready_staged_for(&packages, &target).expect("a ready staged tree");
    assert_eq!(marker.StagedId, chunks::staged_id(&target.ChunkIndexSHA256));
    let nupkgs: Vec<String> = tree(&packages).into_iter().filter(|p| p.ends_with(".nupkg")).collect();
    assert!(nupkgs.is_empty(), "the chunk path downloads no package: {:?}", nupkgs);
    let pending = um.get_update_pending_restart().expect("the staged tree is pending");
    assert_eq!(pending.ChunkIndexSHA256, target.ChunkIndexSHA256);

    let expected = dir.path().join("expected");
    classic_install(&fixture_dir().join("ChunkTestApp-1.1.0-win-full.nupkg"), &expected);
    let layout = chunks::StagedLayout::from_dir(&staged);
    let want = tree(&expected.join("current"));
    assert_eq!(tree(&layout.current_dir()), want);
    for rel in &want {
        assert_eq!(
            fs::read(layout.current_dir().join(rel)).unwrap(),
            fs::read(expected.join("current").join(rel)).unwrap(),
            "{}",
            rel
        );
    }
    assert_eq!(fs::read(layout.updater()).unwrap(), fs::read(expected.join("Update.exe")).unwrap());
    assert_eq!(
        fs::read(layout.stubs_dir().join("ChunkTestApp.exe")).unwrap(),
        fs::read(expected.join("ChunkTestApp.exe")).unwrap()
    );
}
