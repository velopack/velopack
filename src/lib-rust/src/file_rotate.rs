use std::fs::{self, File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};

pub struct FileRotate {
    file: Option<File>,
    path: PathBuf,
    max_size: u64,
    current_size: u64,
}

/// This is a very simple log file rotate implementation. It should implement the Write trait.
/// It should fail-safe, and not panic if it can't write logs for some reason.
/// It will rotate the "path" to "path.old" when it reaches 1mb, overwriting any previous "path.old"
impl FileRotate {
    pub fn new<P: AsRef<Path>>(path: P, max_size: u64) -> Self {
        let path = path.as_ref();

        let mut me = Self { file: None, path: path.to_path_buf(), max_size, current_size: 0 };

        me.ensure_log_dir_exists();

        if let Ok(metadata) = fs::metadata(&me.path) {
            let size = metadata.len();
            if size > me.max_size {
                me.rotate();
            } else {
                me.current_size = size;
            }
        }

        me.open_file();
        me
    }

    fn ensure_log_dir_exists(&self) {
        if let Some(parent) = self.path.parent() {
            if let Err(_e) = fs::create_dir_all(parent) {
                // eprintln!("Failed to create log file parent directory: {e}");
            }
        }
    }

    fn open_file(&mut self) {
        if let Some(_) = &self.file {
            return;
        }

        self.ensure_log_dir_exists();

        let mut o = OpenOptions::new();
        o.read(true).create(true).append(true);

        match o.open(&self.path) {
            Ok(file) => self.file = Some(file),
            Err(_e) => {} //eprintln!("Failed to open log file: {e}"),
        }
    }

    fn rotate(&mut self) {
        self.ensure_log_dir_exists();

        let _ = self.file.take();

        let rotation_path = append_extension(&self.path, "old");

        if let Err(_e) = fs::rename(&self.path, &rotation_path) {
            // eprintln!("Failed to rotate log file: {e}");
        } else {
            self.current_size = 0;
        }

        self.open_file();
    }
}

impl Write for FileRotate {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        if self.file.is_none() {
            self.open_file();
        }

        if let Some(file) = &mut self.file {
            let written = file.write(buf)?;
            self.current_size += written as u64;
            if self.current_size > self.max_size {
                self.flush()?;
                self.rotate();
            }
            Ok(written)
        } else {
            Err(std::io::Error::new(std::io::ErrorKind::Other, "File not open"))
        }
    }

    fn flush(&mut self) -> std::io::Result<()> {
        if self.file.is_none() {
            self.open_file();
        }

        if let Some(file) = &mut self.file {
            file.flush()
        } else {
            Err(std::io::Error::new(std::io::ErrorKind::Other, "File not open"))
        }
    }
}

fn append_extension(path: &Path, ext: &str) -> PathBuf {
    let mut os_string = path.as_os_str().to_os_string();
    os_string.push(format!(".{}", ext));
    PathBuf::from(os_string)
}

#[cfg(test)]
fn write_lines(path: &PathBuf, lines: u32) {
    let mut writer = FileRotate::new(path, 200);
    for idx in 1..=lines {
        let _ = writeln!(writer, "Line {}: Hello World!", idx);
    }
}

#[test]
fn test_file_rotate() {
    let tmpdir = tempfile::tempdir().unwrap();

    let path = tmpdir.path().join("test.log");
    write_lines(&path, 28);

    let content = fs::read_to_string(&path).unwrap();

    let expected = "
20: Hello World!
Line 21: Hello World!
Line 22: Hello World!
Line 23: Hello World!
Line 24: Hello World!
Line 25: Hello World!
Line 26: Hello World!
Line 27: Hello World!
Line 28: Hello World!
";

    assert_eq!(content.trim(), expected.trim());
}

#[cfg(windows)]
#[test]
fn test_file_rotate_case_insensitive() {
    let tmpdir = tempfile::tempdir().unwrap();

    let path = tmpdir.path().join("test.log");
    write_lines(&path, 7);
    write_lines(&path, 7);
    write_lines(&path, 7);

    let path2 = tmpdir.path().join("Test.log");
    write_lines(&path2, 7);
    write_lines(&path2, 7);
    write_lines(&path2, 7);

    let content = fs::read_to_string(&path).unwrap();
    let content_old = fs::read_to_string(&append_extension(&path, "old")).unwrap();

    let expected = "
Line 6: Hello World!
Line 7: Hello World!
";

    let expected_old = "
Line 3: Hello World!
Line 4: Hello World!
Line 5: Hello World!
Line 6: Hello World!
Line 7: Hello World!
Line 1: Hello World!
Line 2: Hello World!
Line 3: Hello World!
Line 4: Hello World!
Line 5: Hello World!
";

    assert_eq!(content.trim(), expected.trim());
    assert_eq!(content_old.trim(), expected_old.trim());
}
