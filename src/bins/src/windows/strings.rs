use anyhow::Result;
use windows::core::{PCWSTR, PWSTR};

pub fn string_to_u16<P: AsRef<str>>(input: P) -> Vec<u16> {
    let input = input.as_ref();
    input.encode_utf16().chain(Some(0)).collect::<Vec<u16>>()
}

pub trait WideString {
    fn to_wide_slice(&self) -> &[u16];
}

impl WideString for PWSTR {
    fn to_wide_slice(&self) -> &[u16] {
        unsafe { self.as_wide() }
    }
}

impl WideString for PCWSTR {
    fn to_wide_slice(&self) -> &[u16] {
        unsafe { self.as_wide() }
    }
}

impl WideString for Vec<u16> {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

impl WideString for &Vec<u16> {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

// impl WideString for [u16] {
//     fn to_wide_slice(&self) -> &[u16] {
//         self.as_ref()
//     }
// }

impl<const N: usize> WideString for [u16; N] {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

pub fn u16_to_string_lossy<T: WideString>(input: T) -> String {
    let slice = input.to_wide_slice();
    let null_pos = slice.iter().position(|&x| x == 0).unwrap_or(slice.len());
    let trimmed_slice = &slice[..null_pos];
    String::from_utf16_lossy(trimmed_slice)
}

pub fn u16_to_string<T: WideString>(input: T) -> Result<String> {
    let slice = input.to_wide_slice();
    let null_pos = slice.iter().position(|&x| x == 0).unwrap_or(slice.len());
    let trimmed_slice = &slice[..null_pos];
    Ok(String::from_utf16(trimmed_slice)?)
}

// pub fn pwstr_to_string(input: PWSTR) -> Result<String> {
//     unsafe {
//         let hstring = input.to_hstring();
//         let string = hstring.to_string_lossy();
//         Ok(string.trim_end_matches('\0').to_string())
//     }
// }

// pub fn pcwstr_to_string(input: PCWSTR) -> Result<String> {
//     unsafe {
//         let hstring = input.to_hstring();
//         let string = hstring.to_string_lossy();
//         Ok(string.trim_end_matches('\0').to_string())
//     }
// }

// pub fn u16_to_string<T: AsRef<[u16]>>(input: T) -> Result<String> {
//     let input = input.as_ref();
//     let hstring = HSTRING::from_wide(input);
//     let string = hstring.to_string_lossy();
//     Ok(string.trim_end_matches('\0').to_string())
// }

// pub fn u16_to_string<T: AsRef<[u16]>>(input: T) -> Result<String> {
//     let input = input.as_ref();
//     // Find position of first null byte (0)
//     let null_pos = input.iter().position(|&x| x == 0).unwrap_or(input.len());
//     // Take only up to the first null byte
//     let trimmed_input = &input[..null_pos];
//     let hstring = HSTRING::from_wide(trimmed_input);
//     Ok(hstring.to_string_lossy())
// }
