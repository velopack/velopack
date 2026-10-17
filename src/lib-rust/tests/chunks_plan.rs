mod common;

use common::index_writer::*;
use std::collections::HashSet;
use std::fs;
use std::path::Path;
use velopack::chunks::{self, coalesce, plan, plan_for_hashes, ChunkIndex, LocalRoots, NeededChunk, Plan};

const CHUNK: usize = 64 * 1024;

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
    let insert = random_bytes(9, 1000);
    big.splice(200_000..200_000, insert);
    vec![
        TestFile::file("app.exe", random_bytes(1, 300_000)),
        TestFile::file("lib/big.bin", big),
        TestFile::file("small.txt", random_bytes(3, 100)),
        TestFile::file("sq.version", b"<version>2.0.0</version>".to_vec()),
        TestFile::file("renamed.bin", random_bytes(4, 150_000)),
        TestFile::file("new.bin", random_bytes(7, 100_000)),
        TestFile::file("copy-of-new.bin", random_bytes(7, 100_000)),
        TestFile::file("empty.txt", Vec::new()),
        TestFile::updater(random_bytes(8, 70_000)),
        TestFile::stub("App", random_bytes(6, 1000)),
    ]
}

fn opts() -> TestIndexOptions {
    TestIndexOptions {
        chunk_size: CHUNK,
        ..Default::default()
    }
}

struct Setup {
    _dir: tempfile::TempDir,
    roots: LocalRoots,
    v1: BuiltIndex,
    v2: BuiltIndex,
    local: ChunkIndex,
    remote: ChunkIndex,
}

fn setup() -> Setup {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("app");
    let v1 = build_index(&v1_files(), &opts());
    let v2 = build_index(&v2_files(), &opts());
    write_installed_view(&root, &v1_files(), &v1);
    let roots = LocalRoots {
        current_dir: root.join("current"),
        updater_path: root.join("Update.exe"),
        root_dir: root.clone(),
    };
    let local = ChunkIndex::parse_local(&v1.a).unwrap();
    let remote = ChunkIndex::parse_trailer(&v2.trailer()).unwrap();
    Setup {
        _dir: dir,
        roots,
        v1,
        v2,
        local,
        remote,
    }
}

fn hashes(chunks: &[NeededChunk]) -> HashSet<[u8; 32]> {
    chunks.iter().map(|c| c.sha256).collect()
}

/// The reference plan: every v2 chunk whose hash is in no usable v1 file, no usable v1 chunk and not in the blob.
fn expected_needed(s: &Setup, usable_v1_paths: &[&str], blob: &HashSet<[u8; 32]>) -> HashSet<[u8; 32]> {
    let have_files: HashSet<[u8; 32]> =
        s.v1.files
            .iter()
            .filter(|f| usable_v1_paths.contains(&f.path.as_str()))
            .map(|f| chunks::sha256(&f.data))
            .collect();
    let have: HashSet<[u8; 32]> =
        s.v1.chunks
            .iter()
            .filter(|c| usable_v1_paths.contains(&c.file_path.as_str()))
            .map(|c| c.sha256)
            .collect();
    let mut needed = HashSet::new();
    for f in &s.v2.files {
        if f.kind != 0 || f.data.is_empty() || have_files.contains(&chunks::sha256(&f.data)) {
            continue;
        }
        for c in s.v2.chunks.iter().filter(|c| c.file_path == f.path) {
            if !have.contains(&c.sha256) && !blob.contains(&c.sha256) {
                needed.insert(c.sha256);
            }
        }
    }
    needed
}

fn all_v1_paths() -> Vec<&'static str> {
    vec![
        "app.exe",
        "lib/big.bin",
        "small.txt",
        "sq.version",
        "gone.bin",
        "Squirrel.exe",
        "App_ExecutionStub.exe",
    ]
}

#[test]
fn plan_vectors() {
    let s = setup();
    let empty = HashSet::new();
    let p = plan(&s.remote, Some(&s.local), &s.roots, &|_| false);
    let expected = expected_needed(&s, &all_v1_paths(), &empty);
    assert_eq!(hashes(&p.needed), expected);
    assert_eq!(p.needed.len(), expected.len(), "needed is deduplicated by hash");
    assert_eq!(p.bytes_total, s.remote.total_file_bytes());
    assert_eq!(p.bytes_needed, p.needed.iter().map(|c| c.compressed_length as u64).sum::<u64>());
    assert_eq!(p.plaintext_needed, p.needed.iter().map(|c| c.length as u64).sum::<u64>());

    // the modified big file keeps its first 3 chunks (192 KiB before the insertion point) and the renamed
    // file costs nothing; new.bin and copy-of-new.bin share chunks
    let big_v2: Vec<_> = s.v2.chunks.iter().filter(|c| c.file_path == "lib/big.bin").collect();
    for c in &big_v2[..3] {
        assert!(!expected.contains(&c.sha256));
    }
    for c in &big_v2[3..] {
        assert!(expected.contains(&c.sha256));
    }
    assert!(s
        .v2
        .chunks
        .iter()
        .filter(|c| c.file_path == "renamed.bin")
        .all(|c| !expected.contains(&c.sha256)));
    let new_chunks: Vec<_> = s.v2.chunks.iter().filter(|c| c.file_path == "new.bin").map(|c| c.sha256).collect();
    assert_eq!(p.needed.iter().filter(|c| new_chunks.contains(&c.sha256)).count(), new_chunks.len());
    // the updater changed and is needed; the stub did not
    assert!(s
        .v2
        .chunks
        .iter()
        .filter(|c| c.file_path == "Squirrel.exe")
        .all(|c| expected.contains(&c.sha256)));
    assert!(s
        .v2
        .chunks
        .iter()
        .filter(|c| c.file_path == "App_ExecutionStub.exe")
        .all(|c| !expected.contains(&c.sha256)));
    // every needed chunk carries its transport range
    for c in &p.needed {
        let built = s.v2.chunks.iter().find(|b| b.sha256 == c.sha256).unwrap();
        assert_eq!(
            (c.offset, c.compressed_length, c.length, c.method),
            (built.offset, built.length, built.plain.len() as u32, 8)
        );
    }

    // chunks already in the blob are excluded
    let blob: HashSet<[u8; 32]> = big_v2[3..5].iter().map(|c| c.sha256).collect();
    let p2 = plan(&s.remote, Some(&s.local), &s.roots, &|h| blob.contains(h));
    assert_eq!(hashes(&p2.needed), expected_needed(&s, &all_v1_paths(), &blob));
    assert_eq!(p2.needed.len(), p.needed.len() - 2);

    // without a local index everything not in the blob is needed
    let p3 = plan(&s.remote, None, &s.roots, &|_| false);
    assert_eq!(hashes(&p3.needed), expected_needed(&s, &[], &empty));
    assert_eq!(
        p3.bytes_needed,
        s.v2.chunks.iter().map(|c| c.length as u64).sum::<u64>() - {
            // copy-of-new.bin duplicates new.bin
            s.v2.chunks
                .iter()
                .filter(|c| c.file_path == "copy-of-new.bin")
                .map(|c| c.length as u64)
                .sum::<u64>()
        }
    );

    // a missing round plans exactly the given hashes, in index order
    let wanted = vec![big_v2[4].sha256, big_v2[3].sha256, [9u8; 32]];
    let p4 = plan_for_hashes(&s.remote, &wanted);
    assert_eq!(
        p4.needed.iter().map(|c| c.sha256).collect::<Vec<_>>(),
        vec![big_v2[3].sha256, big_v2[4].sha256]
    );
    assert_eq!(p4.bytes_total, p.bytes_total);
}

fn needed(offset: u64, compressed_length: u32) -> NeededChunk {
    NeededChunk {
        chunk_index: 0,
        sha256: [0; 32],
        length: compressed_length,
        method: 8,
        offset,
        compressed_length,
    }
}

fn plan_of(chunks: Vec<NeededChunk>) -> Plan {
    let bytes_needed = chunks.iter().map(|c| c.compressed_length as u64).sum();
    Plan {
        needed: chunks,
        bytes_needed,
        plaintext_needed: bytes_needed,
        bytes_total: 0,
    }
}

#[test]
fn coalesce_gap_and_size_limits() {
    let gap = chunks::COALESCE_GAP;
    let cap = chunks::REQUEST_CAP;
    // unsorted input; gap exactly 32 KiB merges, one byte more does not
    let p = plan_of(vec![
        needed(1000 + gap + 100, 50),
        needed(0, 1000),
        needed(1000 + gap, 100),
        needed(1000 + gap + 100 + 50 + gap + 1, 10),
    ]);
    let reqs = coalesce(&p);
    assert_eq!(reqs.len(), 2);
    assert_eq!((reqs[0].offset, reqs[0].length), (0, 1000 + gap + 150));
    assert_eq!(
        reqs[0].chunks.iter().map(|c| c.offset).collect::<Vec<_>>(),
        vec![0, 1000 + gap, 1000 + gap + 100]
    );
    assert_eq!((reqs[1].offset, reqs[1].length), (1000 + gap + 100 + 50 + gap + 1, 10));

    // the 8 MiB cap limits merging: three 3 MiB chunks back to back become 2 requests
    let three_mib = 3 * 1024 * 1024u32;
    let p = plan_of(vec![
        needed(0, three_mib),
        needed(three_mib as u64, three_mib),
        needed(2 * three_mib as u64, three_mib),
    ]);
    let reqs = coalesce(&p);
    assert_eq!(reqs.len(), 2);
    assert_eq!(reqs[0].length, 2 * three_mib as u64);
    assert_eq!(reqs[1].length, three_mib as u64);
    // exactly the cap merges
    let p = plan_of(vec![needed(0, (cap / 2) as u32), needed(cap / 2, (cap / 2) as u32)]);
    assert_eq!(coalesce(&p).len(), 1);
    let p = plan_of(vec![needed(0, (cap / 2) as u32), needed(cap / 2, (cap / 2) as u32 + 1)]);
    assert_eq!(coalesce(&p).len(), 2);
    assert!(coalesce(&plan_of(vec![])).is_empty());
}

#[test]
fn lone_chunk_larger_than_request_cap_is_scheduled() {
    let big = (chunks::REQUEST_CAP + 1) as u32;
    let p = plan_of(vec![needed(100, 10), needed(5000, big), needed(5000 + big as u64 + 10, 10)]);
    let reqs = coalesce(&p);
    assert_eq!(reqs.len(), 3);
    assert_eq!((reqs[1].offset, reqs[1].length), (5000, big as u64));
    assert_eq!(reqs[1].chunks.len(), 1);
    // the small neighbours are not merged into the oversized request
    assert_eq!(reqs[2].offset, 5000 + big as u64 + 10);
}

#[test]
fn transport_ceiling_falls_back() {
    let p = plan_of(vec![needed(0, 90)]);
    assert!(chunks::exceeds_transport_ceiling(&p, 100));
    let p = plan_of(vec![needed(0, 89)]);
    assert!(!chunks::exceeds_transport_ceiling(&p, 100));
    let p = plan_of(vec![]);
    assert!(!chunks::exceeds_transport_ceiling(&p, 100));
    assert!(chunks::exceeds_transport_ceiling(&p, 0));
}

#[test]
fn local_file_with_wrong_length_is_not_a_source() {
    let s = setup();
    let app = s.roots.current_dir.join("app.exe");
    let mut data = fs::read(&app).unwrap();
    data.pop();
    fs::write(&app, &data).unwrap();
    let p = plan(&s.remote, Some(&s.local), &s.roots, &|_| false);
    let usable: Vec<&str> = all_v1_paths().into_iter().filter(|p| *p != "app.exe").collect();
    assert_eq!(hashes(&p.needed), expected_needed(&s, &usable, &HashSet::new()));
    assert!(s
        .v2
        .chunks
        .iter()
        .filter(|c| c.file_path == "app.exe")
        .all(|c| hashes(&p.needed).contains(&c.sha256)));

    // a deleted file is not a source either
    fs::remove_file(s.roots.current_dir.join("lib").join("big.bin")).unwrap();
    let p = plan(&s.remote, Some(&s.local), &s.roots, &|_| false);
    let usable: Vec<&str> = usable.into_iter().filter(|p| *p != "lib/big.bin").collect();
    assert_eq!(hashes(&p.needed), expected_needed(&s, &usable, &HashSet::new()));
    // the stub and updater resolve through their own roots
    assert!(chunks::local_file_usable(
        &s.roots.root_dir.join("App.exe"),
        &s.local.file(s.local.find_file("App_ExecutionStub.exe").unwrap())
    ));
    assert!(chunks::local_file_usable(&s.roots.updater_path, &s.local.updater_record().unwrap()));
    assert!(!chunks::local_file_usable(
        Path::new("does-not-exist"),
        &s.local.updater_record().unwrap()
    ));
}

#[test]
fn small_local_file_is_hashed_during_planning() {
    let s = setup();
    // same length, different content: a small file is hashed and rejected as a source...
    let small = s.roots.current_dir.join("small.txt");
    let mut data = fs::read(&small).unwrap();
    data[0] ^= 0xFF;
    fs::write(&small, &data).unwrap();
    // ...while a large file is only length-checked (assembly verifies every chunk anyway)
    let app = s.roots.current_dir.join("app.exe");
    let mut data = fs::read(&app).unwrap();
    data[123] ^= 0xFF;
    fs::write(&app, &data).unwrap();

    let p = plan(&s.remote, Some(&s.local), &s.roots, &|_| false);
    let usable: Vec<&str> = all_v1_paths().into_iter().filter(|p| *p != "small.txt").collect();
    assert_eq!(hashes(&p.needed), expected_needed(&s, &usable, &HashSet::new()));
    let small_hash = s.v2.chunks.iter().find(|c| c.file_path == "small.txt").unwrap().sha256;
    assert!(hashes(&p.needed).contains(&small_hash));
    assert!(s
        .v2
        .chunks
        .iter()
        .filter(|c| c.file_path == "app.exe")
        .all(|c| !hashes(&p.needed).contains(&c.sha256)));
    assert_eq!(chunks::PLANNING_HASH_THRESHOLD, 64 * 1024);
}
