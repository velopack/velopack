mod common;

use common::index_writer::*;
use velopack::chunks::{self, staged_id, ChunkIndex, Disposition, FileKind};

fn sample_files() -> Vec<TestFile> {
    vec![
        TestFile::file("app.exe", random_bytes(1, 200_000)),
        TestFile::file("data/config.json", b"{\"a\":1}".to_vec()),
        TestFile::file("data/empty.txt", Vec::new()),
        TestFile::updater(random_bytes(2, 70_000)),
        TestFile::stub("MyApp", random_bytes(3, 1000)),
        TestFile::symlink("links/cfg.json", "../data/config.json"),
        TestFile::empty_dir("plugins/empty"),
    ]
}

fn sample() -> BuiltIndex {
    build_index(
        &sample_files(),
        &TestIndexOptions {
            chunk_size: 65536,
            ..Default::default()
        },
    )
}

#[test]
fn parses_sample() {
    let built = sample();
    let index = ChunkIndex::parse_trailer(&built.trailer()).unwrap();
    assert_eq!(index.file_count(), 7);
    assert_eq!(index.chunk_count(), 4 + 1 + 2 + 1);
    assert_eq!(index.a_bytes(), &built.a[..]);
    assert_eq!(index.b_bytes().unwrap(), &built.b[..]);
    assert!(index.has_transport());

    let paths: Vec<&str> = (0..index.file_count()).map(|i| index.path(i)).collect();
    assert_eq!(
        paths,
        vec![
            "MyApp_ExecutionStub.exe",
            "Squirrel.exe",
            "app.exe",
            "data/config.json",
            "data/empty.txt",
            "links/cfg.json",
            "plugins/empty"
        ]
    );

    let app = index.file(index.find_file("app.exe").unwrap());
    assert_eq!(app.kind, FileKind::File);
    assert_eq!(app.disposition, Disposition::AppFile);
    assert_eq!(app.length, 200_000);
    assert_eq!(app.chunk_count, 4);
    assert_eq!(index.file_chunks(&app).map(|(_, c)| c.length as u64).sum::<u64>(), 200_000);
    assert_eq!(app.sha256, chunks::sha256(&random_bytes(1, 200_000)));
    let b = index.b_file(app.index).unwrap();
    assert_eq!(b.method, 8);
    let ranges: Vec<_> = index.file_chunks(&app).map(|(c, _)| index.b_range(c).unwrap()).collect();
    assert_eq!(ranges[0].offset, b.data_offset);
    assert_eq!(ranges[3].offset + ranges[3].length as u64 + 2, b.data_offset + b.compressed_size);

    let updater = index.updater_record().unwrap();
    assert_eq!(index.path(updater.index), "Squirrel.exe");
    assert_eq!(index.installed_name(&updater), "Update.exe");
    let stub = index.file(index.find_file("MyApp_ExecutionStub.exe").unwrap());
    assert_eq!(stub.disposition, Disposition::Stub);
    assert_eq!(index.installed_name(&stub), "MyApp.exe");

    let empty = index.file(index.find_file("data/empty.txt").unwrap());
    assert_eq!(empty.chunk_count, 0);
    assert_eq!(empty.sha256, chunks::sha256(b""));

    let link = index.file(index.find_file("links/cfg.json").unwrap());
    assert_eq!(link.kind, FileKind::Symlink);
    assert_eq!(index.symlink_target(link.index).as_deref(), Some("../data/config.json"));
    let dir = index.file(index.find_file("plugins/empty").unwrap());
    assert_eq!(dir.kind, FileKind::EmptyDir);
    assert!(index.find_file("nope").is_none());
    assert_eq!(index.total_file_bytes(), 200_000 + 7 + 70_000 + 1000);

    let local = ChunkIndex::parse_local(&built.a).unwrap();
    assert!(!local.has_transport());
    assert!(local.b_file(0).is_none());
    assert_eq!(local.file_count(), 7);

    index.validate_asset_size(1 << 40).unwrap();
    assert!(index.validate_asset_size(1000).is_err());
}

fn assert_rejects(name: &str, trailer: &[u8]) {
    let result = ChunkIndex::parse_trailer(trailer);
    assert!(result.is_err(), "{} should be rejected", name);
}

fn trailer_of(files: Vec<TestFile>) -> Vec<u8> {
    build_index(&files, &TestIndexOptions::default()).trailer()
}

#[test]
fn rejects_each_invalid() {
    let valid = sample().trailer();
    assert!(ChunkIndex::parse_trailer(&valid).is_ok());

    // truncated / extended
    assert_rejects("truncated", &valid[..valid.len() - 1]);
    assert_rejects("too short", &valid[..40]);
    let mut extended = valid.clone();
    extended.push(0);
    assert_rejects("trailing byte", &extended);
    let mut local_with_b = valid.clone();
    local_with_b.truncate(sample().a.len() + 1);
    assert!(ChunkIndex::parse_local(&local_with_b).is_err(), "local index with trailing bytes");

    // header fields
    let mut t = valid.clone();
    t[0] = b'X';
    assert_rejects("bad A magic", &t);
    let mut t = valid.clone();
    t[4] = 2;
    assert_rejects("format version 2", &t);
    let mut t = valid.clone();
    t[6] = 2;
    assert_rejects("hash algo 2", &t);
    let mut t = valid.clone();
    t[24..28].copy_from_slice(&u32::MAX.to_le_bytes());
    assert_rejects("count overflow", &t);
    let a_len = sample().a.len();
    let mut t = valid.clone();
    t[a_len] = b'X';
    assert_rejects("bad B magic", &t);
    let mut t = valid.clone();
    t[a_len + 4] = 2;
    assert_rejects("B version", &t);
    let mut t = valid.clone();
    t[a_len + 8] = 9;
    assert_rejects("B counts", &t);

    // non-contiguous chunks: swap first_chunk of the two chunked files
    let built = sample();
    let index = ChunkIndex::parse_trailer(&built.trailer()).unwrap();
    let records_off = 48 + (built.a.len() - 48 - 64 * index.file_count() - 36 * index.chunk_count());
    let mut t = built.trailer();
    let app_record = records_off + 64 * index.find_file("app.exe").unwrap();
    t[app_record + 16..app_record + 20].copy_from_slice(&1u32.to_le_bytes());
    assert_rejects("non-contiguous chunks", &t);
    let mut t = built.trailer();
    t[app_record + 8..app_record + 16].copy_from_slice(&199_999u64.to_le_bytes());
    assert_rejects("length != sum of chunks", &t);
    let mut t = built.trailer();
    t[app_record + 7] = 7;
    assert_rejects("unknown kind", &t);
    let mut t = built.trailer();
    t[app_record + 6] = 3;
    assert_rejects("unknown disposition", &t);
    let chunks_off = records_off + 64 * index.file_count();
    let mut t = built.trailer();
    t[chunks_off..chunks_off + 4].copy_from_slice(&0u32.to_le_bytes());
    assert_rejects("zero-length chunk", &t);
    let mut t = built.trailer();
    t[chunks_off..chunks_off + 4].copy_from_slice(&(16 * 1024 * 1024 + 1u32).to_le_bytes());
    assert_rejects("chunk > 16 MiB", &t);

    // B inconsistencies
    let b_off = built.a.len() + 16 + 24 * index.file_count();
    let mut t = built.trailer();
    t[b_off + 8..b_off + 12].copy_from_slice(&0u32.to_le_bytes());
    assert_rejects("B chunk length 0", &t);
    let mut t = built.trailer();
    t[b_off..b_off + 8].copy_from_slice(&5u64.to_le_bytes());
    assert_rejects("B chunk not contiguous", &t);
    let b_file_off = built.a.len() + 16 + 24 * index.find_file("app.exe").unwrap();
    let mut t = built.trailer();
    t[b_file_off] = 9;
    assert_rejects("unknown method", &t);
    let mut t = built.trailer();
    t[b_file_off + 16..b_file_off + 24].copy_from_slice(&1u64.to_le_bytes());
    assert_rejects("compressed size mismatch", &t);
    let stored = build_index(
        &sample_files(),
        &TestIndexOptions {
            stored: true,
            ..Default::default()
        },
    );
    assert!(ChunkIndex::parse_trailer(&stored.trailer()).is_ok());
    let mut t = stored.trailer();
    t[b_off..b_off + 8].copy_from_slice(&5u64.to_le_bytes());
    assert_rejects("stored chunk offset mismatch", &t);

    // record rules
    let unsorted = build_index(
        &[TestFile::file("b.txt", b"b".to_vec()), TestFile::file("a.txt", b"a".to_vec())],
        &TestIndexOptions {
            keep_order: true,
            ..Default::default()
        },
    );
    assert_rejects("unsorted records", &unsorted.trailer());
    assert_rejects(
        "duplicate path",
        &trailer_of(vec![TestFile::file("a.txt", b"a".to_vec()), TestFile::file("a.txt", b"b".to_vec())]),
    );
    assert_rejects(
        "ascii-fold duplicate",
        &trailer_of(vec![TestFile::file("A.txt", b"a".to_vec()), TestFile::file("a.txt", b"b".to_vec())]),
    );
    assert_rejects(
        "dir prefix",
        &trailer_of(vec![TestFile::file("a", b"a".to_vec()), TestFile::file("a/b", b"b".to_vec())]),
    );
    assert_rejects(
        "empty dir prefix",
        &trailer_of(vec![TestFile::empty_dir("a"), TestFile::file("a/b", b"b".to_vec())]),
    );
    for bad in [
        "../x.txt",
        "a/../b.txt",
        "./a.txt",
        "/a.txt",
        "a.txt/",
        "a//b.txt",
        "a\\b.txt",
        "a:b.txt",
        "a?.txt",
        "a*.txt",
        "a\"b.txt",
        "a<b.txt",
        "a>b.txt",
        "a|b.txt",
        "trailing.",
        "trailing ",
        "dir./x.txt",
        "con",
        "CON.txt",
        "com1.dll",
        "x/LPT9",
        "conin$.txt",
        "a\x01b",
        "a\x7fb",
        "sq.chunks",
        "SQ.CHUNKS",
    ] {
        assert_rejects(bad, &trailer_of(vec![TestFile::file(bad, b"x".to_vec())]));
    }
    assert_rejects("empty path", &trailer_of(vec![TestFile::file("", b"x".to_vec())]));
    assert_rejects("path too long", &trailer_of(vec![TestFile::file(&"a".repeat(1025), b"x".to_vec())]));
    assert_rejects(
        "segment too long",
        &trailer_of(vec![TestFile::file(&format!("{}/x", "a".repeat(256)), b"x".to_vec())]),
    );
    let mut t = trailer_of(vec![TestFile::file("ab.txt", b"x".to_vec())]);
    t[48] = 0xFF; // invalid utf-8 in the path blob
    assert_rejects("invalid utf-8", &t);

    // disposition rules
    assert_rejects(
        "nested Squirrel.exe",
        &trailer_of(vec![TestFile::file("Plugins/Squirrel.exe", b"x".to_vec())]),
    );
    assert_rejects("suffix Squirrel.exe", &trailer_of(vec![TestFile::file("NotSquirrel.exe", b"x".to_vec())]));
    assert_rejects(
        "nested stub",
        &trailer_of(vec![TestFile::file("sub/App_ExecutionStub.exe", b"x".to_vec())]),
    );
    assert_rejects(
        "top-level stub as app file",
        &trailer_of(vec![TestFile::file("App_ExecutionStub.exe", b"x".to_vec())]),
    );
    assert_rejects("stub disposition nested", &trailer_of(vec![TestFile::stub("sub/App", b"x".to_vec())]));
    assert_rejects(
        "stub without suffix",
        &trailer_of(vec![TestFile::file("App.exe", b"x".to_vec()).with_disposition(2)]),
    );
    assert_rejects(
        "stub with wrong case suffix",
        &trailer_of(vec![TestFile::file("App_executionstub.exe", b"x".to_vec()).with_disposition(2)]),
    );
    assert_rejects(
        "updater wrong path",
        &trailer_of(vec![TestFile::file("Update.exe", b"x".to_vec()).with_disposition(1)]),
    );
    assert_rejects(
        "two updaters",
        &trailer_of(vec![
            TestFile::updater(b"x".to_vec()),
            TestFile::file("x/Squirrel.exe", b"x".to_vec()).with_disposition(1),
        ]),
    );
    assert_rejects("updater as app file", &trailer_of(vec![TestFile::file("Squirrel.exe", b"x".to_vec())]));
    assert_rejects(
        "symlink as updater",
        &trailer_of(vec![TestFile::symlink("Squirrel.exe", "x").with_disposition(1)]),
    );

    // symlink targets
    for target in ["../../x", "../x/../../y", "/abs", "C:/abs", "a//b", "a|b", "bad.", ""] {
        assert_rejects(
            &format!("symlink target {:?}", target),
            &trailer_of(vec![TestFile::symlink("links/l", target)]),
        );
    }
    assert_rejects("symlink target escapes via sibling dir", &trailer_of(vec![TestFile::symlink("l", "..")]));
    let mut t = trailer_of(vec![TestFile::symlink("links/l", "x")]);
    t[48 + 7] = 0xFF; // invalid utf-8 in the target
    assert_rejects("symlink target invalid utf-8", &t);
    assert!(ChunkIndex::parse_trailer(&trailer_of(vec![TestFile::symlink("links/l", "../x/./y")])).is_ok());
    assert!(ChunkIndex::parse_trailer(&trailer_of(vec![TestFile::symlink("links/l", "dir/")])).is_ok());
    assert!(ChunkIndex::parse_trailer(&trailer_of(vec![TestFile::symlink("links/l", "..\\x")])).is_ok());
}

#[test]
fn accepts_unknown_cdc_algo() {
    let mut t = sample().trailer();
    t[7] = 42;
    t[8..12].copy_from_slice(&0xFFFF_FFFFu32.to_le_bytes()); // unknown flags are ignored too
    t[12..16].copy_from_slice(&1u32.to_le_bytes());
    t[36..48].copy_from_slice(&[7u8; 12]); // reserved bytes are ignored
    assert!(ChunkIndex::parse_trailer(&t).is_ok());
}

#[test]
fn accepts_unicode_fold_sample() {
    // these differ only under Unicode case folding, which the index rules never apply
    let files = vec![
        TestFile::file("s.txt", b"1".to_vec()),
        TestFile::file("ſ.txt", b"2".to_vec()),
        TestFile::file("k.txt", b"3".to_vec()),
        TestFile::file("\u{212A}.txt", b"4".to_vec()),
        TestFile::file("i.txt", b"5".to_vec()),
        TestFile::file("İ.txt", b"6".to_vec()),
    ];
    let index = ChunkIndex::parse_trailer(&trailer_of(files)).unwrap();
    assert_eq!(index.file_count(), 6);
}

#[test]
fn segment_length_counts_utf16_code_units() {
    // NTFS allows 255 UTF-16 code units per name, however many UTF-8 bytes that is
    let cjk = format!("docs/{}.pdf", "文".repeat(90)); // 94 code units, 274 bytes
    let umlauts = "ü".repeat(255); // 255 code units, 510 bytes
    let index = ChunkIndex::parse_trailer(&trailer_of(vec![
        TestFile::file(&cjk, b"1".to_vec()),
        TestFile::file(&umlauts, b"2".to_vec()),
    ]))
    .unwrap();
    assert_eq!(index.file_count(), 2);
    assert_rejects(
        "256 two-byte characters",
        &trailer_of(vec![TestFile::file(&"ü".repeat(256), b"x".to_vec())]),
    );
    // a character outside the BMP is two code units: 128 of them are 256
    assert_rejects("128 surrogate pairs", &trailer_of(vec![TestFile::file(&"😀".repeat(128), b"x".to_vec())]));
    assert!(ChunkIndex::parse_trailer(&trailer_of(vec![TestFile::file(&"😀".repeat(127), b"x".to_vec())])).is_ok());
}

#[test]
fn staged_id_is_first_32_hex_lower() {
    let sha = "ABCDEF0123456789ABCDEF0123456789abcdef0123456789ABCDEF0123456789";
    assert_eq!(staged_id(sha).as_deref(), Some("abcdef0123456789abcdef0123456789"));
    let built = sample();
    assert_eq!(Some(built.staged_id()), staged_id(&built.digest_hex()));
    assert_eq!(built.staged_id().len(), 32);
}

#[test]
fn staged_id_rejects_anything_but_64_hex() {
    let hex = "ab".repeat(32);
    assert!(staged_id(&hex).is_some());
    let bad = [
        String::new(),
        "ABC".to_string(),
        hex[..63].to_string(),
        format!("{}0", hex),
        format!("../../../../Desktop/{}", &hex[..44]),
        format!("C:/Users/Public/{}", &hex[..48]),
        format!("+f{}", &hex[..62]),
        format!(" {}", &hex[..63]),
        format!("a\u{e9}{}", &hex[..61]),
    ];
    for value in &bad {
        assert!(staged_id(value).is_none(), "{:?}", value);
        assert!(chunks::parse_sha256_hex(value).is_none(), "{:?}", value);
    }
}

#[test]
fn caps_checked_before_allocation() {
    let mut header = vec![0u8; 64];
    header[0..4].copy_from_slice(b"VPKA");
    header[4..6].copy_from_slice(&1u16.to_le_bytes());
    header[6] = 1;
    header[7] = 1;
    header[24..28].copy_from_slice(&u32::MAX.to_le_bytes());
    header[28..32].copy_from_slice(&u32::MAX.to_le_bytes());
    header[32..36].copy_from_slice(&u32::MAX.to_le_bytes());
    let start = std::time::Instant::now();
    assert!(ChunkIndex::parse_trailer(&header).is_err());
    assert!(ChunkIndex::parse_local(&header[..48]).is_err());
    // just over the caps but a tiny buffer: must fail on the counts, not by reading past the end
    header[24..28].copy_from_slice(&(chunks::MAX_FILES as u32 + 1).to_le_bytes());
    header[28..32].copy_from_slice(&0u32.to_le_bytes());
    header[32..36].copy_from_slice(&0u32.to_le_bytes());
    assert!(ChunkIndex::parse_trailer(&header).is_err());
    header[24..28].copy_from_slice(&1000u32.to_le_bytes());
    assert!(ChunkIndex::parse_trailer(&header).is_err());
    assert!(start.elapsed().as_millis() < 1000);
}
