mod common;

use common::index_writer::random_bytes;
use std::fs;
use std::io::{Seek, SeekFrom, Write};
use velopack::chunks::{sha256, ChunkBlob};

#[test]
fn append_and_read() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("chunks.bin");
    let a = random_bytes(1, 1000);
    let b = random_bytes(2, 70_000);
    let (ha, hb) = (sha256(&a), sha256(&b));
    {
        let mut blob = ChunkBlob::open(&path).unwrap();
        assert!(blob.is_empty());
        assert!(!blob.contains(&ha));
        blob.append(&ha, &a).unwrap();
        blob.append(&hb, &b).unwrap();
        assert_eq!(blob.len(), 2);
        assert!(blob.contains(&ha));
        assert_eq!(blob.read(&ha).unwrap(), a);
        assert_eq!(blob.read(&hb).unwrap(), b);
        assert!(blob.read(&sha256(b"nope")).is_none());
        // appending an existing hash again keeps a single index entry
        blob.append(&ha, &a).unwrap();
        assert_eq!(blob.len(), 2);
        blob.flush().unwrap();
    }
    // record layout: "VCHK" | u32 len | sha | payload
    let bytes = fs::read(&path).unwrap();
    assert_eq!(&bytes[0..4], b"VCHK");
    assert_eq!(u32::from_le_bytes(bytes[4..8].try_into().unwrap()), 1000);
    assert_eq!(&bytes[8..40], &ha);
    assert_eq!(&bytes[40..1040], &a[..]);
    assert_eq!(
        bytes.len(),
        3 * 40 + 1000 * 2 + 70_000,
        "the duplicate append is written as a third record"
    );

    let blob = ChunkBlob::open(&path).unwrap();
    assert_eq!(blob.len(), 2);
    assert_eq!(blob.read(&hb).unwrap(), b);
    let mut hashes: Vec<[u8; 32]> = blob.hashes().copied().collect();
    hashes.sort();
    let mut expected = vec![ha, hb];
    expected.sort();
    assert_eq!(hashes, expected);
}

#[test]
fn truncated_tail_is_dropped() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("chunks.bin");
    let a = random_bytes(3, 5000);
    let b = random_bytes(4, 5000);
    {
        let mut blob = ChunkBlob::open(&path).unwrap();
        blob.append(&sha256(&a), &a).unwrap();
        blob.append(&sha256(&b), &b).unwrap();
    }
    let full_len = fs::metadata(&path).unwrap().len();
    // cut the second record in the middle of its payload
    let cut = 40 + 5000 + 40 + 1234;
    fs::OpenOptions::new().write(true).open(&path).unwrap().set_len(cut).unwrap();
    {
        let blob = ChunkBlob::open(&path).unwrap();
        assert_eq!(blob.len(), 1);
        assert!(blob.contains(&sha256(&a)));
        assert!(!blob.contains(&sha256(&b)));
    }
    assert_eq!(fs::metadata(&path).unwrap().len(), 40 + 5000);
    assert!(full_len > cut);

    // a truncated header, then a bad magic, both drop everything after the good record
    let mut f = fs::OpenOptions::new().write(true).open(&path).unwrap();
    f.seek(SeekFrom::End(0)).unwrap();
    f.write_all(b"VCHK\x10\x00").unwrap();
    drop(f);
    assert_eq!(ChunkBlob::open(&path).unwrap().len(), 1);
    assert_eq!(fs::metadata(&path).unwrap().len(), 40 + 5000);
    let mut f = fs::OpenOptions::new().write(true).open(&path).unwrap();
    f.seek(SeekFrom::End(0)).unwrap();
    f.write_all(&[0u8; 100]).unwrap();
    drop(f);
    assert_eq!(ChunkBlob::open(&path).unwrap().len(), 1);
    assert_eq!(fs::metadata(&path).unwrap().len(), 40 + 5000);
    // appending after a truncation continues at the new end
    let c = random_bytes(5, 10);
    let mut blob = ChunkBlob::open(&path).unwrap();
    blob.append(&sha256(&c), &c).unwrap();
    drop(blob);
    let blob = ChunkBlob::open(&path).unwrap();
    assert_eq!(blob.len(), 2);
    assert_eq!(blob.read(&sha256(&c)).unwrap(), c);
}

#[test]
fn corrupt_payload_is_not_indexed_and_scan_continues() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("chunks.bin");
    let a = random_bytes(6, 3000);
    let b = random_bytes(7, 3000);
    let c = random_bytes(8, 3000);
    {
        let mut blob = ChunkBlob::open(&path).unwrap();
        blob.append(&sha256(&a), &a).unwrap();
        blob.append(&sha256(&b), &b).unwrap();
        blob.append(&sha256(&c), &c).unwrap();
    }
    // the crash tail: a flushed header with a zeroed payload (record b)
    let mut f = fs::OpenOptions::new().write(true).open(&path).unwrap();
    f.seek(SeekFrom::Start(40 + 3000 + 40)).unwrap();
    f.write_all(&[0u8; 3000]).unwrap();
    drop(f);

    let blob = ChunkBlob::open(&path).unwrap();
    assert_eq!(blob.len(), 2, "the corrupt record is skipped, the scan continues");
    assert!(blob.contains(&sha256(&a)));
    assert!(!blob.contains(&sha256(&b)));
    assert!(blob.contains(&sha256(&c)));
    assert_eq!(blob.read(&sha256(&c)).unwrap(), c);
    assert_eq!(fs::metadata(&path).unwrap().len(), 3 * (40 + 3000), "nothing is truncated");
    drop(blob);

    // re-appending b after the corrupt record wins: first *verified* record wins at open
    let mut blob = ChunkBlob::open(&path).unwrap();
    blob.append(&sha256(&b), &b).unwrap();
    assert_eq!(blob.read(&sha256(&b)).unwrap(), b);
    drop(blob);
    let blob = ChunkBlob::open(&path).unwrap();
    assert_eq!(blob.len(), 3);
    assert_eq!(blob.read(&sha256(&b)).unwrap(), b);
}
