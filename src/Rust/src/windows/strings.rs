use anyhow::Result;
use windows::core::{HSTRING, PCWSTR, PWSTR};

pub fn string_to_pwstr<P: AsRef<str>>(input: P) -> PWSTR {
    let mut arr = string_to_u16(input);
    PWSTR(arr.as_mut_ptr())
}

pub fn string_to_pcwstr<P: AsRef<str>>(input: P) -> PCWSTR {
    let arr = string_to_u16(input);
    PCWSTR(arr.as_ptr())
}

pub fn string_to_u16<P: AsRef<str>>(input: P) -> Vec<u16> {
    let input = input.as_ref();
    input.encode_utf16().chain(Some(0)).collect::<Vec<u16>>()
}

pub fn pwstr_to_string(input: PWSTR) -> Result<String> {
    unsafe {
        let hstring = input.to_hstring()?;
        let string = hstring.to_string_lossy();
        Ok(string.trim_end_matches('\0').to_string())
    }
}

pub fn pcwstr_to_string(input: PCWSTR) -> Result<String> {
    unsafe {
        let hstring = input.to_hstring()?;
        let string = hstring.to_string_lossy();
        Ok(string.trim_end_matches('\0').to_string())
    }
}

pub fn u16_to_string<T: AsRef<[u16]>>(input: T) -> Result<String> {
    let input = input.as_ref();
    let hstring = HSTRING::from_wide(input)?;
    let string = hstring.to_string_lossy();
    Ok(string.trim_end_matches('\0').to_string())
}
