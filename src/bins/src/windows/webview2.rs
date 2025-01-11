use std::{fmt::Display, marker::PhantomData, mem, ptr};

use windows::{
    core::{Param, HRESULT, PCWSTR, PWSTR},
    Win32::{Globalization::lstrlenW, System::Com},
};

/// RAII holder for a [`PWSTR`] which is allocated with [`Com::CoTaskMemAlloc`] and freed
/// with [`Com::CoTaskMemFree`] when dropped.
pub struct CoTaskMemPWSTR<'a>(PWSTR, PhantomData<&'a PWSTR>);

/// Constant guard object tied to the lifetime of the [`CoTaskMemPWSTR`] so that it
/// is safe to dereference the [`PCWSTR`] as long as both are still in scope.
pub struct CoTaskMemRef<'a>(PCWSTR, PhantomData<&'a PCWSTR>);

impl<'a> CoTaskMemRef<'a> {
    pub fn as_pcwstr(&self) -> &PCWSTR {
        &self.0
    }
}

impl<'a> From<&'a CoTaskMemPWSTR<'a>> for CoTaskMemRef<'a> {
    fn from(value: &'a CoTaskMemPWSTR<'a>) -> Self {
        Self(PCWSTR::from_raw(value.0.as_ptr()), PhantomData)
    }
}

/// Mutable guard object tied to the lifetime of the [`CoTaskMemPWSTR`] so that it
/// is safe to dereference the [`PWSTR`] as long as both are still in scope.
pub struct CoTaskMemMut<'a>(&'a PWSTR);

impl<'a> CoTaskMemMut<'a> {
    pub fn as_pwstr(&mut self) -> &'a PWSTR {
        self.0
    }
}

impl<'a> From<&'a mut CoTaskMemPWSTR<'a>> for CoTaskMemMut<'a> {
    fn from(value: &'a mut CoTaskMemPWSTR<'a>) -> Self {
        Self(&value.0)
    }
}

impl<'a> CoTaskMemPWSTR<'a> {
    /// Get a mutable [`PWSTR`] guard which borrows the pointer.
    pub fn as_mut(&'a mut self) -> CoTaskMemMut<'a> {
        From::from(self)
    }

    /// Get a constant [`PCWSTR`] guard which borrows the pointer.
    pub fn as_ref(&'a self) -> CoTaskMemRef<'a> {
        From::from(self)
    }

    /// Take the [`PWSTR`] pointer and hand off ownership so that it is not freed when the `CoTaskMemPWSTR` is dropped.
    pub fn take(&mut self) -> PWSTR {
        let result = self.0;
        self.0 = PWSTR::null();
        result
    }
}

impl<'a> Drop for CoTaskMemPWSTR<'a> {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe {
                Com::CoTaskMemFree(Some(self.0.as_ptr() as *mut _ as *const _));
            }
        }
    }
}

impl<'a> Default for CoTaskMemPWSTR<'a> {
    fn default() -> Self {
        Self(PWSTR::null(), PhantomData)
    }
}

impl<'a> From<PWSTR> for CoTaskMemPWSTR<'a> {
    fn from(value: PWSTR) -> Self {
        Self(value, PhantomData)
    }
}

impl<'a> From<&str> for CoTaskMemPWSTR<'a> {
    fn from(value: &str) -> Self {
        match value {
            "" => Default::default(),
            value => {
                let encoded: Vec<_> = value.encode_utf16().chain(std::iter::once(0)).collect();

                unsafe {
                    let mut buffer = Com::CoTaskMemAlloc(encoded.len() * mem::size_of::<u16>()) as *mut u16;
                    let result = PWSTR::from_raw(buffer);

                    for char in encoded {
                        *buffer = char;
                        buffer = buffer.add(1);
                    }

                    Self(result, PhantomData)
                }
            }
        }
    }
}

impl<'a> Display for CoTaskMemPWSTR<'a> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let value = string_from_pcwstr(self.as_ref().as_pcwstr());
        f.write_str(value.as_str())
    }
}

/// Copy a [`PCWSTR`] from an input param to a [`String`].
pub fn string_from_pcwstr(source: &PCWSTR) -> String {
    if source.0.is_null() {
        String::new()
    } else {
        let len = unsafe { lstrlenW(*source) };

        if len > 0 {
            unsafe {
                let buffer = ptr::slice_from_raw_parts(source.0, len as usize);
                String::from_utf16_lossy(&*buffer)
            }
        } else {
            String::new()
        }
    }
}

/// Copy a [`PWSTR`] allocated with [`Com::CoTaskMemAlloc`] from an input param to a [`String`]
/// and free the original buffer with [`Com::CoTaskMemFree`].
pub fn take_pwstr(source: PWSTR) -> String {
    CoTaskMemPWSTR::from(source).to_string()
}

/// Allocate a [`PWSTR`] with [`Com::CoTaskMemAlloc`] and copy a [`&str`] into it.
pub fn pwstr_from_str(source: &str) -> PWSTR {
    CoTaskMemPWSTR::from(source).take()
}

// https://github.com/myhrmans/figma-content-length-bug/blob/980b5ce03171218904782f9ab590857d6c7de700/src/webview/webview2/mod.rs#L752
pub fn get_webview_version() -> Option<String> {
    // #[cfg_attr(target_env = "msvc", link(name = "WebView2LoaderStatic", kind = "static"))]
    // #[cfg_attr(not(target_env = "msvc"), link(name = "WebView2Loader.dll"))]
    #[link(name = "WebView2LoaderStatic", kind = "static")]
    extern "system" {
        pub fn GetAvailableCoreWebView2BrowserVersionString(browserexecutablefolder: PCWSTR, versioninfo: *mut PWSTR) -> HRESULT;
    }

    let browserexecutablefolder = PCWSTR::null();
    let mut versioninfo = PWSTR::null();

    let hr = unsafe { GetAvailableCoreWebView2BrowserVersionString(browserexecutablefolder.param().abi(), &mut versioninfo).ok() };
    if hr.is_err() || versioninfo.is_null() {
        return None;
    }

    let str = CoTaskMemPWSTR::from(versioninfo).to_string();
    if str.is_empty() {
        None
    } else {
        Some(str)
    }
}

#[test]
fn test_webview_version() {
    let version = get_webview_version();
    assert!(version.is_some());
}
