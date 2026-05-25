use crate::errors::Error;

fn map_err(e: String) -> Error {
    Error::Io(e)
}

pub struct HandleGuard(pub u32);
impl Drop for HandleGuard {
    fn drop(&mut self) {
        let _ = close(self.0);
    }
}

pub fn open(path: &str, writable: bool, create: bool) -> Result<u32, Error> {
    crate::velopack::core::host_filesystem::open(path, writable, create).map_err(map_err)
}

pub fn read(handle: u32, length: u32) -> Result<Vec<u8>, Error> {
    crate::velopack::core::host_filesystem::read(handle, length).map_err(map_err)
}

pub fn write(handle: u32, data: &[u8]) -> Result<(), Error> {
    crate::velopack::core::host_filesystem::write(handle, data).map_err(map_err)
}

pub fn seek(handle: u32, pos: u64) -> Result<(), Error> {
    crate::velopack::core::host_filesystem::seek(handle, pos).map_err(map_err)
}

pub fn close(handle: u32) -> Result<(), Error> {
    crate::velopack::core::host_filesystem::close(handle).map_err(map_err)
}

pub fn list_dir(path: &str) -> Result<Vec<String>, Error> {
    crate::velopack::core::host_filesystem::list_dir(path).map_err(map_err)
}

pub fn delete_file(path: &str) -> Result<(), Error> {
    crate::velopack::core::host_filesystem::delete_file(path).map_err(map_err)
}

pub fn rename_file(from: &str, to: &str) -> Result<(), Error> {
    crate::velopack::core::host_filesystem::rename_file(from, to).map_err(map_err)
}

pub fn get_file_size(path: &str) -> Result<Option<u64>, Error> {
    crate::velopack::core::host_filesystem::get_file_size(path).map_err(map_err)
}

pub fn file_exists(path: &str) -> bool {
    get_file_size(path).map(|o| o.is_some()).unwrap_or(false)
}

pub fn read_to_string(path: &str) -> Result<String, Error> {
    let data = read_all(path)?;
    String::from_utf8(data).map_err(|e| Error::Io(e.to_string()))
}

pub fn read_all(path: &str) -> Result<Vec<u8>, Error> {
    let size = get_file_size(path)?.unwrap_or(0) as usize;
    let h = open(path, false, false)?;
    let guard = HandleGuard(h);
    let mut result = Vec::with_capacity(size);
    loop {
        let chunk = read(h, 64 * 1024)?;
        if chunk.is_empty() {
            break;
        }
        result.extend_from_slice(&chunk);
    }
    close(h)?;
    std::mem::forget(guard);
    Ok(result)
}

pub fn write_all(path: &str, data: &[u8]) -> Result<(), Error> {
    let h = open(path, true, true)?;
    let guard = HandleGuard(h);
    if !data.is_empty() {
        write(h, data)?;
    }
    close(h)?;
    std::mem::forget(guard);
    Ok(())
}

pub fn copy_file(from: &str, to: &str) -> Result<(), Error> {
    let rh = open(from, false, false)?;
    let rg = HandleGuard(rh);
    let wh = open(to, true, true)?;
    let wg = HandleGuard(wh);
    loop {
        let chunk = read(rh, 64 * 1024)?;
        if chunk.is_empty() {
            break;
        }
        write(wh, &chunk)?;
    }
    close(rh)?;
    std::mem::forget(rg);
    close(wh)?;
    std::mem::forget(wg);
    Ok(())
}
