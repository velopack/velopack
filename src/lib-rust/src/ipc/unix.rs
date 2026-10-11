//! Unix domain socket transport: `{tmp}/velopack-{uid}/{key}.sock`, in a private (0700) per-user directory with
//! the socket itself at 0600. The lock file lives in the same directory on purpose: macOS purges unused `$TMPDIR`
//! entries together, and a server whose files were purged is simply superseded by the next launch.

use super::IpcEndpoint;
use crate::Error;
use std::io::{self, Read, Write};
use std::os::unix::fs::{DirBuilderExt, MetadataExt, PermissionsExt};
use std::os::unix::net::{UnixListener, UnixStream};
use std::path::{Path, PathBuf};
use std::time::Duration;

/// `sun_path` holds 104 bytes on macOS including the terminator.
const MAX_SOCKET_PATH_BYTES: usize = 103;

pub(crate) fn endpoint_for_key(key: &str) -> Result<IpcEndpoint, Error> {
    let base = std::env::temp_dir();
    let base = super::trim_root_unix(&base.to_string_lossy());
    let dir = PathBuf::from(base).join(format!("velopack-{}", current_user()?));
    ensure_private_dir(&dir)?;
    let address = dir.join(format!("{key}.sock"));
    if address.as_os_str().len() > MAX_SOCKET_PATH_BYTES {
        return Err(Error::Other(format!(
            "IPC socket path is too long ({} bytes, max {MAX_SOCKET_PATH_BYTES})",
            address.as_os_str().len()
        )));
    }
    Ok(IpcEndpoint {
        key: key.to_owned(),
        address,
        lock_path: dir.join(format!("{key}.lock")),
    })
}

/// Creates `dir` with mode 0700 (an existing directory is fine: two simultaneous starts both create it) and
/// then verifies it is a real directory (not a symlink) owned by us, tightening its mode if needed.
pub(crate) fn ensure_private_dir(dir: &Path) -> io::Result<()> {
    match std::fs::DirBuilder::new().mode(0o700).create(dir) {
        Ok(()) => {}
        Err(e) if e.kind() == io::ErrorKind::AlreadyExists => {}
        Err(e) => return Err(e),
    }
    let meta = std::fs::symlink_metadata(dir)?;
    if meta.file_type().is_symlink() || !meta.is_dir() {
        return Err(io::Error::other(format!("{} is not a directory", dir.display())));
    }
    if meta.uid() != unsafe { libc::getuid() } {
        return Err(io::Error::other(format!("{} is owned by another user", dir.display())));
    }
    if meta.mode() & 0o077 != 0 {
        std::fs::set_permissions(dir, std::fs::Permissions::from_mode(0o700))?;
    }
    Ok(())
}

/// The decimal real user id.
pub(crate) fn current_user() -> Result<String, Error> {
    Ok(unsafe { libc::getuid() }.to_string())
}

pub(crate) struct Connection {
    stream: UnixStream,
}

impl Connection {
    pub(crate) fn set_timeout(&mut self, timeout: Option<Duration>) {
        let _ = self.stream.set_read_timeout(timeout);
        let _ = self.stream.set_write_timeout(timeout);
    }

    pub(crate) fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        self.stream.read(buf).map_err(timeout_error)
    }

    pub(crate) fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        self.stream.write(buf).map_err(timeout_error)
    }

    pub(crate) fn flush(&mut self) -> io::Result<()> {
        self.stream.flush()
    }
}

/// A socket timeout surfaces as `WouldBlock` on some platforms; callers only see `TimedOut`.
fn timeout_error(e: io::Error) -> io::Error {
    if e.kind() == io::ErrorKind::WouldBlock {
        io::Error::new(io::ErrorKind::TimedOut, "IPC operation timed out")
    } else {
        e
    }
}

pub(crate) struct Listener {
    listener: UnixListener,
}

impl Listener {
    pub(crate) fn accept(&mut self) -> io::Result<Connection> {
        let (stream, _) = self.listener.accept()?;
        Ok(Connection { stream })
    }
}

/// Unlinks a stale socket file (only the lock holder binds, so it cannot belong to a live server), binds, and
/// restricts the socket to the owner.
pub(crate) fn bind(ep: &IpcEndpoint) -> Result<Listener, Error> {
    match std::fs::remove_file(&ep.address) {
        Ok(()) => {}
        Err(e) if e.kind() == io::ErrorKind::NotFound => {}
        Err(e) => return Err(Error::Io(e)),
    }
    let listener = UnixListener::bind(&ep.address)?;
    std::fs::set_permissions(&ep.address, std::fs::Permissions::from_mode(0o600))?;
    Ok(Listener { listener })
}

/// `Ok(None)` when there is no socket file or nobody accepts on it (a stale file from a crashed server).
pub(crate) fn connect(ep: &IpcEndpoint) -> Result<Option<Connection>, Error> {
    match UnixStream::connect(&ep.address) {
        Ok(stream) => Ok(Some(Connection { stream })),
        Err(e) if matches!(e.kind(), io::ErrorKind::NotFound | io::ErrorKind::ConnectionRefused) => Ok(None),
        Err(e) => Err(Error::Io(e)),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_private_dir_is_created_and_checked() {
        let tmp = tempfile::TempDir::new().unwrap();
        let dir = tmp.path().join("private");
        ensure_private_dir(&dir).unwrap();
        assert_eq!(std::fs::metadata(&dir).unwrap().mode() & 0o777, 0o700);
        // an existing directory is fine, and a loose mode is tightened
        std::fs::set_permissions(&dir, std::fs::Permissions::from_mode(0o755)).unwrap();
        ensure_private_dir(&dir).unwrap();
        assert_eq!(std::fs::metadata(&dir).unwrap().mode() & 0o777, 0o700);

        // a symlink to a directory is rejected
        let target = tmp.path().join("target");
        std::fs::create_dir(&target).unwrap();
        let link = tmp.path().join("link");
        std::os::unix::fs::symlink(&target, &link).unwrap();
        assert!(ensure_private_dir(&link).is_err());
    }

    #[test]
    fn test_endpoint_locations() {
        let ep = IpcEndpoint::from_key("abc").unwrap();
        assert_eq!(ep.address.parent(), ep.lock_path.parent());
        assert!(ep
            .address
            .parent()
            .unwrap()
            .file_name()
            .unwrap()
            .to_string_lossy()
            .starts_with("velopack-"));
        assert_eq!(ep.address.file_name().unwrap(), "abc.sock");
        assert_eq!(ep.lock_path.file_name().unwrap(), "abc.lock");
        assert!(ep.address.as_os_str().len() <= MAX_SOCKET_PATH_BYTES);
    }
}
