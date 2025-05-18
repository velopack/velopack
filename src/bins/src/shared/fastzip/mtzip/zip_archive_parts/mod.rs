pub mod data;
pub mod extra_field;
pub mod file;
pub mod job;
use std::io::Seek;
#[inline]
pub fn stream_position_u32<W: Seek>(buf: &mut W) -> std::io::Result<u32> {
    let offset = buf.stream_position()?;
    debug_assert!(offset <= u32::MAX.into());
    Ok(offset as u32)
}
#[inline]
pub fn files_amount_u16<T>(files: &[T]) -> u16 {
    let amount = files.len();
    debug_assert!(amount <= u16::MAX as usize);
    amount as u16
}
