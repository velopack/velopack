use std::fs::{File, OpenOptions};
use std::io::{Error, ErrorKind, Result};
use std::path::PathBuf;

/// A lock file that is used to prevent multiple instances of the application from running.
/// This lock file is automatically released and deleted when the `LockFile` is dropped.
pub struct LockFile {
    file_path: PathBuf,
    file: Option<File>,
}

impl LockFile {
    /// Creates a new `FileLock` with the given file path.
    pub fn try_acquire_lock<P: Into<PathBuf>>(path: P) -> Result<Self> {
        let path: PathBuf = path.into();
        crate::util::retry_io(|| {
            let mut lock_file = LockFile {
                file_path: path.clone(),
                file: None,
            };

            let mut options = OpenOptions::new();
            options.read(true).write(true).create(true).truncate(true);

            #[cfg(windows)]
            {
                use std::os::windows::fs::OpenOptionsExt;
                options.custom_flags(0x04000000); // FILE_FLAG_DELETE_ON_CLOSE
                options.share_mode(0);
            }

            let file = options.open(&path)?;

            #[cfg(any(target_os = "linux", target_os = "macos", target_os = "android"))]
            {
                use std::os::unix::io::AsRawFd;
                let fd = file.as_raw_fd();
                lock_file.unix_exclusive_lock(fd)?;
            }

            #[cfg(target_os = "windows")]
            {
                use std::os::windows::io::AsRawHandle;
                let handle = file.as_raw_handle();
                lock_file.windows_exclusive_lock(handle)?;
            }
            lock_file.file = Some(file);
            Ok(lock_file)
        })
    }

    /// Releases the lock and closes the file.
    fn dispose(&mut self) {
        {
            let _ = self.file.take();
        }
        let _ = std::fs::remove_file(&self.file_path);
    }

    /// Acquires an exclusive, non-blocking lock on Unix-like systems.
    #[cfg(unix)]
    fn unix_exclusive_lock(&self, fd: std::os::unix::io::RawFd) -> Result<()> {
        use libc::{fcntl, F_SETLK, F_WRLCK, SEEK_SET};

        let lock = libc::flock {
            l_type: F_WRLCK as libc::c_short,
            l_whence: SEEK_SET as libc::c_short,
            l_start: 0,
            l_len: 0, // 0 means to lock the entire file
            l_pid: 0,
        };

        let ret = unsafe { fcntl(fd, F_SETLK, &lock) };

        if ret == -1 {
            let err = Error::last_os_error();
            Err(Error::new(
                ErrorKind::Other,
                format!("Failed to lock file: {}", err),
            ))
        } else {
            Ok(())
        }
    }

    /// Acquires an exclusive, non-blocking lock on Windows systems.
    #[cfg(windows)]
    fn windows_exclusive_lock(&self, handle: std::os::windows::io::RawHandle) -> Result<()> {
        use windows::Win32::Foundation::HANDLE;
        use windows::Win32::Storage::FileSystem::{LockFileEx, LOCKFILE_EXCLUSIVE_LOCK, LOCKFILE_FAIL_IMMEDIATELY};
        use windows::Win32::System::IO::OVERLAPPED;

        let mut overlapped = OVERLAPPED::default();

        let res = unsafe {
            LockFileEx(
                HANDLE(handle.into()),
                LOCKFILE_EXCLUSIVE_LOCK | LOCKFILE_FAIL_IMMEDIATELY,
                0,
                1,
                0,
                &mut overlapped,
            )
        };

        if res.is_err() {
            let err = Error::last_os_error();
            return Err(Error::new(
                ErrorKind::Other,
                format!("Failed to lock file: {}", err),
            ));
        }

        Ok(())
    }
}

impl Drop for LockFile {
    fn drop(&mut self) {
        self.dispose();
    }
}