use anyhow::Result;
use windows::core::{PCWSTR, PWSTR};

pub struct WideString {
    inner: Vec<u16>,
}

impl WideString {
    pub fn as_ptr(&self) -> *const u16 {
        self.inner.as_ptr()
    }

    pub fn as_mut_ptr(&mut self) -> *mut u16 {
        self.inner.as_mut_ptr()
    }

    pub fn len(&self) -> usize {
        self.inner.len()
    }

    pub fn as_pcwstr(&self) -> PCWSTR {
        PCWSTR(self.as_ptr())
    }

    pub fn as_pwstr(&mut self) -> PWSTR {
        PWSTR(self.as_mut_ptr())
    }
}

impl Into<Vec<u16>> for WideString {
    fn into(self) -> Vec<u16> {
        self.inner
    }
}

impl Into<PCWSTR> for WideString {
    fn into(self) -> PCWSTR {
        self.as_pcwstr()
    }
}

impl AsRef<[u16]> for WideString {
    fn as_ref(&self) -> &[u16] {
        &self.inner
    }
}

impl AsMut<[u16]> for WideString {
    fn as_mut(&mut self) -> &mut [u16] {
        &mut self.inner
    }
}

pub fn string_to_u16<P: AsRef<str>>(input: P) -> Vec<u16> {
    let input = input.as_ref();
    input.encode_utf16().chain(Some(0)).collect::<Vec<u16>>()
}

pub fn string_to_wide<P: AsRef<str>>(input: P) -> WideString {
    WideString { inner: string_to_u16(input) }
}

pub trait WideStringRef {
    fn to_wide_slice(&self) -> &[u16];
}

impl WideStringRef for PWSTR {
    fn to_wide_slice(&self) -> &[u16] {
        unsafe { self.as_wide() }
    }
}

impl WideStringRef for PCWSTR {
    fn to_wide_slice(&self) -> &[u16] {
        unsafe { self.as_wide() }
    }
}

impl WideStringRef for Vec<u16> {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

impl WideStringRef for &Vec<u16> {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

// impl WideString for [u16] {
//     fn to_wide_slice(&self) -> &[u16] {
//         self.as_ref()
//     }
// }

impl<const N: usize> WideStringRef for [u16; N] {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

pub fn u16_to_string_lossy<T: WideStringRef>(input: T) -> String {
    let slice = input.to_wide_slice();
    let null_pos = slice.iter().position(|&x| x == 0).unwrap_or(slice.len());
    let trimmed_slice = &slice[..null_pos];
    String::from_utf16_lossy(trimmed_slice)
}

pub fn u16_to_string<T: WideStringRef>(input: T) -> Result<String> {
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
