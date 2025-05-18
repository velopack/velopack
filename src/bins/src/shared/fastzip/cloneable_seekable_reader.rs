// Copyright 2022 Google LLC

// Licensed under the Apache License, Version 2.0 <LICENSE-APACHE or
// https://www.apache.org/licenses/LICENSE-2.0> or the MIT license
// <LICENSE-MIT or https://opensource.org/licenses/MIT>, at your
// option. This file may not be copied, modified, or distributed
// except according to those terms.

use std::{
    io::{Read, Seek, SeekFrom},
    sync::{Arc, Mutex},
};

use super::ripunzip::determine_stream_len;

struct Inner<R: Read + Seek> {
    /// The underlying Read implementation.
    r: R,
    /// The position of r.
    pos: u64,
    /// The length of r, lazily loaded.
    len: Option<u64>,
}

impl<R: Read + Seek> Inner<R> {
    fn new(r: R) -> Self {
        Self { r, pos: 0, len: None }
    }

    /// Get the length of the data stream. This is assumed to be constant.
    fn len(&mut self) -> std::io::Result<u64> {
        // Return cached size
        if let Some(len) = self.len {
            return Ok(len);
        }

        let len = determine_stream_len(&mut self.r)?;
        self.len = Some(len);
        Ok(len)
    }

    /// Read into the given buffer, starting at the given offset in the data stream.
    fn read_at(&mut self, offset: u64, buf: &mut [u8]) -> std::io::Result<usize> {
        if offset != self.pos {
            self.r.seek(SeekFrom::Start(offset))?;
        }
        let read_result = self.r.read(buf);
        if let Ok(bytes_read) = read_result {
            // TODO, once stabilised, use checked_add_signed
            self.pos += bytes_read as u64;
        }
        read_result
    }
}

/// A [`Read`] which refers to its underlying stream by reference count,
/// and thus can be cloned cheaply. It supports seeking; each cloned instance
/// maintains its own pointer into the file, and the underlying instance
/// is seeked prior to each read.
pub(crate) struct CloneableSeekableReader<R: Read + Seek> {
    /// The wrapper around the Read implementation, shared between threads.
    inner: Arc<Mutex<Inner<R>>>,
    /// The position of _this_ reader.
    pos: u64,
}

impl<R: Read + Seek> Clone for CloneableSeekableReader<R> {
    fn clone(&self) -> Self {
        Self { inner: self.inner.clone(), pos: self.pos }
    }
}

impl<R: Read + Seek> CloneableSeekableReader<R> {
    /// Constructor. Takes ownership of the underlying `Read`.
    /// You should pass in only streams whose total length you expect
    /// to be fixed and unchanging. Odd behavior may occur if the length
    /// of the stream changes; any subsequent seeks will not take account
    /// of the changed stream length.
    pub(crate) fn new(r: R) -> Self {
        Self { inner: Arc::new(Mutex::new(Inner::new(r))), pos: 0u64 }
    }
}

impl<R: Read + Seek> Read for CloneableSeekableReader<R> {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        let mut inner = self.inner.lock().unwrap();
        let read_result = inner.read_at(self.pos, buf);
        if let Ok(bytes_read) = read_result {
            self.pos = self
                .pos
                .checked_add(bytes_read as u64)
                .ok_or(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Read too far forward"))?;
        }
        read_result
    }
}

impl<R: Read + Seek> Seek for CloneableSeekableReader<R> {
    fn seek(&mut self, pos: SeekFrom) -> std::io::Result<u64> {
        let new_pos = match pos {
            SeekFrom::Start(pos) => pos,
            SeekFrom::End(offset_from_end) => {
                let file_len = self.inner.lock().unwrap().len()?;
                if -offset_from_end as u64 > file_len {
                    return Err(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Seek too far backwards"));
                }
                file_len
                    .checked_add_signed(offset_from_end)
                    .ok_or(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Seek too far backward from end"))?
            }
            SeekFrom::Current(offset_from_pos) => self
                .pos
                .checked_add_signed(offset_from_pos)
                .ok_or(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Seek too far forward from current pos"))?,
        };
        self.pos = new_pos;
        Ok(new_pos)
    }
}

#[cfg(test)]
mod test {
    use super::CloneableSeekableReader;
    use std::io::{Cursor, Read, Seek, SeekFrom};
    // use test_log::test;

    #[test]
    fn test_cloneable_seekable_reader() -> std::io::Result<()> {
        let buf: Vec<u8> = vec![0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        let buf = Cursor::new(buf);
        let mut reader = CloneableSeekableReader::new(buf);
        let mut out = vec![0; 2];
        reader.read_exact(&mut out)?;
        assert_eq!(&out, &[0, 1]);
        reader.rewind()?;
        reader.read_exact(&mut out)?;
        assert_eq!(&out, &[0, 1]);
        reader.stream_position()?;
        reader.read_exact(&mut out)?;
        assert_eq!(&out, &[2, 3]);
        reader.seek(SeekFrom::End(-2))?;
        reader.read_exact(&mut out)?;
        assert_eq!(&out, &[8, 9]);
        assert!(reader.read_exact(&mut out).is_err());
        Ok(())
    }

    #[test]
    fn test_cloned_independent_positions() -> std::io::Result<()> {
        let buf: Vec<u8> = vec![0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        let buf = Cursor::new(buf);
        let mut r1 = CloneableSeekableReader::new(buf);
        let mut r2 = r1.clone();
        let mut out = vec![0; 2];
        r1.read_exact(&mut out)?;
        assert_eq!(&out, &[0, 1]);
        r2.read_exact(&mut out)?;
        assert_eq!(&out, &[0, 1]);
        r1.read_exact(&mut out)?;
        assert_eq!(&out, &[2, 3]);
        r2.seek(SeekFrom::End(-2))?;
        r2.read_exact(&mut out)?;
        assert_eq!(&out, &[8, 9]);
        r1.read_exact(&mut out)?;
        assert_eq!(&out, &[4, 5]);
        Ok(())
    }
}
