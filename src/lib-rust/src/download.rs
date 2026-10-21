use std::fs::File;
use std::io::{Read, Write};
use std::path::Path;
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use serde::{Deserialize, Serialize};

use crate::chunks::fetcher::transient_with_retry_after;
use crate::chunks::{RangeError, RangeReader, MAX_CONSECUTIVE_REFRESHES, MIN_RANGE_THROUGHPUT_BYTES_PER_SEC, REQUEST_TIMEOUT_SECS};
use crate::{misc, Error};

/// A single HTTP header (name and value pair) to be sent with a web request.
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
pub struct HttpHeader {
    /// The name of the HTTP header (eg. "Authorization").
    pub Name: String,
    /// The value of the HTTP header.
    pub Value: String,
}

/// Options to customize HTTP requests (custom headers, timeout, etc).
#[allow(non_snake_case)]
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct HttpOptions {
    /// Additional headers to send with each request.
    pub Headers: Vec<HttpHeader>,
    /// Timeout applied to the entire request (connection + transfer), in milliseconds.
    /// The default of 0 means requests never time out.
    pub TimeoutMilliseconds: u64,
}

/// Downloads a file from a URL and writes it to a file while reporting progress from 0-100.
pub fn download_url_to_file<A, S: AsRef<Path>>(url: &str, file_path: S, options: Option<&HttpOptions>, mut progress: A) -> Result<(), Error>
where
    A: FnMut(i16),
{
    let file_path = file_path.as_ref();
    let agent = get_download_agent(options)?;
    let mut req = agent.get(url);
    if let Some(options) = options {
        for header in &options.Headers {
            req = req.header(&header.Name, &header.Value);
        }
    }
    let (head, body) = req.call()?.into_parts();

    let total_size = head
        .headers
        .get("Content-Length")
        .and_then(|s| s.to_str().ok())
        .and_then(|s| s.parse::<u64>().ok());
    let mut file = misc::retry_io(|| File::create(file_path))?;

    const CHUNK_SIZE: usize = 2 * 1024 * 1024; // 2MB
    let mut downloaded: u64 = 0;
    let mut buffer = vec![0; CHUNK_SIZE];
    let mut reader = body.into_reader();

    let mut last_progress = 0;

    loop {
        let size = reader.read(&mut buffer)?; // Explicitly propagate errors
        if size == 0 {
            break; // End of stream
        }
        file.write_all(&buffer[..size])?;
        downloaded += size as u64;

        if let Some(total) = total_size {
            // floor to nearest 5% to reduce message spam
            let new_progress = (downloaded as f64 / total as f64 * 20.0).floor() as i16 * 5;
            if new_progress > last_progress {
                last_progress = new_progress;
                progress(last_progress);
            }
        }
    }

    Ok(())
}

/// Downloads a file from a URL and returns it as a string.
pub fn download_url_as_string(url: &str, options: Option<&HttpOptions>) -> Result<String, Error> {
    let agent = get_download_agent(options)?;
    let mut req = agent.get(url);
    if let Some(options) = options {
        for header in &options.Headers {
            req = req.header(&header.Name, &header.Value);
        }
    }
    let r = req.call()?.body_mut().read_to_string()?;
    Ok(r)
}

fn get_download_agent(options: Option<&HttpOptions>) -> Result<ureq::Agent, Error> {
    // let tls_builder = native_tls::TlsConnector::builder();
    // let tls_connector = tls_builder.build()?;
    // Ok(ureq::AgentBuilder::new().tls_connector(tls_connector.into()).build())
    let mut config = ureq::Agent::config_builder();
    let timeout_ms = options.map(|o| o.TimeoutMilliseconds).unwrap_or(0);
    if timeout_ms > 0 {
        config = config.timeout_global(Some(Duration::from_millis(timeout_ms)));
    }
    Ok(config.build().into())
}

/// Builds the pooled agent for ranged chunk requests (§5.5, pinned literally). Statuses and redirects are
/// returned as responses so the reader can classify them itself. Each request raises the whole-request
/// timeouts to [`range_request_timeout`] of its length.
pub fn create_range_agent() -> ureq::Agent {
    ureq::Agent::config_builder()
        .http_status_as_error(false)
        .max_redirects(0)
        .max_redirects_will_error(false)
        .max_idle_connections_per_host(8)
        .timeout_connect(Some(Duration::from_secs(30)))
        .timeout_recv_response(Some(Duration::from_secs(30)))
        .timeout_recv_body(Some(Duration::from_secs(REQUEST_TIMEOUT_SECS)))
        .timeout_global(Some(Duration::from_secs(REQUEST_TIMEOUT_SECS)))
        .accept_encoding("identity")
        .build()
        .into()
}

const MAX_REDIRECT_HOPS: u32 = 10;

struct CachedUrl {
    url: String,
    /// Bumped whenever the cached URL is replaced, so concurrent workers can tell a refresh they
    /// observed the failure of from one that already happened.
    generation: u64,
    /// False until the first resolution from the original URL completed.
    resolved: bool,
}

/// Byte-range access to one HTTP asset (§8.2): a pooled agent, manual redirects with the final URL cached,
/// single-flight re-resolution when a cached (signed) URL stops working, and strict 206 validation.
pub struct HttpRangeReader {
    agent: ureq::Agent,
    original_url: String,
    headers: Vec<HttpHeader>,
    cached: Mutex<CachedUrl>,
    refresh_lock: Mutex<()>,
    len: Mutex<Option<u64>>,
    consecutive_refreshes: AtomicU32,
}

/// The whole-request timeout of a ranged request of `len` bytes: 300 s plus the time the body takes at
/// 16 KiB/s, so a large coalesced request on a slow link can still complete instead of timing out on every
/// attempt.
pub fn range_request_timeout(len: u64) -> Duration {
    Duration::from_secs(REQUEST_TIMEOUT_SECS + len / MIN_RANGE_THROUGHPUT_BYTES_PER_SEC)
}

/// Whether two URLs have the same origin (scheme, host and effective port). `Authorization` is only ever sent
/// to the origin of the original URL: never to another host or port, and never downgraded from https to http.
fn same_origin(a: &str, b: &str) -> bool {
    match (url::Url::parse(a), url::Url::parse(b)) {
        (Ok(a), Ok(b)) => {
            let (a, b) = (a.origin(), b.origin());
            a.is_tuple() && a == b
        }
        _ => false,
    }
}

fn map_ureq_error(e: ureq::Error) -> RangeError {
    match e {
        ureq::Error::BadUri(_) | ureq::Error::Http(_) | ureq::Error::InvalidProxyUrl => RangeError::Unavailable(e.to_string()),
        other => RangeError::Transient(other.to_string()),
    }
}

/// Parses `bytes start-end/total` (the total must be a number: `*` is not acceptable for a package).
fn parse_content_range(value: &str) -> Option<(u64, u64, u64)> {
    let rest = value.trim().strip_prefix("bytes")?.trim_start();
    let (range, total) = rest.split_once('/')?;
    let (start, end) = range.split_once('-')?;
    Some((start.trim().parse().ok()?, end.trim().parse().ok()?, total.trim().parse().ok()?))
}

fn header_str<'a>(headers: &'a ureq::http::HeaderMap, name: &str) -> Option<&'a str> {
    headers.get(name).and_then(|v| v.to_str().ok())
}

impl HttpRangeReader {
    /// Creates a reader for `url`; no request is made until the first read. `headers` are sent with
    /// every request (`Authorization` is dropped when a redirect leaves the original origin).
    pub fn open(agent: ureq::Agent, url: &str, headers: &[HttpHeader]) -> Result<HttpRangeReader, RangeError> {
        url::Url::parse(url).map_err(|e| RangeError::Unavailable(format!("invalid url '{}': {}", url, e)))?;
        Ok(HttpRangeReader {
            agent,
            original_url: url.to_string(),
            headers: headers.to_vec(),
            cached: Mutex::new(CachedUrl {
                url: url.to_string(),
                generation: 0,
                resolved: false,
            }),
            refresh_lock: Mutex::new(()),
            len: Mutex::new(None),
            consecutive_refreshes: AtomicU32::new(0),
        })
    }

    fn snapshot(&self) -> (String, u64, bool) {
        let c = self.cached.lock().unwrap();
        (c.url.clone(), c.generation, c.resolved)
    }

    /// Caches the final URL of a resolution. The generation is bumped when the URL changed or when
    /// `force` (a resolution from the original URL, whose waiters must retry rather than resolve again).
    fn cache(&self, final_url: &str, force: bool) {
        let mut c = self.cached.lock().unwrap();
        if force || c.url != final_url {
            c.url = final_url.to_string();
            c.generation += 1;
        }
        c.resolved = true;
    }

    /// One GET with a Range header, following up to 10 redirects by hand. Returns the final URL and the
    /// first non-redirect response (its body unread).
    fn request(&self, start_url: &str, offset: u64, len: usize) -> Result<(String, ureq::http::Response<ureq::Body>), RangeError> {
        let mut url = start_url.to_string();
        let mut hops = 0u32;
        let timeout = Some(range_request_timeout(len as u64));
        loop {
            let same_origin = same_origin(&url, &self.original_url);
            let mut req = self
                .agent
                .get(&url)
                .config()
                .timeout_global(timeout)
                .timeout_recv_body(timeout)
                .build()
                .header("Range", format!("bytes={}-{}", offset, offset + len as u64 - 1))
                .header("Accept-Encoding", "identity");
            for h in &self.headers {
                if !same_origin && h.Name.eq_ignore_ascii_case("authorization") {
                    continue;
                }
                req = req.header(&h.Name, &h.Value);
            }
            let response = req.call().map_err(map_ureq_error)?;
            let status = response.status().as_u16();
            if !matches!(status, 301 | 302 | 303 | 307 | 308) {
                return Ok((url, response));
            }
            hops += 1;
            if hops > MAX_REDIRECT_HOPS {
                return Err(RangeError::Unavailable(format!(
                    "more than {} redirects from '{}'",
                    MAX_REDIRECT_HOPS, start_url
                )));
            }
            let location = match header_str(response.headers(), "Location") {
                Some(l) => l.to_string(),
                None => return Err(RangeError::Unavailable(format!("redirect {} without a Location header", status))),
            };
            let base = url::Url::parse(&url).map_err(|e| RangeError::Unavailable(e.to_string()))?;
            url = base
                .join(&location)
                .map_err(|e| RangeError::Unavailable(format!("invalid redirect target '{}': {}", location, e)))?
                .to_string();
            debug!("Following redirect {} to '{}'", status, url);
        }
    }

    /// Validates a non-redirect response and reads its body into `buf`.
    fn consume(&self, response: ureq::http::Response<ureq::Body>, offset: u64, buf: &mut [u8]) -> Result<(), RangeError> {
        let status = response.status().as_u16();
        let expected = buf.len() as u64;
        let retry_after = header_str(response.headers(), "Retry-After").and_then(|v| v.trim().parse::<u64>().ok());
        match status {
            206 => {}
            200 => return Err(RangeError::Unavailable("the server ignored the Range header (200)".into())),
            416 => return Err(RangeError::Unavailable("the server rejected the range (416)".into())),
            408 | 425 | 429 | 500 | 502 | 503 | 504 => {
                let msg = format!("status {}", status);
                return Err(match retry_after {
                    Some(secs) => transient_with_retry_after(secs, &msg),
                    None => RangeError::Transient(msg),
                });
            }
            400..=499 => return Err(RangeError::Unavailable(format!("status {}", status))),
            _ => return Err(RangeError::Transient(format!("status {}", status))),
        }

        let headers = response.headers();
        if let Some(enc) = header_str(headers, "Content-Encoding") {
            if !enc.trim().eq_ignore_ascii_case("identity") {
                return Err(RangeError::Unavailable(format!("unexpected Content-Encoding '{}'", enc)));
            }
        }
        let (start, end, total) = match header_str(headers, "Content-Range").and_then(parse_content_range) {
            Some(r) => r,
            None => return Err(RangeError::Unavailable("missing or malformed Content-Range".into())),
        };
        if start != offset || end != offset + expected - 1 {
            return Err(RangeError::Unavailable(format!(
                "Content-Range {}-{} does not match the requested {}-{}",
                start,
                end,
                offset,
                offset + expected - 1
            )));
        }
        // ureq strips Content-Length together with Content-Encoding when it transparently decodes a compressed
        // body, so a length-delimited response without Content-Length means the range was compressed. A chunked
        // body has no Content-Length either; its length is enforced while reading (and every byte is hash-verified).
        match header_str(headers, "Content-Length").and_then(|v| v.trim().parse::<u64>().ok()) {
            Some(cl) if cl == expected => {}
            Some(cl) => {
                return Err(RangeError::Unavailable(format!(
                    "Content-Length {} does not match the range length {}",
                    cl, expected
                )))
            }
            None if headers.contains_key("Transfer-Encoding") => {}
            None => return Err(RangeError::Unavailable("missing Content-Length (or a content-encoded body)".into())),
        }
        {
            let mut len = self.len.lock().unwrap();
            match *len {
                Some(known) if known != total => return Err(RangeError::Unavailable(format!("the origin size changed from {} to {}", known, total))),
                _ => *len = Some(total),
            }
        }

        let mut reader = response.into_body().into_reader();
        reader.read_exact(buf).map_err(|e| RangeError::Transient(format!("short body: {}", e)))?;
        let mut probe = [0u8; 1];
        if let Ok(n) = reader.read(&mut probe) {
            if n > 0 {
                return Err(RangeError::Unavailable("the body is longer than the requested range".into()));
            }
        }
        self.consecutive_refreshes.store(0, Ordering::Relaxed);
        Ok(())
    }

    /// Resolves from the original URL (following redirects) and reads through the resulting response. The
    /// caller holds the refresh lock, so this is the single resolution every other worker waits for. A
    /// 401/403/404 from a freshly resolved redirect target counts as one failed refresh: the 3rd in a row
    /// without a successful read is fatal, otherwise the caller retries (and refreshes again).
    fn resolve_and_read(&self, offset: u64, buf: &mut [u8]) -> Result<(), RangeError> {
        let (final_url, response) = self.request(&self.original_url, offset, buf.len())?;
        self.cache(&final_url, true);
        let status = response.status().as_u16();
        if matches!(status, 401 | 403 | 404) && final_url != self.original_url {
            drop(response);
            let refreshes = self.consecutive_refreshes.fetch_add(1, Ordering::Relaxed) + 1;
            if refreshes >= MAX_CONSECUTIVE_REFRESHES {
                return Err(RangeError::Unavailable(format!(
                    "{} consecutive download URL refreshes without progress (status {})",
                    refreshes, status
                )));
            }
            return Err(RangeError::Transient(format!("status {} from the freshly resolved download URL", status)));
        }
        self.consume(response, offset, buf)
    }
}

/// How often one read may find that another worker replaced the cached URL under it before giving up
/// with a transient error (the retry policy then takes over).
const MAX_STALE_URL_RETRIES: u32 = 4;

impl RangeReader for HttpRangeReader {
    fn len(&self) -> Option<u64> {
        *self.len.lock().unwrap()
    }

    fn read_exact_at(&self, offset: u64, buf: &mut [u8]) -> Result<(), RangeError> {
        if buf.is_empty() {
            return Ok(());
        }
        for _ in 0..=MAX_STALE_URL_RETRIES {
            let (url, generation, resolved) = self.snapshot();
            if !resolved {
                // The first resolution is single-flight too: one API/redirect resolution per update.
                let _guard = self.refresh_lock.lock().unwrap();
                if !self.snapshot().2 {
                    return self.resolve_and_read(offset, buf);
                }
                continue;
            }
            let (final_url, response) = self.request(&url, offset, buf.len())?;
            if final_url != url {
                // Only a request that actually redirected may update the cache: a request made with a URL that
                // another worker has meanwhile refreshed must not write the stale one back.
                self.cache(&final_url, false);
            }
            let status = response.status().as_u16();
            if matches!(status, 401 | 403 | 404) && url != self.original_url {
                drop(response);
                // Single-flight re-resolution: the resolving request doubles as the retry. A worker that finds
                // the URL already refreshed by another one retries with the new URL at once, at no budget cost.
                let _guard = self.refresh_lock.lock().unwrap();
                if self.snapshot().1 != generation {
                    continue;
                }
                info!("Re-resolving the download URL after status {} from the cached redirect target", status);
                return self.resolve_and_read(offset, buf);
            }
            return self.consume(response, offset, buf);
        }
        Err(RangeError::Transient(
            "the download URL kept changing while it was being read".to_string(),
        ))
    }
}

#[cfg(test)]
mod range_tests {
    use super::*;
    use crate::chunks::fetcher::retry_after_of;

    #[test]
    fn content_range_parses_strictly() {
        assert_eq!(parse_content_range("bytes 0-99/1000"), Some((0, 99, 1000)));
        assert_eq!(parse_content_range(" bytes 5-9/10 "), Some((5, 9, 10)));
        assert_eq!(parse_content_range("bytes 0-99/*"), None);
        assert_eq!(parse_content_range("bytes */1000"), None);
        assert_eq!(parse_content_range("0-99/1000"), None);
    }

    #[test]
    fn retry_after_round_trips_through_the_message() {
        let e = transient_with_retry_after(7, "status 503");
        match e {
            RangeError::Transient(m) => assert_eq!(retry_after_of(&m), Some(Duration::from_secs(7))),
            _ => panic!(),
        }
        assert_eq!(retry_after_of("status 503"), None);
    }

    #[test]
    fn authorization_only_stays_with_the_same_origin() {
        assert!(same_origin("https://git.example.com/a/b", "https://GIT.example.com:443/c"));
        assert!(same_origin("http://127.0.0.1:8080/a", "http://127.0.0.1:8080/b"));
        assert!(
            !same_origin("https://git.example.com/a", "http://git.example.com/a"),
            "https -> http downgrade"
        );
        assert!(!same_origin("http://127.0.0.1:8080/a", "http://127.0.0.1:8081/a"), "another port");
        assert!(!same_origin("https://git.example.com/a", "https://cdn.example.com/a"), "another host");
        assert!(!same_origin("file:///c:/a", "file:///c:/a"));
    }

    #[test]
    fn range_request_timeout_scales_with_length() {
        assert_eq!(range_request_timeout(0), Duration::from_secs(300));
        assert_eq!(range_request_timeout(64 * 1024), Duration::from_secs(304));
        // an 8 MiB request has room for 16 KiB/s
        assert_eq!(range_request_timeout(8 * 1024 * 1024), Duration::from_secs(300 + 512));
    }
}

#[test]
fn test_download_uses_tls_and_encoding_correctly() {
    assert_eq!(
        download_url_as_string("https://dotnetcli.blob.core.windows.net/dotnet/WindowsDesktop/5.0/latest.version", None).unwrap(),
        "5.0.17"
    );
}

#[test]
fn test_download_file_reports_progress() {
    // https://www.ip-toolbox.com/speedtest-files/
    //"https://proof.ovh.net/files/10Mb.dat"
    let test_file = "https://velopacktesting.blob.core.windows.net/ci-test-files/10Mb.dat";
    let mut prog_count = 0;
    let mut last_prog = 0;

    let tmpfile = tempfile::NamedTempFile::new().unwrap();
    let tmppath = tmpfile.path();

    download_url_to_file(test_file, Path::new(tmppath), None, |p| {
        assert!(p >= last_prog);
        prog_count += 1;
        last_prog = p;
    })
    .unwrap();

    assert!(prog_count >= 4);
    assert!(prog_count <= 20);
    assert_eq!(last_prog, 100);

    let meta = tmppath.metadata().unwrap();
    let len = meta.len();

    assert_eq!(len, 10 * 1024 * 1024);
}

#[test]
fn test_interrupted_download() {
    use std::io::Write;
    use std::net::TcpListener;
    use std::thread;

    // Start a simple HTTP server that cuts the connection mid-download
    let listener = TcpListener::bind("127.0.0.1:0").expect("Failed to bind test server");
    let addr = listener.local_addr().unwrap();

    thread::spawn(move || {
        if let Ok((mut stream, _)) = listener.accept() {
            // Write a partial HTTP response
            let response = "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\n";
            stream.write_all(response.as_bytes()).expect("Failed to write response");

            // Send part of the data, then close the connection early
            let partial_data = vec![0u8; 1024]; // 1 KB
            stream.write_all(&partial_data).expect("Failed to write partial data");

            // Connection closes here, simulating an interrupted download
            thread::sleep(std::time::Duration::from_millis(100));
        }
    });

    let tmpfile = tempfile::NamedTempFile::new().unwrap();
    let result = download_url_to_file(&format!("http://{}", addr), tmpfile.path(), None, |_| {});

    assert!(result.is_err(), "Download should fail due to connection interruption");
}

#[test]
fn test_successful_download() {
    use std::io::Write;
    use std::net::TcpListener;
    use std::thread;

    // Start a simple HTTP server
    let listener = TcpListener::bind("127.0.0.1:0").expect("Failed to bind test server");
    let addr = listener.local_addr().unwrap();

    thread::spawn(move || {
        if let Ok((mut stream, _)) = listener.accept() {
            // Write a full HTTP response with full content
            let response = "HTTP/1.1 200 OK\r\nContent-Length: 10240\r\n\r\n";
            stream.write_all(response.as_bytes()).expect("Failed to write response");

            // Send full 10KB of data
            let full_data = vec![0u8; 10240];
            stream.write_all(&full_data).expect("Failed to write full data");

            // give client time to receive and disconnect
            thread::sleep(std::time::Duration::from_millis(100));
        }
    });

    let tmpfile = tempfile::NamedTempFile::new().unwrap();
    let _ = download_url_to_file(&format!("http://{}", addr), tmpfile.path(), None, |_| {}).unwrap();

    // Verify that the downloaded file has the expected size
    let metadata = tmpfile.path().metadata().unwrap();
    assert_eq!(metadata.len(), 10240, "Downloaded file size should match the expected content size");
}
