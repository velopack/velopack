//! A small, language-agnostic IPC layer shared with the C# library (`Velopack.Ipc`): both derive the identical
//! endpoint for an app and speak the identical protocol, so a C# app can talk to a Rust client and vice versa.
//!
//! Transport: a named pipe on Windows, a Unix domain socket elsewhere. Framing: a `u32` little-endian length prefix
//! followed by that many bytes of compact UTF-8 JSON. Envelope: an [`IpcMessage`] with a protocol `version`, a
//! `type`, an optional correlation `id`, and type-specific fields. A receiver answers a message of a newer major
//! version with `unsupported version`, an unknown `type` with `unknown message type`, and never panics on input.
//!
//! The v1 use is `SingleInstance` association launches (one `association` request, one `ack`/`error` reply, then
//! both sides close). The framing allows any number of frames in either direction, which is what the Update.exe
//! <-> hook process channel (#303) will build on.

use crate::{lockfile::LockFile, Error};
use serde::{Deserialize, Serialize};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

#[cfg(windows)]
#[path = "windows.rs"]
pub(crate) mod os;
#[cfg(unix)]
#[path = "unix.rs"]
pub(crate) mod os;

/// The protocol version this library speaks. Receivers accept any version up to and including this one.
pub const PROTOCOL_VERSION: u32 = 1;
/// The largest frame payload either side accepts.
pub const MAX_MESSAGE_BYTES: usize = 1_048_576;
/// How long a client waits for its request to be sent and answered.
pub const EXCHANGE_TIMEOUT: Duration = Duration::from_secs(5);
/// How long an association launch keeps trying to reach the running instance before handling the activation itself.
pub const CONNECT_WINDOW: Duration = Duration::from_secs(5);
/// The pause between connection attempts within [`CONNECT_WINDOW`].
pub const RETRY_INTERVAL: Duration = Duration::from_millis(100);
/// The per-connection read/write timeout on the server side.
pub const SERVER_IO_TIMEOUT: Duration = Duration::from_secs(5);

/// Message type: an association activation (`items` carry `--veloapp-*` flag/value pairs).
pub const TYPE_ASSOCIATION: &str = "association";
/// Message type: the request was handled.
pub const TYPE_ACK: &str = "ack";
/// Message type: the request was rejected (`message` says why, with one of the fixed `ERR_*` strings).
pub const TYPE_ERROR: &str = "error";

/// Error message: the frame was not a valid message (bad JSON, missing fields, version 0).
pub const ERR_INVALID_MESSAGE: &str = "invalid message";
/// Error message: the message version is newer than [`PROTOCOL_VERSION`].
pub const ERR_UNSUPPORTED_VERSION: &str = "unsupported version";
/// Error message: the message type is unknown to this receiver.
pub const ERR_UNKNOWN_MESSAGE_TYPE: &str = "unknown message type";
/// Error message: an association item has an unknown flag, an empty value, or no hook registered for it.
pub const ERR_UNHANDLED_ASSOCIATION: &str = "unhandled association";

const KEY_PREFIX: &str = "velopack-ipc-v1";

/// One association activation inside an [`IpcMessage`]: the canonical CLI flag and its value.
#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct IpcItem {
    /// `--veloapp-fileassoc` or `--veloapp-urlproto`.
    pub flag: String,
    /// The file path or URL. Never logged.
    pub value: String,
}

/// The message envelope. Unknown fields are ignored, so new optional fields and types can be added without
/// bumping the version.
#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct IpcMessage {
    /// The protocol version of the sender (required; 0 is invalid).
    pub version: u32,
    /// The message type (`association`, `ack`, `error`, ...).
    #[serde(rename = "type")]
    pub message_type: String,
    /// An opaque correlation id (at most 64 chars); a reply copies the request's id.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
    /// The association items of an `association` message.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub items: Vec<IpcItem>,
    /// The reason of an `error` message.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub message: Option<String>,
}

impl IpcMessage {
    /// A message of the given type at the current protocol version.
    pub fn new(message_type: &str) -> Self {
        IpcMessage {
            version: PROTOCOL_VERSION,
            message_type: message_type.to_owned(),
            ..Default::default()
        }
    }

    /// An `ack` reply.
    pub fn ack() -> Self {
        Self::new(TYPE_ACK)
    }

    /// An `error` reply with the given (fixed, value-free) message.
    pub fn error(message: &str) -> Self {
        let mut m = Self::new(TYPE_ERROR);
        m.message = Some(message.to_owned());
        m
    }

    /// Copies the correlation `id` of `request` into this reply.
    pub fn reply_to(mut self, request: &IpcMessage) -> Self {
        self.id = request.id.clone();
        self
    }

    /// Whether this is an `ack`.
    pub fn is_ack(&self) -> bool {
        self.message_type == TYPE_ACK
    }
}

/// Writes one frame (length prefix + compact JSON) to `w`.
pub fn write_frame<W: Write>(w: &mut W, msg: &IpcMessage) -> io::Result<()> {
    let json = serde_json::to_vec(msg).map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;
    if json.is_empty() || json.len() > MAX_MESSAGE_BYTES {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "message too large"));
    }
    w.write_all(&(json.len() as u32).to_le_bytes())?;
    w.write_all(&json)?;
    Ok(())
}

/// Reads one frame from `r`, returning its raw payload. A length of 0, a length above [`MAX_MESSAGE_BYTES`] or an
/// EOF inside the frame is an error.
pub fn read_frame<R: Read>(r: &mut R) -> io::Result<Vec<u8>> {
    let mut len = [0u8; 4];
    r.read_exact(&mut len)?;
    let len = u32::from_le_bytes(len) as usize;
    if len == 0 || len > MAX_MESSAGE_BYTES {
        return Err(io::Error::new(io::ErrorKind::InvalidData, format!("invalid frame length {len}")));
    }
    let mut buf = vec![0u8; len];
    r.read_exact(&mut buf)?;
    Ok(buf)
}

/// Decodes a frame payload. A missing `version` or `type` is an error, as is any malformed JSON.
pub fn decode(payload: &[u8]) -> Result<IpcMessage, serde_json::Error> {
    serde_json::from_slice(payload)
}

/// Where an app's IPC server lives: the derived key, the transport address (pipe name / socket path) and the lock
/// file whose holder is the server.
#[derive(Debug, Clone, PartialEq)]
pub struct IpcEndpoint {
    /// 24 lowercase hex chars derived from app id, install root and user ([`derive_key`]), or a caller-chosen key
    /// ([`IpcEndpoint::from_key`]).
    pub key: String,
    /// `\\.\pipe\velopack-{key}` on Windows, `{tmp}/velopack-{uid}/{key}.sock` elsewhere.
    pub address: PathBuf,
    /// `{LocalAppData}\velopack\ipc\{key}.lock` on Windows, `{tmp}/velopack-{uid}/{key}.lock` elsewhere.
    pub lock_path: PathBuf,
}

impl IpcEndpoint {
    /// The endpoint of the installed app with manifest id `app_id` at install root `root` for the current user.
    /// Fails (IPC disabled) when the inputs are empty, the user cannot be determined, or the endpoint location
    /// cannot be prepared.
    pub fn for_app(app_id: &str, root: &Path) -> Result<IpcEndpoint, Error> {
        if app_id.is_empty() || root.as_os_str().is_empty() {
            return Err(Error::Other("IPC endpoint needs an app id and an install root".into()));
        }
        let user = os::current_user()?;
        let key = derive_key(app_id, &normalized_root(root), &user);
        Self::from_key(&key)
    }

    /// The endpoint for an arbitrary key (`[0-9a-z-]{1,40}`), eg. a random one chosen by a parent process and
    /// passed to a child through the environment.
    pub fn from_key(key: &str) -> Result<IpcEndpoint, Error> {
        let valid = !key.is_empty() && key.len() <= 40 && key.bytes().all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-');
        if !valid {
            return Err(Error::Other(format!("invalid IPC key '{key}'")));
        }
        os::endpoint_for_key(key)
    }
}

/// The install root as the endpoint hashes it: the final (canonical) path when it resolves, then
/// [`normalize_root_windows`] / [`trim_root_unix`].
pub fn normalized_root(root: &Path) -> String {
    let resolved = std::fs::canonicalize(root).unwrap_or_else(|_| root.to_path_buf());
    let text = resolved.to_string_lossy();
    #[cfg(windows)]
    {
        normalize_root_windows(&text)
    }
    #[cfg(unix)]
    {
        trim_root_unix(&text)
    }
}

/// The pure part of the Windows root normalization: `/` → `\`, the `\\?\` and `\\?\UNC\` prefixes removed, trailing
/// `\` trimmed, ASCII letters lowercased (non-ASCII is left as the file system reports it).
pub fn normalize_root_windows(root: &str) -> String {
    let mut s = root.replace('/', "\\");
    if let Some(rest) = s.strip_prefix("\\\\?\\UNC\\") {
        s = format!("\\\\{rest}");
    } else if let Some(rest) = s.strip_prefix("\\\\?\\") {
        s = rest.to_owned();
    }
    let trimmed = s.trim_end_matches('\\');
    trimmed.to_ascii_lowercase()
}

/// The pure part of the Unix root normalization: trailing `/` trimmed while longer than one character.
pub fn trim_root_unix(root: &str) -> String {
    let mut s = root;
    while s.len() > 1 && s.ends_with('/') {
        s = &s[..s.len() - 1];
    }
    s.to_owned()
}

/// The endpoint key: the first 12 bytes of SHA-256 over `"velopack-ipc-v1\0{app_id}\0{normalized_root}\0{user}"`,
/// as lowercase hex.
pub fn derive_key(app_id: &str, normalized_root: &str, user: &str) -> String {
    use sha2::Digest;
    let mut hasher = sha2::Sha256::new();
    hasher.update(KEY_PREFIX.as_bytes());
    hasher.update(b"\0");
    hasher.update(app_id.as_bytes());
    hasher.update(b"\0");
    hasher.update(normalized_root.as_bytes());
    hasher.update(b"\0");
    hasher.update(user.as_bytes());
    let digest = hasher.finalize();
    digest[..12].iter().map(|b| format!("{b:02x}")).collect()
}

/// One end of a connection. Reads and writes are blocking, bounded by [`IpcConnection::set_timeout`].
pub struct IpcConnection {
    inner: os::Connection,
}

impl IpcConnection {
    /// Applies `timeout` to every later receive and send (`None` = wait forever). A timed-out operation fails with
    /// `ErrorKind::TimedOut`.
    pub fn set_timeout(&mut self, timeout: Option<Duration>) {
        self.inner.set_timeout(timeout);
    }

    /// Receives one message.
    pub fn receive(&mut self) -> Result<IpcMessage, Error> {
        let payload = read_frame(self)?;
        Ok(decode(&payload)?)
    }

    /// Sends one message.
    pub fn send(&mut self, msg: &IpcMessage) -> Result<(), Error> {
        write_frame(self, msg)?;
        Ok(())
    }
}

impl Read for IpcConnection {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        self.inner.read(buf)
    }
}

impl Write for IpcConnection {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        self.inner.write(buf)
    }

    fn flush(&mut self) -> io::Result<()> {
        self.inner.flush()
    }
}

/// Connects to the server at `ep`. `Ok(None)` means nobody is listening (the caller may retry); `Err` means the
/// endpoint is unusable or the server could not be verified as running under the current user, in which case
/// nothing has been sent.
pub fn connect(ep: &IpcEndpoint) -> Result<Option<IpcConnection>, Error> {
    Ok(os::connect(ep)?.map(|inner| IpcConnection { inner }))
}

/// Sends `msg` and waits for the single reply, on the calling thread, bounded by `timeout`.
pub fn request(mut conn: IpcConnection, msg: &IpcMessage, timeout: Duration) -> Result<IpcMessage, Error> {
    conn.set_timeout(Some(timeout));
    conn.send(msg)?;
    conn.receive()
}

/// Binds the server at `ep` (synchronously, so a failure is reported here) and then accepts connections for the
/// rest of the process on a `velopack-ipc` thread, which owns `lock` and hands every connection to
/// `on_connection` on a thread of its own. There is no stop API.
pub fn start_server(ep: &IpcEndpoint, lock: Option<LockFile>, on_connection: Arc<dyn Fn(IpcConnection) + Send + Sync>) -> Result<(), Error> {
    let mut listener = os::bind(ep)?;
    std::thread::Builder::new()
        .name("velopack-ipc".into())
        .spawn(move || {
            let _lock = lock;
            loop {
                match listener.accept() {
                    Ok(inner) => {
                        let on_connection = on_connection.clone();
                        let _ = std::thread::Builder::new()
                            .name("velopack-ipc-conn".into())
                            .spawn(move || on_connection(IpcConnection { inner }));
                    }
                    Err(e) => {
                        warn!("IPC: accept failed: {e}");
                        std::thread::sleep(RETRY_INTERVAL);
                    }
                }
            }
        })
        .map_err(Error::Io)?;
    Ok(())
}

/// Serves one request/reply exchange on `conn`: receives a message (within [`SERVER_IO_TIMEOUT`]), validates the
/// envelope, answers with `handler`'s reply (the handler copies the `id`), then flushes. The caller closes the
/// connection by dropping it.
pub fn serve_request(conn: &mut IpcConnection, handler: impl FnOnce(IpcMessage) -> IpcMessage) {
    conn.set_timeout(Some(SERVER_IO_TIMEOUT));
    serve_request_io(conn, handler);
}

/// The transport-independent part of [`serve_request`]. A framing or I/O error ends the exchange without a reply;
/// a malformed message or a version of 0 is answered with `invalid message`, a newer version with
/// `unsupported version`.
pub fn serve_request_io<S: Read + Write>(stream: &mut S, handler: impl FnOnce(IpcMessage) -> IpcMessage) {
    let payload = match read_frame(stream) {
        Ok(p) => p,
        Err(e) => {
            debug!("IPC: dropping connection without a reply: {e}");
            return;
        }
    };
    let reply = match decode(&payload) {
        Err(_) => IpcMessage::error(ERR_INVALID_MESSAGE),
        Ok(msg) if msg.version == 0 => IpcMessage::error(ERR_INVALID_MESSAGE).reply_to(&msg),
        Ok(msg) if msg.version > PROTOCOL_VERSION => IpcMessage::error(ERR_UNSUPPORTED_VERSION).reply_to(&msg),
        Ok(msg) => handler(msg),
    };
    if let Err(e) = write_frame(stream, &reply).and_then(|_| stream.flush()) {
        debug!("IPC: reply could not be written: {e}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;
    use std::sync::Mutex;

    #[test]
    fn test_derive_key_golden_vectors() {
        assert_eq!(
            derive_key("MyCoolApp", "c:\\users\\me\\appdata\\local\\mycoolapp", "S-1-5-21-1-2-3-1001"),
            "8785120f64c79b6ab6fe628e"
        );
        assert_eq!(
            derive_key("MyCoolApp", "/Applications/My Cool App.app", "501"),
            "92693b0765d0a044649c36a8"
        );
        assert_eq!(
            derive_key("\u{00DC}nicode", "/Users/j\u{00F6}rg/Applications/x.app", "502"),
            "e8c1d0ef1783a52f5b53b6de"
        );
    }

    #[test]
    fn test_normalize_root_vectors() {
        let expected = "c:\\users\\me\\appdata\\local\\mycoolapp";
        assert_eq!(normalize_root_windows("C:\\Users\\Me\\AppData\\Local\\MyCoolApp\\"), expected);
        assert_eq!(normalize_root_windows("\\\\?\\C:\\Users\\Me\\AppData\\Local\\MyCoolApp"), expected);
        assert_eq!(normalize_root_windows("C:/Users/Me/AppData/Local/MyCoolApp"), expected);
        assert_eq!(normalize_root_windows("\\\\?\\UNC\\Server\\Share\\App"), "\\\\server\\share\\app");
        assert_eq!(normalize_root_windows("C:\\Pr\u{00F6}gram\\\u{00C4}PP"), "c:\\pr\u{00F6}gram\\\u{00C4}pp");
        assert_eq!(trim_root_unix("/Applications/My App.app/"), "/Applications/My App.app");
        assert_eq!(trim_root_unix("/"), "/");
    }

    #[cfg(windows)]
    #[test]
    fn test_normalized_root_resolves_short_names() {
        use windows::Win32::Storage::FileSystem::GetShortPathNameW;
        let dir = tempfile::Builder::new().prefix("velopack ipc long name ").tempdir().unwrap();
        let long = dir.path().to_path_buf();
        let wide = crate::wide_strings::string_to_wide(long.to_string_lossy().to_string());
        let mut buf = vec![0u16; 1024];
        let n = unsafe { GetShortPathNameW(wide.as_pcwstr(), Some(&mut buf)) } as usize;
        assert!(n > 0 && n < buf.len());
        let short = PathBuf::from(String::from_utf16_lossy(&buf[..n]));
        if short == long {
            return; // the volume has no 8.3 names
        }
        assert_eq!(normalized_root(&short), normalized_root(&long));
        assert_eq!(
            normalized_root(&long),
            normalize_root_windows(&std::fs::canonicalize(&long).unwrap().to_string_lossy())
        );
    }

    #[test]
    fn test_from_key_validation() {
        assert!(IpcEndpoint::from_key("").is_err());
        assert!(IpcEndpoint::from_key("ABC").is_err());
        assert!(IpcEndpoint::from_key("a b").is_err());
        assert!(IpcEndpoint::from_key(&"a".repeat(41)).is_err());
        let ep = IpcEndpoint::from_key("abc-123").unwrap();
        assert_eq!(ep.key, "abc-123");
        assert!(ep.address.to_string_lossy().contains("velopack-"));
        assert!(ep.lock_path.to_string_lossy().ends_with("abc-123.lock"));
    }

    fn association(value: &str) -> IpcMessage {
        let mut m = IpcMessage::new(TYPE_ASSOCIATION);
        m.items.push(IpcItem {
            flag: "--veloapp-urlproto".into(),
            value: value.into(),
        });
        m
    }

    #[test]
    fn test_frame_round_trip_and_json_shape() {
        let msg = association("myapp://x");
        let mut buf = Vec::new();
        write_frame(&mut buf, &msg).unwrap();
        let json = std::str::from_utf8(&buf[4..]).unwrap();
        assert_eq!(
            json,
            r#"{"version":1,"type":"association","items":[{"flag":"--veloapp-urlproto","value":"myapp://x"}]}"#
        );
        assert_eq!(u32::from_le_bytes(buf[..4].try_into().unwrap()) as usize, json.len());
        let payload = read_frame(&mut Cursor::new(&buf)).unwrap();
        assert_eq!(decode(&payload).unwrap(), msg);

        let mut buf = Vec::new();
        write_frame(&mut buf, &IpcMessage::ack()).unwrap();
        assert_eq!(std::str::from_utf8(&buf[4..]).unwrap(), r#"{"version":1,"type":"ack"}"#);
        let mut buf = Vec::new();
        write_frame(&mut buf, &IpcMessage::error(ERR_UNKNOWN_MESSAGE_TYPE)).unwrap();
        assert_eq!(
            std::str::from_utf8(&buf[4..]).unwrap(),
            r#"{"version":1,"type":"error","message":"unknown message type"}"#
        );

        // unknown fields and any order are accepted; a missing version or type is not
        let m = decode(br#"{"extra":{"a":[1,2]},"type":"ack","id":"7","version":1}"#).unwrap();
        assert_eq!(m.id.as_deref(), Some("7"));
        assert!(m.is_ack());
        assert!(decode(br#"{"type":"ack"}"#).is_err());
        assert!(decode(br#"{"version":1}"#).is_err());
    }

    #[test]
    fn test_read_frame_rejects_bad_lengths_and_truncation() {
        let zero = 0u32.to_le_bytes();
        assert_eq!(read_frame(&mut Cursor::new(&zero)).unwrap_err().kind(), io::ErrorKind::InvalidData);
        let huge = ((MAX_MESSAGE_BYTES + 1) as u32).to_le_bytes();
        assert_eq!(read_frame(&mut Cursor::new(&huge)).unwrap_err().kind(), io::ErrorKind::InvalidData);
        let mut truncated = 10u32.to_le_bytes().to_vec();
        truncated.extend_from_slice(b"{\"ver");
        assert_eq!(read_frame(&mut Cursor::new(&truncated)).unwrap_err().kind(), io::ErrorKind::UnexpectedEof);
        assert_eq!(read_frame(&mut Cursor::new(&[1u8, 0])).unwrap_err().kind(), io::ErrorKind::UnexpectedEof);
    }

    struct Duplex {
        input: Cursor<Vec<u8>>,
        output: Vec<u8>,
    }

    impl Read for Duplex {
        fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
            self.input.read(buf)
        }
    }

    impl Write for Duplex {
        fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
            self.output.write(buf)
        }
        fn flush(&mut self) -> io::Result<()> {
            Ok(())
        }
    }

    fn serve(payload: &[u8], handler: impl FnOnce(IpcMessage) -> IpcMessage) -> Option<IpcMessage> {
        let mut input = (payload.len() as u32).to_le_bytes().to_vec();
        input.extend_from_slice(payload);
        let mut duplex = Duplex {
            input: Cursor::new(input),
            output: Vec::new(),
        };
        serve_request_io(&mut duplex, handler);
        if duplex.output.is_empty() {
            return None;
        }
        Some(decode(&read_frame(&mut Cursor::new(&duplex.output)).unwrap()).unwrap())
    }

    #[test]
    fn test_serve_request_replies() {
        let unreachable = |_: IpcMessage| panic!("handler must not run");

        let reply = serve(b"not json", unreachable).unwrap();
        assert_eq!(
            (reply.message_type.as_str(), reply.message.as_deref()),
            (TYPE_ERROR, Some(ERR_INVALID_MESSAGE))
        );

        let reply = serve(br#"{"version":2,"type":"ack","id":"x"}"#, unreachable).unwrap();
        assert_eq!(
            (reply.message_type.as_str(), reply.message.as_deref()),
            (TYPE_ERROR, Some(ERR_UNSUPPORTED_VERSION))
        );
        assert_eq!(reply.id.as_deref(), Some("x"));

        let reply = serve(br#"{"version":0,"type":"ack"}"#, unreachable).unwrap();
        assert_eq!(
            (reply.message_type.as_str(), reply.message.as_deref()),
            (TYPE_ERROR, Some(ERR_INVALID_MESSAGE))
        );

        // the handler's reply (error or ack) passes through, and it is responsible for the id
        let reply = serve(br#"{"version":1,"type":"bogus","id":"abc"}"#, |m| {
            IpcMessage::error(ERR_UNKNOWN_MESSAGE_TYPE).reply_to(&m)
        })
        .unwrap();
        assert_eq!(
            (reply.message_type.as_str(), reply.message.as_deref()),
            (TYPE_ERROR, Some(ERR_UNKNOWN_MESSAGE_TYPE))
        );
        assert_eq!(reply.id.as_deref(), Some("abc"));
        let reply = serve(br#"{"version":1,"type":"association","id":"42","items":[]}"#, |m| {
            IpcMessage::ack().reply_to(&m)
        })
        .unwrap();
        assert!(reply.is_ack());
        assert_eq!(reply.id.as_deref(), Some("42"));

        // a framing error gets no reply at all
        let mut duplex = Duplex {
            input: Cursor::new(0u32.to_le_bytes().to_vec()),
            output: Vec::new(),
        };
        serve_request_io(&mut duplex, unreachable);
        assert!(duplex.output.is_empty());
    }

    // serde_json ignores unknown values iteratively (no recursion limit, no stack use), and the C# reader skips
    // them the same way: a deeply nested unknown value is neither a crash of the serving app nor a rejection
    #[test]
    fn test_serve_request_skips_deeply_nested_unknown_values() {
        let nested = |depth: usize, open: &str, close: &str| open.repeat(depth) + "0" + &close.repeat(depth);
        for payload in [
            format!(r#"{{"version":1,"type":"ack","x":{}}}"#, nested(100_000, "[", "]")),
            format!(r#"{{"version":1,"type":"ack","x":{},"id":"7"}}"#, nested(100_000, r#"{"a":"#, "}")),
            format!(
                r#"{{"version":1,"type":"association","items":[{{"flag":"f","junk":{},"value":"v"}}]}}"#,
                nested(100_000, r#"[{"a":"#, "}]")
            ),
        ] {
            assert!(payload.len() <= MAX_MESSAGE_BYTES);
            let reply = serve(payload.as_bytes(), |m| IpcMessage::ack().reply_to(&m)).unwrap();
            assert!(reply.is_ack(), "{payload:.80}");
        }
        // the skipped value is still validated
        let bad = format!(r#"{{"version":1,"type":"ack","x":{}}}"#, "[1,]");
        let reply = serve(bad.as_bytes(), |_| panic!("handler must not run")).unwrap();
        assert_eq!(reply.message.as_deref(), Some(ERR_INVALID_MESSAGE));
    }

    fn random_endpoint() -> IpcEndpoint {
        IpcEndpoint::from_key(&format!("test-{}", crate::misc::random_string(12).to_ascii_lowercase())).unwrap()
    }

    fn connect_with_retries(ep: &IpcEndpoint) -> IpcConnection {
        let deadline = std::time::Instant::now() + Duration::from_secs(5);
        loop {
            match connect(ep).unwrap() {
                Some(c) => return c,
                None if std::time::Instant::now() < deadline => std::thread::sleep(Duration::from_millis(20)),
                None => panic!("no server at {:?}", ep.address),
            }
        }
    }

    #[test]
    fn test_server_client_round_trip() {
        let ep = random_endpoint();
        let received = Arc::new(Mutex::new(Vec::<String>::new()));
        let sink = received.clone();
        start_server(
            &ep,
            None,
            Arc::new(move |mut conn| {
                let sink = sink.clone();
                serve_request(&mut conn, move |msg| {
                    sink.lock().unwrap().extend(msg.items.iter().map(|i| i.value.clone()));
                    IpcMessage::ack().reply_to(&msg)
                })
            }),
        )
        .unwrap();

        for i in 0..300 {
            let conn = connect_with_retries(&ep);
            let reply = request(conn, &association(&format!("myapp://{i}")), EXCHANGE_TIMEOUT).unwrap();
            assert!(reply.is_ack(), "round {i}: {reply:?}");
        }
        let received = received.lock().unwrap();
        assert_eq!(received.len(), 300);
        assert_eq!(received[299], "myapp://299");
    }

    #[test]
    fn test_receive_times_out_when_peer_is_silent() {
        let ep = random_endpoint();
        // the server holds every connection open for a while without writing anything
        start_server(&ep, None, Arc::new(|_conn| std::thread::sleep(Duration::from_secs(3)))).unwrap();
        let mut conn = connect_with_retries(&ep);
        conn.set_timeout(Some(Duration::from_millis(300)));
        let started = std::time::Instant::now();
        match conn.receive() {
            Err(Error::Io(e)) => assert_eq!(e.kind(), io::ErrorKind::TimedOut, "{e:?}"),
            other => panic!("expected a timeout, got {other:?}"),
        }
        assert!(started.elapsed() < Duration::from_secs(2), "took {:?}", started.elapsed());
    }

    #[test]
    fn test_connect_without_server_is_none() {
        let ep = random_endpoint();
        assert!(connect(&ep).unwrap().is_none());
    }

    #[test]
    fn test_for_app_is_stable_and_creates_endpoint_twice() {
        let dir = tempfile::TempDir::new().unwrap();
        let a = IpcEndpoint::for_app("MyCoolApp", dir.path()).unwrap();
        let b = IpcEndpoint::for_app("MyCoolApp", dir.path()).unwrap();
        assert_eq!(a, b);
        assert_eq!(a.key.len(), 24);
        assert!(IpcEndpoint::for_app("", dir.path()).is_err());
        assert!(IpcEndpoint::for_app("MyCoolApp", Path::new("")).is_err());
        #[cfg(unix)]
        assert!(a.address.as_os_str().len() <= 103, "{:?}", a.address);
    }
}
