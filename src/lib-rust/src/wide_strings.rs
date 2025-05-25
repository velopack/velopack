use std::{
    ffi::{c_void, OsStr, OsString},
    fmt::Debug,
    os::windows::ffi::{OsStrExt, OsStringExt},
};
use windows::core::{PCWSTR, PWSTR};

#[derive(Clone, PartialEq, Eq)]
pub struct WideString {
    str: OsString,
    vec: Vec<u16>,
}

impl WideString {
    pub fn as_slice(&self) -> &[u16] {
        &self.vec
    }

    pub fn as_mut_slice(&mut self) -> &mut [u16] {
        &mut self.vec
    }

    pub fn len(&self) -> usize {
        self.vec.len()
    }

    pub fn as_os_str(&self) -> &OsStr {
        self.str.as_os_str()
    }

    pub fn as_cvoid(&self) -> *const c_void {
        self.as_ptr() as *const c_void
    }

    pub fn as_mut_cvoid(&mut self) -> *const c_void {
        self.as_mut_ptr() as *const c_void
    }

    pub fn as_ptr(&self) -> *const u16 {
        self.as_slice().as_ptr()
    }

    pub fn as_mut_ptr(&mut self) -> *mut u16 {
        self.as_mut_slice().as_mut_ptr()
    }

    pub fn as_pcwstr(&self) -> PCWSTR {
        PCWSTR(self.as_ptr())
    }

    pub fn as_pwstr(&mut self) -> PWSTR {
        PWSTR(self.as_mut_ptr())
    }
}

impl Debug for WideString {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "WideString({:?})", self.str)
    }
}

impl From<Vec<u16>> for WideString {
    fn from(inner: Vec<u16>) -> Self {
        WideString { str: OsString::from_wide(&inner), vec: inner }
    }
}

impl From<String> for WideString {
    fn from(inner: String) -> Self {
        string_to_wide(inner)
    }
}

impl From<&str> for WideString {
    fn from(inner: &str) -> Self {
        string_to_wide(inner)
    }
}

impl Into<Vec<u16>> for WideString {
    fn into(self) -> Vec<u16> {
        self.vec
    }
}

impl AsRef<OsStr> for WideString {
    fn as_ref(&self) -> &OsStr {
        self.str.as_os_str()
    }
}

impl AsRef<[u16]> for WideString {
    fn as_ref(&self) -> &[u16] {
        &self.vec
    }
}

impl AsMut<[u16]> for WideString {
    fn as_mut(&mut self) -> &mut [u16] {
        &mut self.vec
    }
}

pub fn string_to_wide<P: AsRef<OsStr>>(input: P) -> WideString {
    let str = input.as_ref();
    let str = OsString::from(str);
    let vec = str
        .encode_wide()
        .filter(|f| *f != 0) // Filter out any null characters
        .chain(Some(0))
        .collect::<Vec<u16>>();
    WideString { vec, str }
}

pub fn string_to_wide_opt<P: AsRef<OsStr>>(input: Option<P>) -> Option<WideString> {
    input.map(string_to_wide)
}

pub trait ToWideSlice {
    fn to_wide_slice(&self) -> &[u16];
}

impl ToWideSlice for PWSTR {
    fn to_wide_slice(&self) -> &[u16] {
        unsafe { self.as_wide() }
    }
}

impl ToWideSlice for PCWSTR {
    fn to_wide_slice(&self) -> &[u16] {
        unsafe { self.as_wide() }
    }
}

impl ToWideSlice for Vec<u16> {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

impl ToWideSlice for &Vec<u16> {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

impl ToWideSlice for &[u16] {
    fn to_wide_slice(&self) -> &[u16] {
        self
    }
}

impl<const N: usize> ToWideSlice for [u16; N] {
    fn to_wide_slice(&self) -> &[u16] {
        self.as_ref()
    }
}

pub fn wide_to_string_lossy<T: ToWideSlice>(input: T) -> String {
    let slice = input.to_wide_slice();
    let null_pos = slice.iter().position(|&x| x == 0).unwrap_or(slice.len());
    let trimmed_slice = &slice[..null_pos];
    String::from_utf16_lossy(trimmed_slice)
}

pub fn wide_to_string_lossy_opt<T: ToWideSlice>(input: Option<T>) -> Option<String> {
    input.map(wide_to_string_lossy)
}

pub fn wide_to_string<T: ToWideSlice>(input: T) -> Result<String, std::string::FromUtf16Error> {
    let slice = input.to_wide_slice();
    let null_pos = slice.iter().position(|&x| x == 0).unwrap_or(slice.len());
    let trimmed_slice = &slice[..null_pos];
    Ok(String::from_utf16(trimmed_slice)?)
}

pub fn wide_to_string_opt<T: ToWideSlice>(input: Option<T>) -> Option<Result<String, std::string::FromUtf16Error>> {
    input.map(wide_to_string)
}

pub fn wide_to_os_string<T: ToWideSlice>(input: T) -> OsString {
    let slice = input.to_wide_slice();
    let null_pos = slice.iter().position(|&x| x == 0).unwrap_or(slice.len());
    let trimmed_slice = &slice[..null_pos];
    OsString::from_wide(trimmed_slice)
}

pub fn wide_to_os_string_opt<T: ToWideSlice>(input: Option<T>) -> Option<OsString> {
    input.map(wide_to_os_string)
}
