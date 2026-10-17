//! The chunk fetcher (§5.2, §5.5–5.7): reads the trailer and the planned ranges through a
//! [`RangeReader`] under the retry policy, inflates and verifies every chunk and appends it to the
//! blob. Budgets are progress-based: every verified chunk (or index segment) resets them, so a long
//! download on a spotty link is never penalised for its age.

use super::blob::ChunkBlob;
use super::fetch::{RangeError, RangeReader};
use super::inflate::{inflate_chunk, sha256, sha256_hex};
use super::plan::{NeededChunk, RangeRequest};
use super::*;
use crate::VelopackAsset;
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

/// Why a fetch stopped.
#[derive(Debug, Clone, thiserror::Error)]
pub enum FetchError {
    /// The chunk path is abandoned in favour of the full download (§5.6 Fatal rows).
    #[error("chunk fetch failed: {0}")]
    Fatal(String),
    /// The trailer did not match the feed's `ChunkIndexSHA256` twice: the chunk path is abandoned and the
    /// staged directory deleted (§11).
    #[error("chunk index invalid: {0}")]
    IndexInvalid(String),
    /// The caller cancelled the update; the staged directory is kept for resume.
    #[error("chunk fetch cancelled")]
    Cancelled,
}

/// The retry class of a range error.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ErrorClass {
    /// Retry the request after backoff.
    Transient,
    /// Abandon the chunk path.
    Fatal,
    /// Propagate the cancellation.
    Cancel,
}

impl ErrorClass {
    /// Classifies a range error (§5.6).
    pub fn of(error: &RangeError) -> ErrorClass {
        match error {
            RangeError::Transient(_) => ErrorClass::Transient,
            RangeError::Unavailable(_) => ErrorClass::Fatal,
            RangeError::Cancelled => ErrorClass::Cancel,
        }
    }
}

const RETRY_AFTER_PREFIX: &str = "retry-after=";

/// Builds a transient error that carries a `Retry-After` value. `RangeError` is a frozen type, so the
/// delay travels in the message as a `retry-after=<secs>; ` prefix that [`retry_after_of`] parses.
pub fn transient_with_retry_after(secs: u64, message: &str) -> RangeError {
    RangeError::Transient(format!("{}{}; {}", RETRY_AFTER_PREFIX, secs, message))
}

/// The `Retry-After` delay carried by a transient error message, if any.
pub fn retry_after_of(message: &str) -> Option<Duration> {
    let rest = message.strip_prefix(RETRY_AFTER_PREFIX)?;
    let digits: String = rest.chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse::<u64>().ok().map(Duration::from_secs)
}

/// The retry policy: injectable sleep and clock (tests never really sleep) plus the cancel flag.
pub struct RetryPolicy {
    sleep: Box<dyn Fn(Duration) + Send + Sync>,
    now: Box<dyn Fn() -> Instant + Send + Sync>,
    cancel: Arc<AtomicBool>,
}

impl Default for RetryPolicy {
    fn default() -> Self {
        RetryPolicy::new()
    }
}

impl RetryPolicy {
    /// The production policy: real sleeps (in short slices, so a cancellation is noticed promptly) and
    /// the monotonic clock.
    pub fn new() -> RetryPolicy {
        let cancel = Arc::new(AtomicBool::new(false));
        let flag = Arc::clone(&cancel);
        RetryPolicy {
            sleep: Box::new(move |d: Duration| {
                let deadline = Instant::now() + d;
                while !flag.load(Ordering::Relaxed) {
                    let left = deadline.saturating_duration_since(Instant::now());
                    if left.is_zero() {
                        break;
                    }
                    std::thread::sleep(left.min(Duration::from_millis(250)));
                }
            }),
            now: Box::new(Instant::now),
            cancel,
        }
    }

    /// A policy with an injected sleep function, clock and cancel flag (tests never really sleep).
    pub fn with_hooks(
        sleep: Box<dyn Fn(Duration) + Send + Sync>,
        now: Box<dyn Fn() -> Instant + Send + Sync>,
        cancel: Arc<AtomicBool>,
    ) -> RetryPolicy {
        RetryPolicy { sleep, now, cancel }
    }

    /// The flag that cancels every fetch using this policy.
    pub fn cancel_flag(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.cancel)
    }

    /// Whether the fetch was cancelled.
    pub fn is_cancelled(&self) -> bool {
        self.cancel.load(Ordering::Relaxed)
    }

    fn check_cancel(&self) -> Result<(), FetchError> {
        if self.is_cancelled() {
            Err(FetchError::Cancelled)
        } else {
            Ok(())
        }
    }

    fn now(&self) -> Instant {
        (self.now)()
    }

    /// Full-jitter backoff: `random(0, min(30 s, 1 s * 2^(attempt-1)))`, or `min(Retry-After, 60 s)` when the
    /// server asked for a delay.
    pub fn backoff_delay(attempt: u32, retry_after: Option<Duration>) -> Duration {
        if let Some(ra) = retry_after {
            return ra.min(Duration::from_secs(RETRY_AFTER_MAX_SECS));
        }
        let exp = BACKOFF_BASE_SECS.saturating_mul(1u64 << attempt.saturating_sub(1).min(16));
        let cap = exp.min(BACKOFF_MAX_SECS) as f64;
        Duration::from_secs_f64(cap * rand::random::<f64>())
    }

    fn sleep_before_retry(&self, attempt: u32, retry_after: Option<Duration>) -> Result<(), FetchError> {
        self.check_cancel()?;
        let delay = Self::backoff_delay(attempt, retry_after);
        debug!("Retrying in {:?} (attempt {})", delay, attempt);
        (self.sleep)(delay);
        self.check_cancel()
    }
}

/// The progress clock shared by all workers of one fetch (§5.6). There is deliberately no shared count of
/// failed attempts: 4 workers failing at once (Wi-Fi drop, DNS failure) would exhaust it in seconds, so a
/// short outage is ridden out by the per-request budgets and this clock instead.
pub struct Budget {
    last_progress: Mutex<Instant>,
}

impl Budget {
    /// A fresh budget whose progress clock starts now.
    pub fn new(policy: &RetryPolicy) -> Budget {
        Budget {
            last_progress: Mutex::new(policy.now()),
        }
    }

    /// A verified chunk or index segment arrived: the clock restarts.
    pub fn record_progress(&self, policy: &RetryPolicy) {
        *self.last_progress.lock().unwrap() = policy.now();
    }

    /// A request attempt failed; fatal once nothing was verified for 10 minutes.
    pub fn record_failure(&self, policy: &RetryPolicy, what: &str) -> Result<(), FetchError> {
        let since = policy.now().saturating_duration_since(*self.last_progress.lock().unwrap());
        if since >= Duration::from_secs(MAX_SECS_WITHOUT_PROGRESS) {
            return Err(FetchError::Fatal(format!("no progress for {:?} ({})", since, what)));
        }
        Ok(())
    }
}

/// Attempt counters of one request.
#[derive(Default)]
struct RequestAttempts {
    failures: u32,
    hash_mismatches: u32,
}

/// One request, split into sub-reads of at most 8 MiB, plus the origin size check.
fn read_once(reader: &dyn RangeReader, offset: u64, buf: &mut [u8], asset_size: u64) -> Result<(), RangeError> {
    let mut done = 0usize;
    while done < buf.len() {
        let n = (buf.len() - done).min(REQUEST_CAP as usize);
        reader.read_exact_at(offset + done as u64, &mut buf[done..done + n])?;
        if let Some(len) = reader.len() {
            if len != asset_size {
                return Err(RangeError::Unavailable(format!(
                    "the origin reports {} bytes but the feed says {}",
                    len, asset_size
                )));
            }
        }
        done += n;
    }
    Ok(())
}

/// How a [`read_retrying`] call ended without a fatal error.
#[derive(Debug, PartialEq, Eq)]
enum ReadOutcome {
    /// `buf` was filled.
    Filled,
    /// A transient failure (after its backoff) of a request that may be split: the caller retries it as
    /// smaller requests instead, so a slow or flaky link that cannot deliver the whole span still progresses.
    Split,
}

/// Fills `buf` from `offset`, retrying transient failures within the request and shared budgets. `stop`
/// is set when another worker already failed fatally, so this request gives up at its next attempt.
/// With `splittable`, the first transient failure returns [`ReadOutcome::Split`] instead of retrying.
#[allow(clippy::too_many_arguments)]
fn read_retrying(
    reader: &dyn RangeReader,
    offset: u64,
    buf: &mut [u8],
    asset_size: u64,
    policy: &RetryPolicy,
    budget: &Budget,
    attempts: &mut RequestAttempts,
    stop: Option<&AtomicBool>,
    splittable: bool,
    what: &str,
) -> Result<ReadOutcome, FetchError> {
    loop {
        policy.check_cancel()?;
        if stop.map(|s| s.load(Ordering::Relaxed)).unwrap_or(false) {
            return Err(FetchError::Fatal(format!("{} abandoned after another request failed", what)));
        }
        match read_once(reader, offset, buf, asset_size) {
            Ok(()) => return Ok(ReadOutcome::Filled),
            Err(RangeError::Cancelled) => return Err(FetchError::Cancelled),
            Err(RangeError::Unavailable(m)) => return Err(FetchError::Fatal(format!("{}: {}", what, m))),
            Err(RangeError::Transient(m)) => {
                attempts.failures += 1;
                warn!("{} failed (attempt {}): {}", what, attempts.failures, m);
                if attempts.failures > MAX_ATTEMPTS_PER_REQUEST {
                    return Err(FetchError::Fatal(format!("{} failed {} times: {}", what, attempts.failures, m)));
                }
                budget.record_failure(policy, what)?;
                policy.sleep_before_retry(attempts.failures, retry_after_of(&m))?;
                if splittable {
                    return Ok(ReadOutcome::Split);
                }
            }
        }
    }
}

/// Reads the trailer `[ChunkIndexOffset, +ChunkIndexLength)` in ≤ 8 MiB segments under the retry policy and
/// verifies its SHA-256 (one whole-index retry on mismatch). `progress` receives `(bytes, total)`.
pub fn fetch_index(
    reader: &dyn RangeReader,
    asset: &VelopackAsset,
    policy: &RetryPolicy,
    progress: &(dyn Fn(u64, u64) + Sync),
) -> Result<Vec<u8>, FetchError> {
    let total = asset.ChunkIndexLength;
    if total == 0 || total > MAX_TRAILER_LEN || asset.ChunkIndexOffset.checked_add(total).map(|e| e > asset.Size).unwrap_or(true) {
        return Err(FetchError::Fatal("the feed's chunk index pointer is out of bounds".into()));
    }
    let budget = Budget::new(policy);
    for round in 0..2 {
        let mut bytes = vec![0u8; total as usize];
        let mut done = 0u64;
        while done < total {
            let n = (total - done).min(REQUEST_CAP);
            let mut attempts = RequestAttempts::default();
            let what = format!("index segment at {}", done);
            read_retrying(
                reader,
                asset.ChunkIndexOffset + done,
                &mut bytes[done as usize..(done + n) as usize],
                asset.Size,
                policy,
                &budget,
                &mut attempts,
                None,
                false,
                &what,
            )?;
            budget.record_progress(policy);
            done += n;
            progress(done, total);
        }
        let digest = sha256_hex(&sha256(&bytes));
        if digest.eq_ignore_ascii_case(&asset.ChunkIndexSHA256) {
            return Ok(bytes);
        }
        warn!(
            "Chunk index digest mismatch (expected {}, got {}), round {}",
            asset.ChunkIndexSHA256,
            digest,
            round + 1
        );
        budget.record_failure(policy, "index digest")?;
    }
    Err(FetchError::IndexInvalid("the chunk index did not match its digest twice".into()))
}

struct Shared<'a> {
    reader: &'a dyn RangeReader,
    requests: &'a [RangeRequest],
    asset_size: u64,
    blob: &'a Mutex<ChunkBlob>,
    policy: &'a RetryPolicy,
    budget: Budget,
    force_append: bool,
    next: AtomicUsize,
    stop: AtomicBool,
    first_error: Mutex<Option<FetchError>>,
    bytes_done: AtomicU64,
    bytes_total: u64,
    progress: &'a (dyn Fn(u64, u64) + Sync),
}

/// Fetches every coalesced request with 4 workers, verifying each chunk (inflate + SHA-256) and appending it
/// to the blob. Chunks already in the blob are skipped unless `force_append` (a `missing.bin` round).
/// `progress` receives `(bytes received, total bytes)`.
pub fn fetch_ranges(
    reader: &dyn RangeReader,
    requests: &[RangeRequest],
    asset_size: u64,
    blob: &Mutex<ChunkBlob>,
    policy: &RetryPolicy,
    progress: &(dyn Fn(u64, u64) + Sync),
    force_append: bool,
) -> Result<(), FetchError> {
    let shared = Shared {
        reader,
        requests,
        asset_size,
        blob,
        policy,
        budget: Budget::new(policy),
        force_append,
        next: AtomicUsize::new(0),
        stop: AtomicBool::new(false),
        first_error: Mutex::new(None),
        bytes_done: AtomicU64::new(0),
        bytes_total: requests.iter().map(|r| r.length).sum(),
        progress,
    };
    let workers = FETCH_PARALLELISM.min(requests.len()).max(1);
    std::thread::scope(|scope| {
        for _ in 0..workers {
            scope.spawn(|| worker(&shared));
        }
    });
    if let Some(e) = shared.first_error.lock().unwrap().take() {
        return Err(e);
    }
    blob.lock()
        .unwrap()
        .flush()
        .map_err(|e| FetchError::Fatal(format!("failed to flush the chunk blob: {}", e)))?;
    Ok(())
}

fn worker(shared: &Shared<'_>) {
    loop {
        if shared.stop.load(Ordering::Relaxed) {
            return;
        }
        let i = shared.next.fetch_add(1, Ordering::Relaxed);
        let request = match shared.requests.get(i) {
            Some(r) => r,
            None => return,
        };
        if let Err(e) = fetch_request(shared, request) {
            let mut first = shared.first_error.lock().unwrap();
            if first.is_none() {
                *first = Some(e);
            }
            shared.stop.store(true, Ordering::Relaxed);
            return;
        }
    }
}

/// A chunk that inflated and matched its hash: `(sha256, plaintext)`.
type VerifiedChunk = ([u8; 32], Vec<u8>);

/// Slices, inflates and hashes every chunk of a response; all of them must verify before any is appended,
/// so a retried request never leaves duplicates in the blob.
fn verify_chunks(buf: &[u8], request: &RangeRequest) -> Result<Vec<VerifiedChunk>, String> {
    let mut out = Vec::with_capacity(request.chunks.len());
    for chunk in &request.chunks {
        let start = (chunk.offset - request.offset) as usize;
        let comp = &buf[start..start + chunk.compressed_length as usize];
        let plain = inflate_chunk(comp, chunk.method, chunk.length as usize).map_err(|e| format!("chunk {}: {}", chunk.chunk_index, e))?;
        if sha256(&plain) != chunk.sha256 {
            return Err(format!("chunk {} hash mismatch", chunk.chunk_index));
        }
        out.push((chunk.sha256, plain));
    }
    Ok(out)
}

/// Splits a multi-chunk request at its middle chunk boundary into two requests covering only their own
/// chunks (the gap bytes between the halves are not fetched again).
fn split_request(request: &RangeRequest) -> (RangeRequest, RangeRequest) {
    let span = |chunks: &[NeededChunk]| {
        let first = &chunks[0];
        let last = &chunks[chunks.len() - 1];
        RangeRequest {
            offset: first.offset,
            length: last.offset + last.compressed_length as u64 - first.offset,
            chunks: chunks.to_vec(),
        }
    };
    let mid = request.chunks.len() / 2;
    (span(&request.chunks[..mid]), span(&request.chunks[mid..]))
}

/// Fetches one coalesced request. A transient failure of a request with several chunks splits it into two
/// halves (each with its own attempt budget) that are fetched in turn, so every attempt is more likely to
/// land and each verified half counts as progress; a single chunk is retried under its per-request budget.
fn fetch_request(shared: &Shared<'_>, request: &RangeRequest) -> Result<(), FetchError> {
    let mut pending = vec![request.clone()];
    while let Some(request) = pending.pop() {
        if let Some((first, second)) = fetch_one(shared, &request)? {
            // the bytes between the halves are never fetched: count them as done so progress still ends at the total
            let gap = request.length - first.length - second.length;
            shared.bytes_done.fetch_add(gap, Ordering::Relaxed);
            pending.push(second);
            pending.push(first);
        }
    }
    Ok(())
}

/// Fetches, verifies and appends one request; returns the two halves to fetch instead when it was split.
fn fetch_one(shared: &Shared<'_>, request: &RangeRequest) -> Result<Option<(RangeRequest, RangeRequest)>, FetchError> {
    let what = format!("range {}+{}", request.offset, request.length);
    let mut attempts = RequestAttempts::default();
    let mut buf = vec![0u8; request.length as usize];
    loop {
        if shared.stop.load(Ordering::Relaxed) {
            return Ok(None);
        }
        let outcome = read_retrying(
            shared.reader,
            request.offset,
            &mut buf,
            shared.asset_size,
            shared.policy,
            &shared.budget,
            &mut attempts,
            Some(&shared.stop),
            request.chunks.len() > 1,
            &what,
        )?;
        if outcome == ReadOutcome::Split {
            info!("Splitting {} ({} chunks) after a failed attempt", what, request.chunks.len());
            return Ok(Some(split_request(request)));
        }
        match verify_chunks(&buf, request) {
            Ok(chunks) => {
                {
                    let mut blob = shared.blob.lock().unwrap();
                    for (hash, plain) in &chunks {
                        if shared.force_append || !blob.contains(hash) {
                            blob.append(hash, plain)
                                .map_err(|e| FetchError::Fatal(format!("failed to append to the chunk blob: {}", e)))?;
                        }
                    }
                }
                shared.budget.record_progress(shared.policy);
                let done = shared.bytes_done.fetch_add(request.length, Ordering::Relaxed) + request.length;
                (shared.progress)(done, shared.bytes_total);
                return Ok(None);
            }
            Err(m) => {
                attempts.hash_mismatches += 1;
                attempts.failures += 1;
                warn!("{} did not verify ({}), mismatch {}", what, m, attempts.hash_mismatches);
                if attempts.hash_mismatches >= MAX_HASH_MISMATCHES_PER_REQUEST {
                    return Err(FetchError::Fatal(format!(
                        "{} failed verification {} times",
                        what, attempts.hash_mismatches
                    )));
                }
                shared.budget.record_failure(shared.policy, &what)?;
                shared.policy.sleep_before_retry(attempts.failures, None)?;
            }
        }
    }
}
