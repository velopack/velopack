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
    /// Creates a new `FileLock` with the given file path.
    pub fn try_acquire_lock<P: Into<PathBuf>>(path: P) -> Result<Self> {
        let path: PathBuf = path.into();

        crate::util::retry_io(|| {
            #[cfg(windows)]
            {
                let file = Self::windows_exclusive_lock(&path)?;
                let lock = LockFile {
                    file_path: path.clone(),
                    file: Some(file),
                    file_descriptor: None,
                };
                Ok(lock)
            }

            #[cfg(unix)]
            {
                let fd = unsafe { Self::unix_exclusive_lock(&path)? };
                let lock = LockFile {
                    file_path: path.clone(),
                    file: None,
                    file_descriptor: Some(fd),
                };
                Ok(lock)
            }
        })
    }

    /// Releases the lock and closes the file.
    fn dispose(&mut self) {
        let _ = self.file.take(); // dispose file handle
        #[cfg(unix)]
        {
            if let Some(fd) = self.file_descriptor.take() {
                unsafe { libc::close(fd); }
            }
        }
    }

    /// Acquires an exclusive, non-blocking lock on Unix-like systems.
    #[cfg(unix)]
    unsafe fn unix_exclusive_lock<P: Into<PathBuf>>(path: P) -> Result<i32> {
        use std::os::unix::ffi::OsStrExt;
        use libc::{open, creat, lockf, close, F_TLOCK, O_RDWR, O_CLOEXEC, EINTR};
        use std::io::{Error, ErrorKind};

        let path = path.into();
        let c_path = std::ffi::CString::new(path.as_os_str().as_bytes())?;

        // try to open existing file
        let mut fd;
        loop { 
            fd = open(c_path.as_ptr(), O_RDWR | O_CLOEXEC);
            if fd != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) { break; }
        }

        // create it if that fails
        if fd == -1 {
            loop { 
                fd = creat(c_path.as_ptr(), 0o666);
                if fd != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) { break; }
            }
        }

        if fd == -1 {
            let err = std::io::Error::last_os_error();
            let _ = close(fd);
            return Err(std::io::Error::new(
                ErrorKind::Other,
                format!("Failed to open lock file: {}", err),
            ))
        }

        let mut ret;
        loop { 
            ret = lockf(fd, F_TLOCK, 0);
            if ret != -1 || Error::last_os_error().raw_os_error() != Some(EINTR) { break; }
        }

        if ret == -1 {
            let err = std::io::Error::last_os_error();
            let _ = close(fd);
            Err(std::io::Error::new(
                ErrorKind::Other,
                format!("Failed to lock file: {}", err),
            ))
        } else {
            Ok(fd)
        }
    }

    /// Acquires an exclusive, non-blocking lock on Windows systems.
    #[cfg(windows)]
    fn windows_exclusive_lock<P: Into<PathBuf>>(path: P) -> Result<File> {
        use std::os::windows::fs::OpenOptionsExt;
        use std::fs::OpenOptions;

        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .create(true)
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