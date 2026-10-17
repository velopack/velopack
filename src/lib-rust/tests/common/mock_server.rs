//! A tiny HTTP/1.1 test server: fixed routes (`MockRoute`), byte-range served files with a scriptable
//! fault plan, redirects (plain and "signed URL" style that expire), and a request log.

use std::collections::VecDeque;
use std::io::{Read, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

#[derive(Clone)]
pub struct MockRoute {
    pub path_contains: String,
    pub response_code: u16,
    pub response_body: Vec<u8>,
    pub expected_headers: Vec<(String, String)>,
}

/// One scripted misbehaviour, applied to the next ranged file request that pops it.
#[derive(Clone, Debug)]
pub enum Fault {
    /// Serve the request normally.
    None,
    /// Close the connection after writing `n` body bytes (0 = right after the headers).
    ResetAfter(usize),
    /// Respond with this status and an empty body.
    Status(u16),
    /// Respond with this status and a `Retry-After: secs` header.
    StatusRetryAfter(u16, u64),
    /// Ignore the Range header: 200 with a very large body (the client must not read it).
    IgnoreRange,
    /// 206 whose Content-Range does not match the request.
    WrongContentRange,
    /// 206 whose body is `n` bytes short of the advertised Content-Length.
    ShortBody(usize),
    /// Delay the response.
    Stall(Duration),
    /// 416 Range Not Satisfiable.
    Range416,
    /// 206 with `Content-Encoding: gzip` and a gzip body.
    Gzip,
    /// 206 with one body byte flipped.
    CorruptByte(usize),
}

/// A logged request.
#[derive(Clone, Debug)]
pub struct LoggedRequest {
    pub method: String,
    /// Path including the query string.
    pub path: String,
    pub range: Option<(u64, u64)>,
    pub headers: Vec<(String, String)>,
    /// The fault that was applied, if any.
    pub fault: Option<String>,
}

#[derive(Clone)]
struct FileRoute {
    path: String,
    bytes: Arc<Vec<u8>>,
}

#[derive(Clone)]
struct Redirect {
    from: String,
    to: String,
}

/// A redirect whose target carries a generation that expires after `expire_after` successful responses;
/// requests for an expired generation get 403, so the client has to re-resolve.
struct SignedRedirect {
    from: String,
    to: String,
    generation: u64,
    successes: u64,
    expire_after: u64,
    /// Expired generations (403 forever).
    expired: Vec<u64>,
    /// The signed target never works (every generation is rejected).
    always_reject: bool,
}

#[derive(Default)]
struct State {
    routes: Vec<MockRoute>,
    files: Vec<FileRoute>,
    redirects: Vec<Redirect>,
    signed: Option<SignedRedirect>,
    faults: VecDeque<Fault>,
    always_ignore_range: bool,
    log: Vec<LoggedRequest>,
    /// Body bytes actually written for IgnoreRange responses before the client went away.
    ignore_range_bytes_sent: u64,
    expirations: u64,
    resolves: u64,
}

pub struct MockHttpServer {
    pub addr: SocketAddr,
    state: Arc<Mutex<State>>,
    _handle: thread::JoinHandle<()>,
}

/// Body size of an `IgnoreRange` 200 response (large, so a client that reads it is noticed).
pub const IGNORE_RANGE_BODY_LEN: u64 = 64 * 1024 * 1024;

impl MockHttpServer {
    /// Create a server with no routes initially. Use `add_route` to add routes before making requests.
    pub fn empty() -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("Failed to bind test server");
        let addr = listener.local_addr().unwrap();
        let state: Arc<Mutex<State>> = Arc::new(Mutex::new(State::default()));
        let state_clone = Arc::clone(&state);

        let handle = thread::spawn(move || {
            while let Ok((stream, _)) = listener.accept() {
                let state = Arc::clone(&state_clone);
                thread::spawn(move || handle_connection(stream, &state));
            }
        });

        MockHttpServer {
            addr,
            state,
            _handle: handle,
        }
    }

    pub fn add_route(&self, route: MockRoute) {
        self.state.lock().unwrap().routes.push(route);
    }

    /// Serves `bytes` at `path` with Range support (206 + Content-Range).
    pub fn add_file(&self, path: &str, bytes: Vec<u8>) {
        self.state.lock().unwrap().files.push(FileRoute {
            path: path.to_string(),
            bytes: Arc::new(bytes),
        });
    }

    /// Every request to `from` is answered with 302 to `to`.
    pub fn add_redirect(&self, from: &str, to: &str) {
        self.state.lock().unwrap().redirects.push(Redirect {
            from: from.to_string(),
            to: to.to_string(),
        });
    }

    /// Every request to `from` resolves to `to?gen=N` for a new generation N; a generation expires (403)
    /// after `expire_after` successful responses (0 = never). With `always_reject` every generation is 403.
    pub fn add_signed_redirect(&self, from: &str, to: &str, expire_after: u64, always_reject: bool) {
        self.state.lock().unwrap().signed = Some(SignedRedirect {
            from: from.to_string(),
            to: to.to_string(),
            generation: 0,
            successes: 0,
            expire_after,
            expired: Vec::new(),
            always_reject,
        });
    }

    /// Queues faults; each ranged file request pops the next one.
    pub fn push_faults(&self, faults: impl IntoIterator<Item = Fault>) {
        self.state.lock().unwrap().faults.extend(faults);
    }

    /// Makes the server ignore Range headers on every file request (200 with the whole file).
    pub fn set_always_ignore_range(&self, ignore: bool) {
        self.state.lock().unwrap().always_ignore_range = ignore;
    }

    pub fn requests(&self) -> Vec<LoggedRequest> {
        self.state.lock().unwrap().log.clone()
    }

    pub fn ignore_range_bytes_sent(&self) -> u64 {
        self.state.lock().unwrap().ignore_range_bytes_sent
    }

    /// How many times a signed generation expired.
    pub fn expirations(&self) -> u64 {
        self.state.lock().unwrap().expirations
    }

    /// How many times the signed redirect was resolved.
    pub fn resolves(&self) -> u64 {
        self.state.lock().unwrap().resolves
    }

    pub fn url(&self) -> String {
        format!("http://127.0.0.1:{}", self.addr.port())
    }
}

fn status_text(code: u16) -> &'static str {
    match code {
        200 => "OK",
        206 => "Partial Content",
        302 => "Found",
        403 => "Forbidden",
        404 => "Not Found",
        408 => "Request Timeout",
        416 => "Range Not Satisfiable",
        425 => "Too Early",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Unknown",
    }
}

fn write_response(stream: &mut TcpStream, code: u16, extra_headers: &[(String, String)], body: &[u8]) -> std::io::Result<()> {
    let mut head = format!(
        "HTTP/1.1 {} {}\r\nContent-Length: {}\r\nConnection: close\r\n",
        code,
        status_text(code),
        body.len()
    );
    for (k, v) in extra_headers {
        head.push_str(&format!("{}: {}\r\n", k, v));
    }
    head.push_str("\r\n");
    stream.write_all(head.as_bytes())?;
    stream.write_all(body)?;
    stream.flush()
}

fn parse_range(value: &str) -> Option<(u64, u64)> {
    let rest = value.trim().strip_prefix("bytes=")?;
    let (a, b) = rest.split_once('-')?;
    Some((a.trim().parse().ok()?, b.trim().parse().ok()?))
}

/// `(method, path, headers)` of a parsed request.
type ParsedRequest = (String, String, Vec<(String, String)>);

fn parse_request(buf: &[u8]) -> Option<ParsedRequest> {
    let text = String::from_utf8_lossy(buf);
    let mut lines = text.split("\r\n");
    let request_line = lines.next()?;
    let mut parts = request_line.split_whitespace();
    let method = parts.next()?.to_string();
    let path = parts.next()?.to_string();
    let mut headers = Vec::new();
    for line in lines {
        if line.is_empty() {
            break;
        }
        if let Some((k, v)) = line.split_once(':') {
            headers.push((k.trim().to_string(), v.trim().to_string()));
        }
    }
    Some((method, path, headers))
}

fn gzip(data: &[u8]) -> Vec<u8> {
    use flate2::write::GzEncoder;
    let mut enc = GzEncoder::new(Vec::new(), flate2::Compression::default());
    enc.write_all(data).unwrap();
    enc.finish().unwrap()
}

fn handle_connection(mut stream: TcpStream, state: &Arc<Mutex<State>>) {
    let mut buf = [0u8; 8192];
    let n = match stream.read(&mut buf) {
        Ok(n) => n,
        Err(_) => return,
    };
    let (method, path, headers) = match parse_request(&buf[..n]) {
        Some(r) => r,
        None => return,
    };
    let range = headers
        .iter()
        .find(|(k, _)| k.eq_ignore_ascii_case("Range"))
        .and_then(|(_, v)| parse_range(v));
    let request = String::from_utf8_lossy(&buf[..n]).to_string();
    let request_lower = request.to_lowercase();
    let path_only = path.split('?').next().unwrap_or("").to_string();
    let query = path.split_once('?').map(|(_, q)| q.to_string());

    let mut log = LoggedRequest {
        method,
        path: path.clone(),
        range,
        headers: headers.clone(),
        fault: None,
    };

    // Decide what to do under the lock, then release it before writing (stalls and large bodies must not
    // block other connections).
    enum Action {
        Legacy(MockRoute),
        Redirect(String),
        Status(u16, Vec<(String, String)>),
        File(Arc<Vec<u8>>, Option<Fault>, bool),
        NotFound,
    }
    let action = {
        let mut st = state.lock().unwrap();
        let mut action = Action::NotFound;
        if let Some(route) = st.routes.iter().find(|r| request.contains(&r.path_contains)).cloned() {
            action = Action::Legacy(route);
        } else if let Some(r) = st.redirects.iter().find(|r| r.from == path_only).cloned() {
            action = Action::Redirect(r.to);
        } else if st.signed.as_ref().map(|s| s.from == path_only).unwrap_or(false) {
            let signed = st.signed.as_mut().unwrap();
            signed.generation += 1;
            signed.successes = 0;
            let target = format!("{}?gen={}", signed.to, signed.generation);
            st.resolves += 1;
            action = Action::Redirect(target);
        } else if let Some(file) = st.files.iter().find(|f| f.path == path_only).cloned() {
            let mut rejected = false;
            let mut signed_hit = false;
            if let Some(signed) = st.signed.as_mut() {
                if signed.to == path_only {
                    signed_hit = true;
                    let gen: u64 = query
                        .as_deref()
                        .and_then(|q| q.strip_prefix("gen="))
                        .and_then(|g| g.parse().ok())
                        .unwrap_or(0);
                    if signed.always_reject || gen != signed.generation || signed.expired.contains(&gen) {
                        rejected = true;
                    }
                }
            }
            if rejected {
                action = Action::Status(403, Vec::new());
            } else {
                let fault = st.faults.pop_front();
                let ignore = st.always_ignore_range;
                if signed_hit && !matches!(fault, Some(Fault::Status(_)) | Some(Fault::StatusRetryAfter(_, _))) {
                    let signed = st.signed.as_mut().unwrap();
                    signed.successes += 1;
                    if signed.expire_after > 0 && signed.successes >= signed.expire_after {
                        let gen = signed.generation;
                        signed.expired.push(gen);
                        st.expirations += 1;
                    }
                }
                action = Action::File(file.bytes, fault, ignore);
            }
        }
        match &action {
            Action::File(_, Some(f), _) if !matches!(f, Fault::None) => log.fault = Some(format!("{:?}", f)),
            Action::Status(code, _) => log.fault = Some(format!("status {}", code)),
            _ => {}
        }
        st.log.push(log);
        action
    };

    match action {
        Action::Legacy(route) => {
            for (name, value) in &route.expected_headers {
                let expected = format!("{}: {}", name, value).to_lowercase();
                assert!(
                    request_lower.contains(&expected),
                    "Expected header '{}:{}' not found in request:\n{}",
                    name,
                    value,
                    request
                );
            }
            let headers = vec![("Content-Type".to_string(), "application/octet-stream".to_string())];
            let _ = write_response(&mut stream, route.response_code, &headers, &route.response_body);
        }
        Action::Redirect(to) => {
            let _ = write_response(&mut stream, 302, &[("Location".to_string(), to)], b"");
        }
        Action::Status(code, headers) => {
            let _ = write_response(&mut stream, code, &headers, b"");
        }
        Action::NotFound => {
            let _ = write_response(&mut stream, 404, &[], b"");
        }
        Action::File(bytes, fault, ignore) => serve_file(&mut stream, &bytes, range, fault, ignore, state),
    }
}

fn serve_file(stream: &mut TcpStream, bytes: &[u8], range: Option<(u64, u64)>, fault: Option<Fault>, ignore: bool, state: &Arc<Mutex<State>>) {
    let total = bytes.len() as u64;
    // A plain (unranged) GET is always served as a normal 200: that is the classic full download.
    let fault = if ignore && range.is_some() {
        Some(Fault::IgnoreRange)
    } else {
        fault
    };
    let (start, end) = match range {
        Some((s, e)) if s <= e && e < total => (s, e),
        Some(_) => {
            let _ = write_response(stream, 416, &[("Content-Range".to_string(), format!("bytes */{}", total))], b"");
            return;
        }
        None => (0, total.saturating_sub(1)),
    };
    let slice = &bytes[start as usize..=end as usize];
    let content_range = |s: u64, e: u64| ("Content-Range".to_string(), format!("bytes {}-{}/{}", s, e, total));
    match fault {
        None | Some(Fault::None) => {
            if range.is_some() {
                let _ = write_response(stream, 206, &[content_range(start, end)], slice);
            } else {
                let _ = write_response(stream, 200, &[], bytes);
            }
        }
        Some(Fault::Status(code)) => {
            let _ = write_response(stream, code, &[], b"");
        }
        Some(Fault::StatusRetryAfter(code, secs)) => {
            let _ = write_response(stream, code, &[("Retry-After".to_string(), secs.to_string())], b"");
        }
        Some(Fault::Range416) => {
            let _ = write_response(stream, 416, &[("Content-Range".to_string(), format!("bytes */{}", total))], b"");
        }
        Some(Fault::WrongContentRange) => {
            let _ = write_response(stream, 206, &[content_range(start + 1, end + 1)], slice);
        }
        Some(Fault::CorruptByte(i)) => {
            let mut body = slice.to_vec();
            let i = i.min(body.len().saturating_sub(1));
            body[i] ^= 0xFF;
            let _ = write_response(stream, 206, &[content_range(start, end)], &body);
        }
        Some(Fault::Gzip) => {
            let body = gzip(slice);
            let headers = [content_range(start, end), ("Content-Encoding".to_string(), "gzip".to_string())];
            let _ = write_response(stream, 206, &headers, &body);
        }
        Some(Fault::Stall(d)) => {
            thread::sleep(d);
            let _ = write_response(stream, 206, &[content_range(start, end)], slice);
        }
        Some(Fault::ShortBody(n)) => {
            let head = format!(
                "HTTP/1.1 206 Partial Content\r\nContent-Length: {}\r\nContent-Range: bytes {}-{}/{}\r\nConnection: close\r\n\r\n",
                slice.len(),
                start,
                end,
                total
            );
            let _ = stream.write_all(head.as_bytes());
            let keep = slice.len().saturating_sub(n);
            let _ = stream.write_all(&slice[..keep]);
            let _ = stream.flush();
        }
        Some(Fault::ResetAfter(n)) => {
            let head = format!(
                "HTTP/1.1 206 Partial Content\r\nContent-Length: {}\r\nContent-Range: bytes {}-{}/{}\r\nConnection: close\r\n\r\n",
                slice.len(),
                start,
                end,
                total
            );
            let _ = stream.write_all(head.as_bytes());
            let _ = stream.write_all(&slice[..n.min(slice.len())]);
            let _ = stream.flush();
            // dropping the stream closes the connection mid-body
        }
        Some(Fault::IgnoreRange) => {
            // A 200 with a huge body: the client must drop the response without reading it.
            let body_len = IGNORE_RANGE_BODY_LEN.max(total);
            let head = format!("HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", body_len);
            if stream.write_all(head.as_bytes()).is_err() {
                return;
            }
            let mut sent = 0u64;
            let zeros = vec![0u8; 64 * 1024];
            let mut ok = stream.write_all(bytes).is_ok();
            if ok {
                sent = total;
            }
            while ok && sent < body_len {
                let n = (body_len - sent).min(zeros.len() as u64) as usize;
                ok = stream.write_all(&zeros[..n]).is_ok();
                if ok {
                    sent += n as u64;
                }
            }
            let _ = stream.flush();
            state.lock().unwrap().ignore_range_bytes_sent += sent;
        }
    }
}
