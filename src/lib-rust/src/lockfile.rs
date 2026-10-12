use std::fs::File;
use std::io::Result;
use std::path::PathBuf;

/// An exclusive, inter-process lock on a file (a Windows byte-range lock / a POSIX `lockf` record lock). The lock
/// is released when the `LockFile` is dropped; the file itself is left in place. The C# library uses the same
/// kernel primitives, so a C# and a Rust process exclude each other.
///
/// POSIX record locks are per process: a second `LockFile` on the same path within one process succeeds, and
/// closing any descriptor of that file in the process releases the lock.
#[allow(dead_code)]
#[derive(Debug)]
pub struct LockFile {
    file_path: PathBuf,
    file: Option<File>,
    file_descriptor: Option<i32>,
}

impl LockFile {
    /// Acquires the lock, retrying a few times (about two seconds in total) while it is held by someone else.
    pub fn try_acquire_lock<P: Into<PathBuf>>(path: P) -> Result<Self> {
        let path: PathBuf = path.into();
        crate::misc::retry_io(|| Self::try_acquire_lock_once(&path))
    }

    /// Makes a single attempt to acquire the lock. When it is held elsewhere the error satisfies
    /// [`is_lock_contention`]; any other error means the lock file could not be opened or locked at all.
    pub fn try_acquire_lock_once<P: Into<PathBuf>>(path: P) -> Result<Self> {
        let path: PathBuf = path.into();

        #[cfg(windows)]
        {
            let file = Self::windows_exclusive_lock(&path)?;
            Ok(LockFile {
                file_path: path,
                file: Some(file),
                file_descriptor: None,
            })
        }

        #[cfg(unix)]
        {
            let fd = unsafe { Self::unix_exclusive_lock(&path)? };
            Ok(LockFile {
                file_path: path,
                file: None,
                file_descriptor: Some(fd),
            })
        }
    }

    /// Releases the lock and closes the file.
    fn dispose(&mut self) {
        let _ = self.file.take(); // dispose file handle
        #[cfg(unix)]
        {
            if let Some(fd) = self.file_descriptor.take() {
                unsafe {
                    libc::close(fd);
                }
            }
        }
    }

    /// Opens (creating if needed, never truncating) the lock file with `O_CLOEXEC`, so the lock does not leak into
    /// child processes, and takes a non-blocking `lockf` lock on it. Contention (`lockf` failing with `EAGAIN` or
    /// `EACCES`) is reported as [`ErrorKind::WouldBlock`]; an `open` failure keeps its own kind.
    #[cfg(unix)]
    unsafe fn unix_exclusive_lock(path: &std::path::Path) -> Result<i32> {
        use libc::{close, lockf, open, EACCES, EAGAIN, EINTR, F_TLOCK, O_CLOEXEC, O_CREAT, O_RDWR};
        use std::io::{Error, ErrorKind};
        use std::os::unix::ffi::OsStrExt;

        let c_path = std::ffi::CString::new(path.as_os_str().as_bytes())?;

        let mut fd;
        loop {
            fd = open(c_path.as_ptr(), O_RDWR | O_CREAT | O_CLOEXEC, 0o666);
            if fd != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) {
                break;
            }
        }
        if fd == -1 {
            return Err(Error::last_os_error());
        }

        let mut ret;
        loop {
            ret = lockf(fd, F_TLOCK, 0);
            if ret != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) {
                break;
            }
        }
        if ret == -1 {
            let err = Error::last_os_error();
            let _ = close(fd);
            // EAGAIN and EWOULDBLOCK share a value on Linux and macOS, hence no `matches!` or-pattern
            return Err(match err.raw_os_error() {
                Some(code) if code == EAGAIN || code == EACCES => Error::new(ErrorKind::WouldBlock, err),
                _ => err,
            });
        }
        Ok(fd)
    }

    /// Opens (creating if needed, never truncating) the lock file with full sharing, so that transient opens by
    /// other software (antivirus, indexers, backup agents) are never mistaken for contention, and takes an
    /// exclusive byte-range lock on byte 0. Contention is reported as `ERROR_LOCK_VIOLATION`.
    #[cfg(windows)]
    fn windows_exclusive_lock(path: &std::path::Path) -> Result<File> {
        use std::fs::OpenOptions;
        use std::os::windows::fs::OpenOptionsExt;
        use std::os::windows::io::AsRawHandle;
        use windows::Win32::Foundation::HANDLE;
        use windows::Win32::Storage::FileSystem::{
            LockFileEx, FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, LOCKFILE_EXCLUSIVE_LOCK, LOCKFILE_FAIL_IMMEDIATELY,
        };
        use windows::Win32::System::IO::OVERLAPPED;

        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .create(true)
            .truncate(false)
            .share_mode((FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE).0)
            .open(path)?;

        let mut overlapped = OVERLAPPED::default();
        unsafe {
            LockFileEx(
                HANDLE(file.as_raw_handle()),
                LOCKFILE_EXCLUSIVE_LOCK | LOCKFILE_FAIL_IMMEDIATELY,
                None,
                1,
                0,
                &mut overlapped,
            )
            .map_err(crate::misc::io_error_from_win32)?;
        }
        Ok(file)
    }
}

/// Whether a [`LockFile::try_acquire_lock_once`] error means the lock is currently held by another holder (as
/// opposed to the file being inaccessible).
pub fn is_lock_contention(e: &std::io::Error) -> bool {
    #[cfg(windows)]
    {
        e.raw_os_error() == Some(33) // ERROR_LOCK_VIOLATION
    }
    #[cfg(unix)]
    {
        e.kind() == std::io::ErrorKind::WouldBlock
    }
}

impl Drop for LockFile {
    fn drop(&mut self) {
        self.dispose();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_lock_once_acquires_and_releases() {
        let dir = tempfile::TempDir::new().unwrap();
        let path = dir.path().join("test.lock");
        let lock = LockFile::try_acquire_lock_once(&path).unwrap();
        assert!(path.exists());
        drop(lock);
        // re-acquirable once released, and the file is kept
        let _again = LockFile::try_acquire_lock_once(&path).unwrap();
        assert!(path.exists());
    }

    // POSIX record locks are per process, so contention can only be observed on Windows in-process.
    #[cfg(windows)]
    #[test]
    fn test_lock_once_contention_and_shared_open() {
        let dir = tempfile::TempDir::new().unwrap();
        let path = dir.path().join("test.lock");
        let _holder = LockFile::try_acquire_lock_once(&path).unwrap();

        let err = LockFile::try_acquire_lock_once(&path).unwrap_err();
        assert!(is_lock_contention(&err), "{err:?}");

        // a plain open (eg. an indexer) neither fails nor disturbs the holder
        let other = std::fs::OpenOptions::new().read(true).write(true).open(&path).unwrap();
        drop(other);
        let err = LockFile::try_acquire_lock_once(&path).unwrap_err();
        assert!(is_lock_contention(&err), "{err:?}");

        let not_contention = std::io::Error::from_raw_os_error(32);
        assert!(!is_lock_contention(&not_contention));
    }

    // an unwritable lock file is a hard failure, not contention (which would make association launches wait for
    // a server that does not exist)
    #[cfg(unix)]
    #[test]
    fn test_lock_once_unwritable_file_is_not_contention() {
        use std::os::unix::fs::PermissionsExt;
        if unsafe { libc::geteuid() } == 0 {
            return; // root opens anything
        }
        let dir = tempfile::TempDir::new().unwrap();
        let path = dir.path().join("test.lock");
        std::fs::write(&path, b"").unwrap();
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o000)).unwrap();

        let err = LockFile::try_acquire_lock_once(&path).unwrap_err();
        assert!(!is_lock_contention(&err), "{err:?}");
        assert_eq!(err.raw_os_error(), Some(libc::EACCES));
    }
}
