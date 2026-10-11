//! Named-pipe transport (`\\.\pipe\velopack-{key}`), restricted to the current user and integrity level.
//!
//! Both ends are opened with `FILE_FLAG_OVERLAPPED` so every operation can be bounded by a timeout: an operation
//! is started with a manual-reset event in its `OVERLAPPED`, waited for with `WaitForSingleObject`, and cancelled
//! with `CancelIoEx` when the wait times out. All APIs are Vista+ and load on Windows 7.

use super::IpcEndpoint;
use crate::{misc::io_error_from_win32, wide_strings::string_to_wide, Error};
use std::io;
use std::path::PathBuf;
use std::time::Duration;
use windows::core::{PCWSTR, PWSTR};
use windows::Win32::Foundation::{
    CloseHandle, LocalFree, GENERIC_READ, GENERIC_WRITE, HANDLE, HLOCAL, INVALID_HANDLE_VALUE, WAIT_OBJECT_0, WAIT_TIMEOUT,
};
use windows::Win32::Security::Authorization::{ConvertSidToStringSidW, ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1};
use windows::Win32::Security::{
    GetTokenInformation, TokenIntegrityLevel, TokenUser, PSECURITY_DESCRIPTOR, SECURITY_ATTRIBUTES, TOKEN_INFORMATION_CLASS, TOKEN_MANDATORY_LABEL,
    TOKEN_QUERY, TOKEN_USER,
};
use windows::Win32::Storage::FileSystem::{
    CreateFileW, FlushFileBuffers, ReadFile, WriteFile, FILE_FLAG_FIRST_PIPE_INSTANCE, FILE_FLAG_OVERLAPPED, FILE_SHARE_MODE, OPEN_EXISTING,
    PIPE_ACCESS_DUPLEX, SECURITY_IDENTIFICATION, SECURITY_SQOS_PRESENT,
};
use windows::Win32::System::Pipes::{
    ConnectNamedPipe, CreateNamedPipeW, GetNamedPipeServerProcessId, WaitNamedPipeW, PIPE_READMODE_BYTE, PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE,
    PIPE_UNLIMITED_INSTANCES, PIPE_WAIT,
};
use windows::Win32::System::Threading::{
    CreateEventW, GetCurrentProcess, OpenProcess, OpenProcessToken, WaitForSingleObject, INFINITE, PROCESS_QUERY_LIMITED_INFORMATION,
};
use windows::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};

const ERROR_FILE_NOT_FOUND: u32 = 2;
const ERROR_ACCESS_DENIED: u32 = 5;
const ERROR_HANDLE_EOF: u32 = 38;
const ERROR_BROKEN_PIPE: u32 = 109;
const ERROR_INSUFFICIENT_BUFFER: u32 = 122;
const ERROR_PIPE_BUSY: u32 = 231;
const ERROR_PIPE_NOT_CONNECTED: u32 = 233;
const ERROR_PIPE_CONNECTED: u32 = 535;
const ERROR_OPERATION_ABORTED: u32 = 995;
const ERROR_IO_PENDING: u32 = 997;
const PIPE_BUFFER_SIZE: u32 = 65536;

pub(crate) fn endpoint_for_key(key: &str) -> Result<IpcEndpoint, Error> {
    let lock_dir = crate::known_path::get_local_app_data()
        .map_err(|e| Error::Other(format!("LocalAppData unavailable: {e}")))?
        .join("velopack")
        .join("ipc");
    std::fs::create_dir_all(&lock_dir)?;
    Ok(IpcEndpoint {
        key: key.to_owned(),
        address: PathBuf::from(format!("\\\\.\\pipe\\velopack-{key}")),
        lock_path: lock_dir.join(format!("{key}.lock")),
    })
}

/// The string SID of the current process token's user, eg. `S-1-5-21-...`.
pub(crate) fn current_user() -> Result<String, Error> {
    let (user, _) = token_sids(unsafe { GetCurrentProcess() })?;
    Ok(user)
}

fn win32_code(e: &windows::core::Error) -> u32 {
    crate::misc::win32_error_code(e)
}

fn sid_to_string(sid: windows::Win32::Security::PSID) -> io::Result<String> {
    let mut text = PWSTR::null();
    unsafe {
        ConvertSidToStringSidW(sid, &mut text).map_err(io_error_from_win32)?;
        let result = text.to_string().map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e));
        let _ = LocalFree(Some(HLOCAL(text.0.cast())));
        result
    }
}

fn token_information(token: HANDLE, class: TOKEN_INFORMATION_CLASS) -> io::Result<Vec<u8>> {
    let mut size = 0u32;
    unsafe {
        match GetTokenInformation(token, class, None, 0, &mut size) {
            Err(e) if win32_code(&e) == ERROR_INSUFFICIENT_BUFFER && size > 0 => {}
            Err(e) => return Err(io_error_from_win32(e)),
            Ok(()) => return Err(io::Error::other("GetTokenInformation returned no data")),
        }
        let mut buf = vec![0u8; size as usize];
        GetTokenInformation(token, class, Some(buf.as_mut_ptr().cast()), size, &mut size).map_err(io_error_from_win32)?;
        Ok(buf)
    }
}

/// The user SID and the integrity-level SID (eg. `S-1-16-8192` medium, `S-1-16-12288` high) of `process`'s token.
pub(crate) fn token_sids(process: HANDLE) -> io::Result<(String, String)> {
    let mut token = HANDLE::default();
    unsafe { OpenProcessToken(process, TOKEN_QUERY, &mut token).map_err(io_error_from_win32)? };
    let result = (|| {
        let user = token_information(token, TokenUser)?;
        // SAFETY: the buffer holds a TOKEN_USER whose Sid points inside the same buffer
        let user_sid = unsafe { (*user.as_ptr().cast::<TOKEN_USER>()).User.Sid };
        let user = sid_to_string(user_sid)?;
        let label = token_information(token, TokenIntegrityLevel)?;
        let label_sid = unsafe { (*label.as_ptr().cast::<TOKEN_MANDATORY_LABEL>()).Label.Sid };
        let level = sid_to_string(label_sid)?;
        Ok((user, level))
    })();
    unsafe {
        let _ = CloseHandle(token);
    }
    result
}

/// A pipe handle plus the event used for its overlapped operations.
pub(crate) struct Connection {
    handle: HANDLE,
    event: HANDLE,
    timeout: Option<Duration>,
}

// SAFETY: the handles are process-wide kernel handles, usable from any thread.
unsafe impl Send for Connection {}

impl Connection {
    fn new(handle: HANDLE) -> io::Result<Connection> {
        let event = unsafe { CreateEventW(None, true, false, PCWSTR::null()) }.map_err(|e| {
            unsafe {
                let _ = CloseHandle(handle);
            }
            io_error_from_win32(e)
        })?;
        Ok(Connection {
            handle,
            event,
            timeout: None,
        })
    }

    pub(crate) fn set_timeout(&mut self, timeout: Option<Duration>) {
        self.timeout = timeout;
    }

    /// Runs one overlapped operation to completion, bounded by `timeout`: `start` issues it with the prepared
    /// `OVERLAPPED`; the result is the number of bytes transferred. A broken/disconnected pipe reads as EOF (0),
    /// `ERROR_PIPE_CONNECTED` from `ConnectNamedPipe` counts as success, and a timed-out operation is cancelled
    /// (and awaited) before `ErrorKind::TimedOut` is returned.
    fn overlapped(&self, timeout: Option<Duration>, start: impl FnOnce(*mut OVERLAPPED) -> windows::core::Result<()>) -> io::Result<u32> {
        let mut ov = OVERLAPPED {
            hEvent: self.event,
            ..Default::default()
        };
        match start(&mut ov) {
            Ok(()) => {}
            Err(e) => match win32_code(&e) {
                ERROR_IO_PENDING => {}
                ERROR_PIPE_CONNECTED => return Ok(0),
                ERROR_BROKEN_PIPE | ERROR_PIPE_NOT_CONNECTED | ERROR_HANDLE_EOF => return Ok(0),
                _ => return Err(io_error_from_win32(e)),
            },
        }

        let ms = timeout.map(|t| t.as_millis().min(u32::MAX as u128 - 1) as u32).unwrap_or(INFINITE);
        let mut transferred = 0u32;
        unsafe {
            match WaitForSingleObject(self.event, ms) {
                WAIT_OBJECT_0 => {}
                WAIT_TIMEOUT => {
                    let _ = CancelIoEx(self.handle, Some(&ov as *const OVERLAPPED));
                    return match GetOverlappedResult(self.handle, &ov, &mut transferred, true) {
                        Ok(()) => Ok(transferred), // completed just before the cancel
                        Err(e) if win32_code(&e) == ERROR_OPERATION_ABORTED => {
                            Err(io::Error::new(io::ErrorKind::TimedOut, "IPC operation timed out"))
                        }
                        Err(e) => Err(io_error_from_win32(e)),
                    };
                }
                _ => return Err(io::Error::last_os_error()),
            }
            match GetOverlappedResult(self.handle, &ov, &mut transferred, false) {
                Ok(()) => Ok(transferred),
                Err(e) => match win32_code(&e) {
                    ERROR_PIPE_CONNECTED => Ok(0),
                    ERROR_BROKEN_PIPE | ERROR_PIPE_NOT_CONNECTED | ERROR_HANDLE_EOF => Ok(0),
                    _ => Err(io_error_from_win32(e)),
                },
            }
        }
    }

    pub(crate) fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }
        let handle = self.handle;
        let n = self.overlapped(self.timeout, |ov| unsafe { ReadFile(handle, Some(buf), None, Some(ov)) })?;
        Ok(n as usize)
    }

    pub(crate) fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }
        let handle = self.handle;
        let n = self.overlapped(self.timeout, |ov| unsafe { WriteFile(handle, Some(buf), None, Some(ov)) })?;
        if n == 0 {
            return Err(io::Error::new(io::ErrorKind::BrokenPipe, "pipe closed"));
        }
        Ok(n as usize)
    }

    /// Blocks until the peer has read everything written so far (or the pipe breaks). A server calls this before
    /// closing, so the client never loses the reply; `DisconnectNamedPipe` is never used for that reason.
    pub(crate) fn flush(&mut self) -> io::Result<()> {
        unsafe { FlushFileBuffers(self.handle) }.map_err(io_error_from_win32)
    }
}

impl Drop for Connection {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.handle);
            let _ = CloseHandle(self.event);
        }
    }
}

/// A security descriptor built from SDDL, kept alive while pipe instances are created with it.
pub(crate) struct SecurityDescriptor {
    descriptor: PSECURITY_DESCRIPTOR,
}

impl SecurityDescriptor {
    pub(crate) fn from_sddl(sddl: &str) -> io::Result<SecurityDescriptor> {
        let wide = string_to_wide(sddl);
        let mut descriptor = PSECURITY_DESCRIPTOR::default();
        unsafe {
            ConvertStringSecurityDescriptorToSecurityDescriptorW(wide.as_pcwstr(), SDDL_REVISION_1, &mut descriptor, None)
                .map_err(io_error_from_win32)?;
        }
        Ok(SecurityDescriptor { descriptor })
    }

    fn attributes(&self) -> SECURITY_ATTRIBUTES {
        SECURITY_ATTRIBUTES {
            nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: self.descriptor.0,
            bInheritHandle: false.into(),
        }
    }
}

impl Drop for SecurityDescriptor {
    fn drop(&mut self) {
        unsafe {
            let _ = LocalFree(Some(HLOCAL(self.descriptor.0)));
        }
    }
}

/// Creates one instance of the named pipe `name` (duplex, byte mode, overlapped, local clients only).
pub(crate) fn create_pipe_instance(name: &str, security: &SecurityDescriptor, first: bool) -> io::Result<HANDLE> {
    let wide = string_to_wide(name);
    let mut open_mode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED;
    if first {
        open_mode |= FILE_FLAG_FIRST_PIPE_INSTANCE;
    }
    let sa = security.attributes();
    let handle = unsafe {
        CreateNamedPipeW(
            wide.as_pcwstr(),
            open_mode,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            PIPE_UNLIMITED_INSTANCES,
            PIPE_BUFFER_SIZE,
            PIPE_BUFFER_SIZE,
            0,
            Some(&sa as *const SECURITY_ATTRIBUTES),
        )
    };
    if handle == INVALID_HANDLE_VALUE {
        return Err(io_error_from_win32(windows::core::Error::from_thread()));
    }
    Ok(handle)
}

/// The server side: owns the security descriptor and always keeps one unconnected instance waiting, so the pipe
/// name never disappears between connections.
pub(crate) struct Listener {
    name: String,
    security: SecurityDescriptor,
    waiting: Option<Connection>,
}

// SAFETY: see Connection; the descriptor is a process-local heap allocation.
unsafe impl Send for Listener {}

impl Listener {
    pub(crate) fn accept(&mut self) -> io::Result<Connection> {
        loop {
            let current = match self.waiting.take() {
                Some(c) => c,
                None => Connection::new(create_pipe_instance(&self.name, &self.security, false)?)?,
            };
            let handle = current.handle;
            if let Err(e) = current.overlapped(None, |ov| unsafe { ConnectNamedPipe(handle, Some(ov)) }) {
                // eg. ERROR_NO_DATA: the client connected and went away again; this instance is spent
                debug!("IPC: ConnectNamedPipe failed, recreating the instance: {e}");
                drop(current);
                continue;
            }
            // the next instance exists before this one is handed out, so the name stays reachable
            match create_pipe_instance(&self.name, &self.security, false).and_then(Connection::new) {
                Ok(next) => self.waiting = Some(next),
                Err(e) => warn!("IPC: could not create the next pipe instance: {e}"),
            }
            return Ok(current);
        }
    }
}

/// Creates the first pipe instance (failing if the name is already taken), with a DACL granting access only to
/// the current user and a mandatory label at the current integrity level (named pipes carry no label by default,
/// which would let a medium-integrity process push activations into an elevated instance).
pub(crate) fn bind(ep: &IpcEndpoint) -> Result<Listener, Error> {
    let (user, level) = token_sids(unsafe { GetCurrentProcess() })?;
    let security = SecurityDescriptor::from_sddl(&format!("D:P(A;;GA;;;{user})S:(ML;;NW;;;{level})"))?;
    let name = ep.address.to_string_lossy().into_owned();
    let first = Connection::new(create_pipe_instance(&name, &security, true)?)?;
    Ok(Listener {
        name,
        security,
        waiting: Some(first),
    })
}

/// Opens the pipe as a client. `Ok(None)` when no instance exists or all are busy (the caller retries), `Err` when
/// access is denied or the server process cannot be verified to run as the same user and integrity level.
pub(crate) fn connect(ep: &IpcEndpoint) -> Result<Option<Connection>, Error> {
    let name = string_to_wide(ep.address.to_string_lossy().into_owned());
    let handle = unsafe {
        CreateFileW(
            name.as_pcwstr(),
            GENERIC_READ.0 | GENERIC_WRITE.0,
            FILE_SHARE_MODE(0),
            None,
            OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION,
            None,
        )
    };
    let handle = match handle {
        Ok(h) => h,
        Err(e) => {
            return match win32_code(&e) {
                ERROR_FILE_NOT_FOUND => Ok(None),
                ERROR_PIPE_BUSY => {
                    unsafe {
                        let _ = WaitNamedPipeW(name.as_pcwstr(), 100);
                    }
                    Ok(None)
                }
                ERROR_ACCESS_DENIED => Err(Error::Other("IPC server access denied (another user or integrity level)".into())),
                _ => Err(Error::Io(io_error_from_win32(e))),
            };
        }
    };
    let conn = Connection::new(handle)?;
    verify_server(&conn).map_err(|e| Error::Other(format!("IPC server could not be verified: {e}")))?;
    Ok(Some(conn))
}

/// Before anything is written, the server process must run under our user SID and integrity level. Any failure
/// along the way (a squatter from another user typically makes `OpenProcess` fail) is a mismatch.
fn verify_server(conn: &Connection) -> io::Result<()> {
    let mut pid = 0u32;
    unsafe { GetNamedPipeServerProcessId(conn.handle, &mut pid).map_err(io_error_from_win32)? };
    let process = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) }.map_err(io_error_from_win32)?;
    let server = token_sids(process);
    unsafe {
        let _ = CloseHandle(process);
    }
    let server = server?;
    let own = token_sids(unsafe { GetCurrentProcess() })?;
    if server != own {
        return Err(io::Error::new(io::ErrorKind::PermissionDenied, "server user or integrity level differs"));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_token_sids_of_current_process() {
        let (user, level) = token_sids(unsafe { GetCurrentProcess() }).unwrap();
        assert!(user.starts_with("S-1-5-"), "{user}");
        assert!(level.starts_with("S-1-16-"), "{level}");
        assert_eq!(current_user().unwrap(), user);
    }

    #[test]
    fn test_connect_refuses_squatted_pipe() {
        // a pipe only SYSTEM may open: our own CreateFileW fails with access denied, which must be an error
        // (never "nobody listening", which would make the caller retry and eventually handle in-process)
        let ep = IpcEndpoint::from_key(&format!("squat-{}", crate::misc::random_string(12).to_ascii_lowercase())).unwrap();
        let security = SecurityDescriptor::from_sddl("D:P(A;;GA;;;SY)").unwrap();
        let squatter = create_pipe_instance(&ep.address.to_string_lossy(), &security, true).unwrap();
        let result = connect(&ep);
        unsafe {
            let _ = CloseHandle(squatter);
        }
        match result {
            Err(Error::Other(msg)) => assert!(msg.contains("access denied"), "{msg}"),
            other => panic!("expected an access-denied error, got {:?}", other.map(|c| c.is_some())),
        }
    }
}
