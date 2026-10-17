//! Ranged fetch with fault injection (§12.3): the fetcher's retry policy and budgets, and the HTTP range
//! reader's strict 206 handling, redirect caching and single-flight URL refresh, against the mock server.
//! No test sleeps: the policy's sleep and clock are injected.

mod common;

use common::index_writer::*;
use common::mock_server::{Fault, LoggedRequest, MockHttpServer, IGNORE_RANGE_BODY_LEN};
use common::package::*;
use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use velopack::chunks::{
    self, coalesce, fetch_index, fetch_ranges, plan, ChunkBlob, ChunkIndex, FetchError, LocalRoots, RangeError, RangeReader, RangeRequest,
    RetryPolicy,
};
use velopack::download::HttpHeader;
use velopack::download::{create_range_agent, HttpRangeReader};
use velopack::sources::{FileSource, UpdateSource};

const PKG_PATH: &str = "/pkg.nupkg";

fn test_files() -> Vec<TestFile> {
    vec![
        TestFile::file("a.bin", compressible(1, 300_000)),
        TestFile::file("b/c.bin", compressible(2, 500_000)),
        TestFile::file("d.txt", compressible(3, 20_000)),
        TestFile::updater(compressible(4, 70_000)),
    ]
}

fn one_chunk_files() -> Vec<TestFile> {
    vec![TestFile::file("one.bin", compressible(5, 30_000)), TestFile::updater(Vec::new())]
}

fn many_chunk_files() -> Vec<TestFile> {
    vec![
        TestFile::file("x.bin", compressible(6, 3_200_000)),
        TestFile::file("y.bin", compressible(7, 3_200_000)),
        TestFile::updater(compressible(4, 70_000)),
    ]
}

fn dummy_roots(dir: &std::path::Path) -> LocalRoots {
    LocalRoots {
        current_dir: dir.join("current"),
        updater_path: dir.join("Update.exe"),
        root_dir: dir.to_path_buf(),
    }
}

struct Harness {
    server: MockHttpServer,
    pkg: Package,
    remote: ChunkIndex,
    dir: tempfile::TempDir,
    blob: Mutex<ChunkBlob>,
    sleeps: Arc<Mutex<Vec<Duration>>>,
    clock: Arc<Mutex<Duration>>,
}

impl Harness {
    fn new(files: &[TestFile]) -> Harness {
        let server = MockHttpServer::empty();
        let pkg = build_package(files, "pkg.nupkg", "2.0.0");
        server.add_file(PKG_PATH, pkg.bytes.clone());
        let remote = ChunkIndex::parse_trailer(&pkg.built.trailer()).unwrap();
        let dir = tempfile::tempdir().unwrap();
        let blob = Mutex::new(ChunkBlob::open(dir.path().join("chunks.bin")).unwrap());
        Harness {
            server,
            pkg,
            remote,
            dir,
            blob,
            sleeps: Arc::new(Mutex::new(Vec::new())),
            clock: Arc::new(Mutex::new(Duration::ZERO)),
        }
    }

    fn reader_at(&self, path: &str) -> HttpRangeReader {
        HttpRangeReader::open(create_range_agent(), &format!("{}{}", self.server.url(), path), &[]).unwrap()
    }

    fn reader(&self) -> HttpRangeReader {
        self.reader_at(PKG_PATH)
    }

    /// A policy that records sleeps instead of sleeping and reads an injected clock.
    fn policy(&self) -> RetryPolicy {
        let sleeps = Arc::clone(&self.sleeps);
        let clock = Arc::clone(&self.clock);
        let base = Instant::now();
        RetryPolicy::with_hooks(
            Box::new(move |d| sleeps.lock().unwrap().push(d)),
            Box::new(move || base + *clock.lock().unwrap()),
            Arc::new(AtomicBool::new(false)),
        )
    }

    fn per_chunk_requests(&self) -> Vec<RangeRequest> {
        let p = plan(&self.remote, None, &dummy_roots(self.dir.path()), &|_| false);
        p.needed
            .iter()
            .map(|c| RangeRequest {
                offset: c.offset,
                length: c.compressed_length as u64,
                chunks: vec![c.clone()],
            })
            .collect()
    }

    fn fetch_all(&self, reader: &HttpRangeReader, policy: &RetryPolicy) -> Result<(), FetchError> {
        let requests = self.per_chunk_requests();
        fetch_ranges(reader, &requests, self.pkg.asset.Size, &self.blob, policy, &|_, _| {}, false)
    }

    fn assert_blob_complete(&self) {
        let blob = self.blob.lock().unwrap();
        for c in &self.pkg.built.chunks {
            assert!(
                blob.contains(&c.sha256),
                "chunk of {} at {} missing from the blob",
                c.file_path,
                c.plain_offset
            );
            assert_eq!(blob.read(&c.sha256).unwrap(), c.plain);
        }
    }

    fn ranged_requests(&self) -> Vec<LoggedRequest> {
        self.server
            .requests()
            .into_iter()
            .filter(|r| r.path.starts_with(PKG_PATH) && r.range.is_some())
            .collect()
    }

    fn chunk_count(&self) -> usize {
        self.pkg.built.chunks.len()
    }
}

fn is_fatal<T>(r: &Result<T, FetchError>) -> bool {
    matches!(r, Err(FetchError::Fatal(_)))
}

/// An in-memory range reader whose `fail(offset, length, nth call at that offset)` decides which reads fail
/// (instantly, with a transient error, like a dropped network); every read is logged as `(offset, length, failed)`.
struct ScriptedReader {
    bytes: Vec<u8>,
    calls: Mutex<HashMap<u64, u32>>,
    log: Mutex<Vec<(u64, usize, bool)>>,
    fail: Box<dyn Fn(u64, usize, u32) -> bool + Send + Sync>,
    /// Successful reads wait (up to 10 s) until this many reads have failed.
    failures_before_success: u32,
}

impl ScriptedReader {
    fn new(bytes: &[u8], fail: impl Fn(u64, usize, u32) -> bool + Send + Sync + 'static) -> ScriptedReader {
        ScriptedReader {
            bytes: bytes.to_vec(),
            calls: Mutex::new(HashMap::new()),
            log: Mutex::new(Vec::new()),
            fail: Box::new(fail),
            failures_before_success: 0,
        }
    }

    fn failures(&self) -> u32 {
        self.log.lock().unwrap().iter().filter(|(_, _, f)| *f).count() as u32
    }

    fn log(&self) -> Vec<(u64, usize, bool)> {
        self.log.lock().unwrap().clone()
    }
}

impl RangeReader for ScriptedReader {
    fn len(&self) -> Option<u64> {
        Some(self.bytes.len() as u64)
    }

    fn read_exact_at(&self, offset: u64, buf: &mut [u8]) -> Result<(), RangeError> {
        let nth = {
            let mut calls = self.calls.lock().unwrap();
            let n = calls.entry(offset).or_insert(0);
            *n += 1;
            *n
        };
        let failed = (self.fail)(offset, buf.len(), nth);
        if !failed {
            let deadline = Instant::now() + Duration::from_secs(10);
            while self.failures() < self.failures_before_success && Instant::now() < deadline {
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        self.log.lock().unwrap().push((offset, buf.len(), failed));
        if failed {
            return Err(RangeError::Transient("scripted failure".to_string()));
        }
        buf.copy_from_slice(&self.bytes[offset as usize..offset as usize + buf.len()]);
        Ok(())
    }
}

#[test]
fn survives_transient_faults() {
    let h = Harness::new(&test_files());
    h.server.push_faults([
        Fault::ResetAfter(10),
        Fault::Status(503),
        Fault::ShortBody(5),
        Fault::Status(429),
        Fault::Status(500),
        Fault::Status(502),
        Fault::Status(504),
        Fault::Status(408),
    ]);
    let policy = h.policy();
    h.fetch_all(&h.reader(), &policy).unwrap();
    h.assert_blob_complete();
    let requests = h.ranged_requests();
    assert_eq!(requests.len(), h.chunk_count() + 8, "every fault costs exactly one extra request");
    assert_eq!(h.sleeps.lock().unwrap().len(), 8, "one backoff per failed attempt");
    // completed chunks are never read again: each range appears once plus its own failures
    for req in &h.per_chunk_requests() {
        let range = Some((req.offset, req.offset + req.length - 1));
        let hits: Vec<&LoggedRequest> = requests.iter().filter(|r| r.range == range).collect();
        let faults = hits.iter().filter(|r| r.fault.is_some()).count();
        assert_eq!(hits.len(), 1 + faults, "range {:?}", range);
    }
}

#[test]
fn budget_resets_on_progress() {
    let h = Harness::new(&many_chunk_files());
    // every queued fault is consumed: the fetch makes at least chunk_count + 40 >= 120 requests
    assert!(h.chunk_count() >= 80, "{} chunks", h.chunk_count());
    // 40 failures interleaved with successes: far more than the 16-failure budget, never 16 in a row
    // (a period of 3 keeps the 4 lock-stepped workers from always popping the same slot)
    let faults: Vec<Fault> = (0..120)
        .map(|i| {
            if i % 3 == 0 {
                Fault::Status(500)
            } else {
                Fault::None
            }
        })
        .collect();
    h.server.push_faults(faults);
    let policy = h.policy();
    h.fetch_all(&h.reader(), &policy).unwrap();
    h.assert_blob_complete();
    let requests = h.ranged_requests();
    assert_eq!(requests.iter().filter(|r| r.fault.is_some()).count(), 40);
    assert_eq!(requests.len(), h.chunk_count() + 40);
}

#[test]
fn total_outage_ends_on_the_per_request_budget() {
    let h = Harness::new(&test_files());
    h.server.push_faults((0..200).map(|_| Fault::Status(500)));
    let policy = h.policy();
    let result = h.fetch_all(&h.reader(), &policy);
    assert!(is_fatal(&result), "{:?}", result);
    let n = h.ranged_requests().len();
    // the first request to fail a 9th time ends the fetch; the other 3 workers stop at their next check
    assert!((9..=40).contains(&n), "saw {} requests", n);
}

#[test]
fn parallel_failures_do_not_exhaust_a_shared_budget() {
    // a short outage: every request fails instantly 6 times (within its 8-attempt budget) before the network
    // is back, so the 4 workers see at least 24 failures in a row before the first verified chunk
    let h = Harness::new(&test_files());
    let mut reader = ScriptedReader::new(&h.pkg.bytes, |_, _, nth| nth <= 6);
    reader.failures_before_success = 24;
    let requests = h.per_chunk_requests();
    assert!(requests.len() >= chunks::FETCH_PARALLELISM);
    let policy = h.policy();
    fetch_ranges(&reader, &requests, h.pkg.asset.Size, &h.blob, &policy, &|_, _| {}, false).unwrap();
    h.assert_blob_complete();
    let log = reader.log();
    let leading_failures = log.iter().take_while(|(_, _, failed)| *failed).count();
    assert!(leading_failures >= 16, "{} failures before the first success", leading_failures);
    assert_eq!(log.iter().filter(|(_, _, failed)| *failed).count(), requests.len() * 6);
}

#[test]
fn failing_coalesced_request_is_split_until_it_lands() {
    // a link that cannot deliver large requests (they always time out or drop) but delivers single chunks
    let h = Harness::new(&many_chunk_files());
    let p = plan(&h.remote, None, &dummy_roots(h.dir.path()), &|_| false);
    let requests = coalesce(&p);
    let largest_chunk = p.needed.iter().map(|c| c.compressed_length as usize).max().unwrap();
    assert!(
        requests.iter().any(|r| r.chunks.len() > 8 && r.length as usize > 4 * largest_chunk),
        "the plan coalesces large multi-chunk requests"
    );
    let reader = ScriptedReader::new(&h.pkg.bytes, move |_, len, _| len > largest_chunk);
    let seen = Mutex::new(Vec::new());
    let policy = h.policy();
    fetch_ranges(
        &reader,
        &requests,
        h.pkg.asset.Size,
        &h.blob,
        &policy,
        &|done, total| seen.lock().unwrap().push((done, total)),
        false,
    )
    .unwrap();
    h.assert_blob_complete();

    // every failed span was split rather than retried as a whole
    let log = reader.log();
    let failed: Vec<(u64, usize)> = log.iter().filter(|(_, _, f)| *f).map(|(o, l, _)| (*o, *l)).collect();
    assert!(!failed.is_empty());
    for span in &failed {
        assert_eq!(failed.iter().filter(|s| *s == span).count(), 1, "{:?} was retried whole", span);
    }
    // the skipped bytes between halves still count, so progress ends at the total
    let total: u64 = requests.iter().map(|r| r.length).sum();
    let seen = seen.into_inner().unwrap();
    assert_eq!(seen.iter().map(|(d, _)| *d).max(), Some(total));
}

#[test]
fn no_progress_for_ten_minutes_is_fatal() {
    let h = Harness::new(&one_chunk_files());
    h.server.push_faults([Fault::Status(500), Fault::Status(500), Fault::Status(500)]);
    // every backoff "takes" 11 minutes on the injected clock
    let clock = Arc::clone(&h.clock);
    let base = Instant::now();
    let policy = RetryPolicy::with_hooks(
        Box::new(move |_| *clock.lock().unwrap() += Duration::from_secs(11 * 60)),
        Box::new({
            let clock = Arc::clone(&h.clock);
            move || base + *clock.lock().unwrap()
        }),
        Arc::new(AtomicBool::new(false)),
    );
    let result = h.fetch_all(&h.reader(), &policy);
    assert!(is_fatal(&result), "{:?}", result);
    assert_eq!(h.ranged_requests().len(), 2, "the second failure sees 11 minutes without progress");
}

#[test]
fn per_request_budget_is_fatal() {
    let h = Harness::new(&one_chunk_files());
    h.server.push_faults((0..30).map(|_| Fault::Status(503)));
    let policy = h.policy();
    let result = h.fetch_all(&h.reader(), &policy);
    assert!(is_fatal(&result), "{:?}", result);
    assert_eq!(h.ranged_requests().len(), 9, "8 retries, the 9th failure is fatal");
}

#[test]
fn retry_after_honoured() {
    let h = Harness::new(&one_chunk_files());
    h.server.push_faults([Fault::StatusRetryAfter(503, 7), Fault::StatusRetryAfter(429, 120)]);
    let policy = h.policy();
    h.fetch_all(&h.reader(), &policy).unwrap();
    h.assert_blob_complete();
    let sleeps = h.sleeps.lock().unwrap().clone();
    assert_eq!(
        sleeps,
        vec![Duration::from_secs(7), Duration::from_secs(60)],
        "Retry-After used as-is, capped at 60 s"
    );
}

#[test]
fn status_200_is_fatal_without_reading_body() {
    let h = Harness::new(&test_files());
    h.server.push_faults([Fault::IgnoreRange]);
    let policy = h.policy();
    let started = Instant::now();
    let result = h.fetch_all(&h.reader(), &policy);
    assert!(is_fatal(&result), "{:?}", result);
    assert!(started.elapsed() < Duration::from_secs(20));
    // give the server thread a moment to notice the dropped connection and record what it managed to send
    std::thread::sleep(Duration::from_millis(500));
    let sent = h.server.ignore_range_bytes_sent();
    assert!(
        sent < IGNORE_RANGE_BODY_LEN,
        "the client must drop a 200 without reading its body (server sent {} bytes)",
        sent
    );
}

#[test]
fn wrong_content_range_is_fatal() {
    let h = Harness::new(&test_files());
    h.server.push_faults([Fault::WrongContentRange]);
    let policy = h.policy();
    assert!(is_fatal(&h.fetch_all(&h.reader(), &policy)));

    let h = Harness::new(&one_chunk_files());
    h.server.push_faults([Fault::Range416]);
    let policy = h.policy();
    assert!(is_fatal(&h.fetch_all(&h.reader(), &policy)));
    assert!(h.sleeps.lock().unwrap().is_empty(), "fatal responses are not retried");
}

#[test]
fn short_body_retried() {
    let h = Harness::new(&test_files());
    h.server.push_faults([Fault::ShortBody(100)]);
    let policy = h.policy();
    h.fetch_all(&h.reader(), &policy).unwrap();
    h.assert_blob_complete();
    assert_eq!(h.ranged_requests().len(), h.chunk_count() + 1);
}

#[test]
fn hash_mismatch_refetches_only_that_range() {
    let h = Harness::new(&test_files());
    h.server.push_faults([Fault::CorruptByte(3)]);
    let policy = h.policy();
    h.fetch_all(&h.reader(), &policy).unwrap();
    h.assert_blob_complete();
    let requests = h.ranged_requests();
    assert_eq!(requests.len(), h.chunk_count() + 1);
    let corrupted = requests.iter().find(|r| r.fault.is_some()).unwrap().range;
    assert_eq!(requests.iter().filter(|r| r.range == corrupted).count(), 2);
    for req in &h.per_chunk_requests() {
        let range = Some((req.offset, req.offset + req.length - 1));
        if range != corrupted {
            assert_eq!(requests.iter().filter(|r| r.range == range).count(), 1);
        }
    }
    // the blob holds the chunk exactly once
    let blob_len = std::fs::metadata(h.dir.path().join("chunks.bin")).unwrap().len();
    let expected: u64 = h.pkg.built.chunks.iter().map(|c| 40 + c.plain.len() as u64).sum();
    assert_eq!(blob_len, expected);
}

#[test]
fn index_sha_mismatch_retries_once_then_fatal() {
    let h = Harness::new(&test_files());
    let mut asset = h.pkg.asset.clone();
    asset.ChunkIndexSHA256 = "00".repeat(32);
    let policy = h.policy();
    let result = fetch_index(&h.reader(), &asset, &policy, &|_, _| {});
    assert!(matches!(result, Err(FetchError::IndexInvalid(_))), "{:?}", result);
    let index_range = Some((asset.ChunkIndexOffset, asset.ChunkIndexOffset + asset.ChunkIndexLength - 1));
    assert_eq!(h.ranged_requests().iter().filter(|r| r.range == index_range).count(), 2);

    // a transient corruption of the index bytes is healed by the one retry
    let h = Harness::new(&test_files());
    h.server.push_faults([Fault::CorruptByte(10)]);
    let policy = h.policy();
    let progress = Mutex::new(Vec::new());
    let bytes = fetch_index(&h.reader(), &h.pkg.asset, &policy, &|done, total| {
        progress.lock().unwrap().push((done, total))
    })
    .unwrap();
    assert_eq!(bytes, h.pkg.built.trailer());
    assert_eq!(h.ranged_requests().len(), 2);
    let progress = progress.into_inner().unwrap();
    assert_eq!(progress.last().unwrap().0, h.pkg.asset.ChunkIndexLength);
}

#[test]
fn authorization_not_forwarded_to_another_origin() {
    let h = Harness::new(&test_files());
    // the same host on another port: a different origin (as is https -> http on the same host)
    let other = MockHttpServer::empty();
    other.add_file(PKG_PATH, h.pkg.bytes.clone());
    h.server.add_redirect("/r/pkg.nupkg", &format!("{}{}", other.url(), PKG_PATH));
    let auth = HttpHeader {
        Name: "Authorization".to_string(),
        Value: "token secret".to_string(),
    };
    let reader = HttpRangeReader::open(create_range_agent(), &format!("{}/r/pkg.nupkg", h.server.url()), &[auth]).unwrap();
    let policy = h.policy();
    let index = fetch_index(&reader, &h.pkg.asset, &policy, &|_, _| {}).unwrap();
    assert_eq!(index, h.pkg.built.trailer());
    let has_auth = |r: &LoggedRequest| r.headers.iter().any(|(k, _)| k.eq_ignore_ascii_case("authorization"));
    assert!(h.server.requests().iter().all(has_auth), "the original origin gets the token");
    let redirected = other.requests();
    assert!(!redirected.is_empty());
    assert!(!redirected.iter().any(has_auth), "the token never leaves the original origin");
}

#[test]
fn redirect_resolved_once_and_cached() {
    let h = Harness::new(&test_files());
    h.server.add_redirect("/r/pkg.nupkg", PKG_PATH);
    let policy = h.policy();
    h.fetch_all(&h.reader_at("/r/pkg.nupkg"), &policy).unwrap();
    h.assert_blob_complete();
    let requests = h.server.requests();
    assert_eq!(
        requests.iter().filter(|r| r.path == "/r/pkg.nupkg").count(),
        1,
        "one resolution for the whole fetch"
    );
    assert_eq!(h.ranged_requests().len(), h.chunk_count());
}

/// The distinct signed-URL generations that answered a request with 403 (each one must cost exactly one
/// re-resolution, however many workers were using it).
fn rejected_generations(h: &Harness) -> Vec<String> {
    let mut gens: Vec<String> = h
        .server
        .requests()
        .into_iter()
        .filter(|r| r.path.starts_with(PKG_PATH) && r.fault.as_deref() == Some("status 403"))
        .filter_map(|r| r.path.split_once('?').map(|(_, q)| q.to_string()))
        .collect();
    gens.sort();
    gens.dedup();
    gens
}

#[test]
fn expired_url_refresh_is_single_flight() {
    let h = Harness::new(&test_files());
    h.server.add_signed_redirect("/s/pkg", PKG_PATH, 2, false);
    let policy = h.policy();
    h.fetch_all(&h.reader_at("/s/pkg"), &policy).unwrap();
    h.assert_blob_complete();
    let expirations = h.server.expirations();
    assert!(expirations >= 3, "the signed URL expired {} times", expirations);
    let rejected = rejected_generations(&h);
    assert!(rejected.len() >= 2);
    assert_eq!(
        h.server.resolves(),
        rejected.len() as u64 + 1,
        "one initial resolution plus exactly one per expired URL, shared by all workers"
    );
    // a worker that finds the URL already refreshed retries at once: refreshes never cost a backoff
    assert!(h.sleeps.lock().unwrap().is_empty(), "{:?}", h.sleeps.lock().unwrap());
    let served = h.ranged_requests().iter().filter(|r| r.fault.is_none()).count();
    assert_eq!(served, h.chunk_count());
}

#[test]
fn refresh_budget_resets_on_progress() {
    let h = Harness::new(&test_files());
    // every single response expires the URL: far more than 3 refreshes, each followed by a verified chunk
    h.server.add_signed_redirect("/s/pkg", PKG_PATH, 1, false);
    let policy = h.policy();
    h.fetch_all(&h.reader_at("/s/pkg"), &policy).unwrap();
    h.assert_blob_complete();
    assert!(h.server.resolves() > 3);
    assert_eq!(h.server.resolves(), rejected_generations(&h).len() as u64 + 1);
}

#[test]
fn three_refreshes_without_progress_is_fatal() {
    let h = Harness::new(&one_chunk_files());
    h.server.add_signed_redirect("/s/pkg", PKG_PATH, 0, true);
    let policy = h.policy();
    let result = h.fetch_all(&h.reader_at("/s/pkg"), &policy);
    assert!(is_fatal(&result), "{:?}", result);
    assert_eq!(h.server.resolves(), 3);
}

#[test]
fn cancellation_stops_promptly() {
    let h = Harness::new(&test_files());
    h.server.push_faults((0..100).map(|_| Fault::Status(500)));
    // the first backoff cancels the update
    let flag = Arc::new(AtomicBool::new(false));
    let policy = RetryPolicy::with_hooks(
        Box::new({
            let flag = Arc::clone(&flag);
            move |_| flag.store(true, Ordering::Relaxed)
        }),
        Box::new(Instant::now),
        Arc::clone(&flag),
    );
    let result = h.fetch_all(&h.reader(), &policy);
    assert!(matches!(result, Err(FetchError::Cancelled)), "{:?}", result);
    assert!(policy.is_cancelled());
    assert!(h.ranged_requests().len() <= 8, "workers stop at their next check");
}

#[test]
fn no_sleep_in_tests() {
    let h = Harness::new(&many_chunk_files());
    // 20 failures (half of them asking for a 30 s Retry-After) interleaved with successes
    h.server.push_faults((0..60).map(|i| match i % 6 {
        0 => Fault::StatusRetryAfter(503, 30),
        3 => Fault::Status(500),
        _ => Fault::None,
    }));
    let policy = h.policy();
    let started = Instant::now();
    h.fetch_all(&h.reader(), &policy).unwrap();
    let sleeps = h.sleeps.lock().unwrap().clone();
    assert_eq!(sleeps.len(), 20);
    assert_eq!(
        sleeps.iter().filter(|d| **d == Duration::from_secs(30)).count(),
        10,
        "Retry-After delays were requested"
    );
    assert!(started.elapsed() < Duration::from_secs(30), "the injected sleep never waits");
    h.assert_blob_complete();
}

#[test]
fn auto_decompression_disabled() {
    let h = Harness::new(&test_files());
    h.server.push_faults([Fault::Gzip]);
    let policy = h.policy();
    let result = h.fetch_all(&h.reader(), &policy);
    assert!(is_fatal(&result), "{:?}", result);
    let request = h.ranged_requests().into_iter().next().unwrap();
    let accept = request
        .headers
        .iter()
        .find(|(k, _)| k.eq_ignore_ascii_case("Accept-Encoding"))
        .map(|(_, v)| v.clone());
    assert_eq!(accept.as_deref(), Some("identity"));
}

#[test]
fn missing_round_appends_unconditionally() {
    let h = Harness::new(&one_chunk_files());
    let policy = h.policy();
    let requests = h.per_chunk_requests();
    fetch_ranges(&h.reader(), &requests, h.pkg.asset.Size, &h.blob, &policy, &|_, _| {}, false).unwrap();
    let blob_path = h.dir.path().join("chunks.bin");
    let len1 = std::fs::metadata(&blob_path).unwrap().len();
    fetch_ranges(&h.reader(), &requests, h.pkg.asset.Size, &h.blob, &policy, &|_, _| {}, false).unwrap();
    assert_eq!(
        std::fs::metadata(&blob_path).unwrap().len(),
        len1,
        "a chunk already in the blob is skipped"
    );
    fetch_ranges(&h.reader(), &requests, h.pkg.asset.Size, &h.blob, &policy, &|_, _| {}, true).unwrap();
    assert_eq!(
        std::fs::metadata(&blob_path).unwrap().len(),
        len1 * 2,
        "a missing round appends regardless"
    );
    h.assert_blob_complete();
}

#[test]
fn corrupt_payload_is_refetched() {
    let h = Harness::new(&test_files());
    let blob_path = h.dir.path().join("chunks.bin");
    {
        // chunk 0: a valid header with a zeroed payload (the crash tail); chunk 1: a verified record
        let c0 = &h.pkg.built.chunks[0];
        let c1 = &h.pkg.built.chunks[1];
        let mut bytes = Vec::new();
        bytes.extend_from_slice(b"VCHK");
        bytes.extend_from_slice(&(c0.plain.len() as u32).to_le_bytes());
        bytes.extend_from_slice(&c0.sha256);
        bytes.extend_from_slice(&vec![0u8; c0.plain.len()]);
        bytes.extend_from_slice(b"VCHK");
        bytes.extend_from_slice(&(c1.plain.len() as u32).to_le_bytes());
        bytes.extend_from_slice(&c1.sha256);
        bytes.extend_from_slice(&c1.plain);
        std::fs::write(&blob_path, bytes).unwrap();
    }
    let blob = ChunkBlob::open(&blob_path).unwrap();
    assert!(!blob.contains(&h.pkg.built.chunks[0].sha256));
    assert!(blob.contains(&h.pkg.built.chunks[1].sha256));
    let p = plan(&h.remote, None, &dummy_roots(h.dir.path()), &|hash| blob.contains(hash));
    assert_eq!(
        p.needed.len(),
        h.chunk_count() - 1,
        "the corrupt record is planned again, the verified one is not"
    );
    let requests: Vec<RangeRequest> = p
        .needed
        .iter()
        .map(|c| RangeRequest {
            offset: c.offset,
            length: c.compressed_length as u64,
            chunks: vec![c.clone()],
        })
        .collect();
    let blob = Mutex::new(blob);
    let policy = h.policy();
    fetch_ranges(&h.reader(), &requests, h.pkg.asset.Size, &blob, &policy, &|_, _| {}, false).unwrap();
    let blob = blob.into_inner().unwrap();
    for c in &h.pkg.built.chunks {
        assert_eq!(blob.read(&c.sha256).unwrap(), c.plain);
    }
    assert_eq!(h.ranged_requests().len(), h.chunk_count() - 1);
}

#[test]
fn coalesced_requests_complete_with_progress() {
    let h = Harness::new(&test_files());
    let p = plan(&h.remote, None, &dummy_roots(h.dir.path()), &|_| false);
    let requests = coalesce(&p);
    assert!(requests.len() < h.chunk_count(), "adjacent chunks are merged");
    let seen = Mutex::new(Vec::new());
    let policy = h.policy();
    fetch_ranges(
        &h.reader(),
        &requests,
        h.pkg.asset.Size,
        &h.blob,
        &policy,
        &|done, total| seen.lock().unwrap().push((done, total)),
        false,
    )
    .unwrap();
    h.assert_blob_complete();
    let seen = seen.into_inner().unwrap();
    assert!(seen.windows(2).all(|w| w[0].0 < w[1].0), "progress is monotonic: {:?}", seen);
    let total: u64 = requests.iter().map(|r| r.length).sum();
    assert_eq!(seen.last().unwrap(), &(total, total));
    assert_eq!(h.ranged_requests().len(), requests.len());
}

#[test]
fn origin_size_mismatch_is_fatal() {
    let h = Harness::new(&test_files());
    let mut asset = h.pkg.asset.clone();
    asset.Size += 1;
    let policy = h.policy();
    let result = fetch_index(&h.reader(), &asset, &policy, &|_, _| {});
    assert!(is_fatal(&result), "{:?}", result);
}

#[test]
fn file_source_range_reader_fetches_everything() {
    let h = Harness::new(&test_files());
    let dir = tempfile::tempdir().unwrap();
    std::fs::write(dir.path().join("pkg.nupkg"), &h.pkg.bytes).unwrap();
    let source = FileSource::new(dir.path());
    let reader = source.open_range_reader(&h.pkg.asset).unwrap().expect("file source supports ranges");
    assert_eq!(reader.len(), Some(h.pkg.asset.Size));
    let policy = h.policy();
    let index = fetch_index(reader.as_ref(), &h.pkg.asset, &policy, &|_, _| {}).unwrap();
    assert_eq!(index, h.pkg.built.trailer());
    let requests = h.per_chunk_requests();
    fetch_ranges(reader.as_ref(), &requests, h.pkg.asset.Size, &h.blob, &policy, &|_, _| {}, false).unwrap();
    h.assert_blob_complete();
}

#[test]
fn auto_source_forwards_range_reader() {
    let h = Harness::new(&test_files());
    let source = velopack::sources::AutoSource::new(&h.server.url());
    let reader = source.open_range_reader(&h.pkg.asset).unwrap().expect("http source supports ranges");
    let policy = h.policy();
    let index = fetch_index(reader.as_ref(), &h.pkg.asset, &policy, &|_, _| {}).unwrap();
    assert_eq!(index, h.pkg.built.trailer());
    assert_eq!(reader.len(), Some(h.pkg.asset.Size));
    assert!(chunks::ChunkIndex::parse_trailer(&index).is_ok());
}

#[test]
fn backoff_is_equal_jitter() {
    for attempt in 1..=12u32 {
        let cap = (1u64 << (attempt - 1)).min(30) as f64;
        for _ in 0..50 {
            let d = RetryPolicy::backoff_delay(attempt, None).as_secs_f64();
            assert!(d >= cap / 2.0 && d <= cap, "attempt {}: {} not in [{}, {}]", attempt, d, cap / 2.0, cap);
        }
    }
    // the 8 retries of one request always wait at least 0.5+1+2+4+8+15+15+15 seconds
    let floor: f64 = (1..=chunks::MAX_ATTEMPTS_PER_REQUEST)
        .map(|a| ((1u64 << (a - 1)).min(30) as f64) / 2.0)
        .sum();
    assert_eq!(floor, 60.5);
    assert_eq!(RetryPolicy::backoff_delay(3, Some(Duration::from_secs(120))), Duration::from_secs(60));
}
