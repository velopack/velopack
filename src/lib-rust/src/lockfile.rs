use std::fs::File;
use std::io::Result;
use std::path::PathBuf;

/// A lock file that is used to prevent multiple instances of the application from running.
/// This lock file is automatically released and deleted when the `LockFile` is dropped.
#[allow(dead_code)]
pub struct LockFile {
    file_path: PathBuf,
    file: Option<File>,
    file_descriptor: Option<i32>,
}

impl LockFile {
    /// Creates a new `FileLock` with the given file path, retrying for about 2 seconds while another
    /// process holds the lock.
    pub fn try_acquire_lock<P: Into<PathBuf>>(path: P) -> Result<Self> {
        let path: PathBuf = path.into();
        crate::misc::retry_io(|| Self::try_acquire_lock_once(&path))
    }

    /// Creates a new `FileLock` with the given file path, failing at once if another process holds it.
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

    /// Acquires an exclusive, non-blocking lock on Unix-like systems.
    #[cfg(unix)]
    unsafe fn unix_exclusive_lock<P: Into<PathBuf>>(path: P) -> Result<i32> {
        use libc::{close, creat, lockf, open, EINTR, F_TLOCK, O_CLOEXEC, O_RDWR};
        use std::io::Error;
        use std::os::unix::ffi::OsStrExt;

        let path = path.into();
        let c_path = std::ffi::CString::new(path.as_os_str().as_bytes())?;

        // try to open existing file
        let mut fd;
        loop {
            fd = open(c_path.as_ptr(), O_RDWR | O_CLOEXEC);
            if fd != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) {
                break;
            }
        }

        // create it if that fails
        if fd == -1 {
            loop {
                fd = creat(c_path.as_ptr(), 0o666);
                if fd != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) {
                    break;
                }
            }
        }

        if fd == -1 {
            let err = std::io::Error::last_os_error();
            let _ = close(fd);
            return Err(std::io::Error::other(format!("Failed to open lock file: {}", err)));
        }

        let mut ret;
        loop {
            ret = lockf(fd, F_TLOCK, 0);
            if ret != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) {
                break;
            }
        }

        if ret == -1 {
            let err = std::io::Error::last_os_error();
            let _ = close(fd);
            Err(std::io::Error::other(format!("Failed to lock file: {}", err)))
        } else {
            Ok(fd)
        }
    }

    /// Acquires an exclusive, non-blocking lock on Windows systems.
    #[cfg(windows)]
    fn windows_exclusive_lock<P: Into<PathBuf>>(path: P) -> Result<File> {
        use std::fs::OpenOptions;
        use std::os::windows::fs::OpenOptionsExt;

        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .create(true)
            .truncate(false)
            // .custom_flags(0x04000000) // FILE_FLAG_DELETE_ON_CLOSE
            .share_mode(0)
            .open(path.into())?;

        Ok(file)
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
    use std::time::{Duration, Instant};

    // POSIX record locks belong to the process, so a second lock from the same process succeeds on unix.
    #[cfg(windows)]
    #[test]
    fn try_acquire_lock_once_fails_without_waiting() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join(".velopack_lock");
        let held = LockFile::try_acquire_lock_once(&path).unwrap();
        let started = Instant::now();
        assert!(LockFile::try_acquire_lock_once(&path).is_err());
        assert!(started.elapsed() < Duration::from_millis(300), "took {:?}", started.elapsed());
        drop(held);
        assert!(LockFile::try_acquire_lock_once(&path).is_ok());
    }
}
