//! Unix domain socket location: `{tmp}/velopack-{uid}/{key}.sock`, in a private (0700) per-user directory with
//! the socket itself at 0600. The lock file lives in the same directory on purpose: macOS purges unused `$TMPDIR`
//! entries together, and a server whose files were purged is simply superseded by the next launch.

use super::{IpcConnection, IpcEndpoint};
use crate::Error;
use std::ffi::OsString;
use std::io;
use std::os::unix::fs::{DirBuilderExt, MetadataExt, PermissionsExt};
use std::path::{Path, PathBuf};

/// `sun_path` holds 104 bytes on macOS including the terminator.
const MAX_SOCKET_PATH_BYTES: usize = 103;

pub(crate) fn endpoint_for_key(key: &str) -> Result<IpcEndpoint, Error> {
    let base = socket_base_dir(std::env::var_os("TMPDIR"));
    let dir = base.join(format!("velopack-{}", current_user()?));
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

/// The directory the per-user socket directory lives in, by the same rule as the C# library
/// (`IpcEndpoint.SocketBaseDir`), so both find each other whatever the environment of either process: on macOS always
/// the per-user temporary directory (`confstr(_CS_DARWIN_USER_TEMP_DIR)`, `$TMPDIR` is ignored as it may be unset,
/// eg. over ssh or in a launchd job), `/tmp` if that fails; elsewhere `tmpdir` (the value of `$TMPDIR`) when it is
/// set and not empty, else `/tmp`. Trailing slashes are trimmed.
pub(crate) fn socket_base_dir(tmpdir: Option<OsString>) -> PathBuf {
    #[cfg(target_os = "macos")]
    let base = {
        let _ = tmpdir;
        darwin_user_temp_dir().unwrap_or_else(|| "/tmp".into())
    };
    #[cfg(not(target_os = "macos"))]
    let base = match tmpdir {
        Some(dir) if !dir.is_empty() => dir.to_string_lossy().into_owned(),
        _ => "/tmp".to_owned(),
    };
    PathBuf::from(super::trim_root_unix(&base))
}

#[cfg(target_os = "macos")]
fn darwin_user_temp_dir() -> Option<String> {
    let mut buf = vec![0u8; 1024];
    // SAFETY: confstr writes at most buf.len() bytes (NUL-terminated) and returns the size the full value needs
    let needed = unsafe { libc::confstr(libc::_CS_DARWIN_USER_TEMP_DIR, buf.as_mut_ptr().cast(), buf.len()) };
    if needed == 0 || needed > buf.len() {
        return None;
    }
    buf.truncate(needed - 1);
    String::from_utf8(buf).ok().filter(|dir| !dir.is_empty())
}

/// Makes writes on `conn` to a peer that already went away fail with `EPIPE` instead of raising `SIGPIPE`, which
/// would kill a host process that does not ignore it (a C/C++ app; Rust, .NET, Node and Python ignore it). On Apple
/// targets the socket gets `SO_NOSIGPIPE`; elsewhere every thread doing IPC I/O calls [`block_sigpipe`] instead.
pub(crate) fn suppress_sigpipe(conn: &IpcConnection) -> io::Result<()> {
    #[cfg(target_vendor = "apple")]
    {
        use std::os::fd::{AsFd, AsRawFd};
        let IpcConnection::UdSocket(stream) = conn;
        let on: libc::c_int = 1;
        // SAFETY: a valid socket fd and a c_int option value of the given size
        let rc = unsafe {
            libc::setsockopt(
                stream.as_fd().as_raw_fd(),
                libc::SOL_SOCKET,
                libc::SO_NOSIGPIPE,
                (&on as *const libc::c_int).cast(),
                std::mem::size_of::<libc::c_int>() as libc::socklen_t,
            )
        };
        if rc != 0 {
            return Err(io::Error::last_os_error());
        }
    }
    #[cfg(not(target_vendor = "apple"))]
    let _ = conn;
    Ok(())
}

/// Blocks `SIGPIPE` on the calling thread (a no-op on Apple targets, see [`suppress_sigpipe`]). A write-induced
/// `SIGPIPE` is directed at the writing thread, so it stays pending there, the write fails with `EPIPE`, and the
/// signal is discarded when the thread exits. Only called on threads the IPC layer owns.
pub(crate) fn block_sigpipe() {
    #[cfg(not(target_vendor = "apple"))]
    // SAFETY: plain signal-set manipulation on a local set; only the calling thread's mask changes
    unsafe {
        let mut set: libc::sigset_t = std::mem::zeroed();
        libc::sigemptyset(&mut set);
        libc::sigaddset(&mut set, libc::SIGPIPE);
        libc::pthread_sigmask(libc::SIG_BLOCK, &set, std::ptr::null_mut());
    }
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
    fn test_socket_base_dir() {
        #[cfg(target_os = "macos")]
        {
            // always the per-user temporary directory, whatever TMPDIR says
            let base = socket_base_dir(None);
            assert!(base.to_string_lossy().contains("/var/folders/"), "{base:?}");
            assert_eq!(socket_base_dir(Some("/elsewhere/".into())), base);
            assert_eq!(socket_base_dir(Some("".into())), base);
        }
        #[cfg(not(target_os = "macos"))]
        {
            assert_eq!(socket_base_dir(None), PathBuf::from("/tmp"));
            assert_eq!(socket_base_dir(Some("".into())), PathBuf::from("/tmp"));
            assert_eq!(socket_base_dir(Some("/run/user/1000/tmp/".into())), PathBuf::from("/run/user/1000/tmp"));
        }
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
