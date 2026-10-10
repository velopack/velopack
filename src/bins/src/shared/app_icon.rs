//! Locates the app icon that `vpk pack --icon` put into the package, so dialogs can show it.
//! - Windows: vpk embeds the `.ico` into Setup.exe / Update.exe as icon group 1.
//! - Linux: the AppImage root (`{root}/usr/bin/UpdateNix`) has the icon at `{root}/.DirIcon`.
//! - macOS: `{App}.app/Contents/Resources/{CFBundleIconFile}`, next to `Contents/MacOS/UpdateMac`.

use std::path::Path;

/// Loads the app icon of the running Setup / Update binary and sets it as the icon of every dialog.
pub fn init_dialog_icon() {
    if let Some(icon) = load_app_icon().filter(|icon| is_supported_icon(icon)) {
        crate::dialogs::set_app_icon(icon);
    }
}

/// Only images xdialog can decode, so general dialogs fall back to a stock icon otherwise (e.g. an SVG `.DirIcon`).
fn is_supported_icon(bytes: &[u8]) -> bool {
    bytes.starts_with(b"\x89PNG\r\n\x1a\n") || bytes.starts_with(b"icns") || bytes.starts_with(&[0, 0, 1, 0])
}

#[allow(dead_code)]
fn read_icon_file(path: &Path) -> Option<Vec<u8>> {
    std::fs::read(path).ok()
}

#[cfg(target_os = "windows")]
fn load_app_icon() -> Option<Vec<u8>> {
    use windows::core::PCWSTR;
    use windows::Win32::System::LibraryLoader::{FindResourceW, LoadResource, LockResource, SizeofResource};
    use windows::Win32::UI::WindowsAndMessaging::{RT_GROUP_ICON, RT_ICON};

    fn load_resource(kind: PCWSTR, id: u16) -> Option<&'static [u8]> {
        unsafe {
            let res = FindResourceW(None, PCWSTR(id as usize as *const u16), kind);
            if res.is_invalid() {
                return None;
            }
            let size = SizeofResource(None, res) as usize;
            let ptr = LockResource(LoadResource(None, res).ok()?) as *const u8;
            if ptr.is_null() || size == 0 {
                return None;
            }
            Some(std::slice::from_raw_parts(ptr, size))
        }
    }

    // vpk (ResourceEdit.SetExeIcon) always writes the icon as group 1
    let group = load_resource(RT_GROUP_ICON, 1)?;
    ico_from_icon_group(group, |id| load_resource(RT_ICON, id))
}

/// Rebuilds an `.ico` file from an `RT_GROUP_ICON` resource and its `RT_ICON` images.
/// The group is an ICONDIR whose 14-byte entries end in a u16 resource id, where an `.ico`
/// has 16-byte entries ending in a u32 file offset to the image.
#[allow(dead_code)]
fn ico_from_icon_group<'a>(group: &[u8], load_image: impl Fn(u16) -> Option<&'a [u8]>) -> Option<Vec<u8>> {
    let count = u16::from_le_bytes(group.get(4..6)?.try_into().ok()?) as usize;
    let entries = group.get(6..6 + count * 14)?;
    let images = entries
        .chunks_exact(14)
        .map(|entry| Some((entry, load_image(u16::from_le_bytes([entry[12], entry[13]]))?)))
        .collect::<Option<Vec<_>>>()?;
    if images.is_empty() {
        return None;
    }

    let mut ico = vec![0, 0, 1, 0];
    ico.extend_from_slice(&(count as u16).to_le_bytes());
    let mut offset = 6 + count * 16;
    for (entry, image) in &images {
        // width, height, color count, reserved, planes, bit count
        ico.extend_from_slice(&entry[..8]);
        ico.extend_from_slice(&(image.len() as u32).to_le_bytes());
        ico.extend_from_slice(&(offset as u32).to_le_bytes());
        offset += image.len();
    }
    for (_, image) in images {
        ico.extend_from_slice(image);
    }
    Some(ico)
}

#[cfg(target_os = "linux")]
fn load_app_icon() -> Option<Vec<u8>> {
    let exe = std::env::current_exe().ok()?;
    let bin_dir = exe.parent()?;
    if !bin_dir.ends_with("usr/bin") {
        return None;
    }
    read_icon_file(&bin_dir.parent()?.parent()?.join(".DirIcon"))
}

#[cfg(target_os = "macos")]
fn load_app_icon() -> Option<Vec<u8>> {
    let exe = std::env::current_exe().ok()?;
    let contents_dir = exe.parent()?.parent()?;
    let plist = std::fs::read_to_string(contents_dir.join("Info.plist")).ok()?;
    let mut icon_name = plist_string_value(&plist, "CFBundleIconFile")?;
    if Path::new(&icon_name).extension().is_none() {
        icon_name.push_str(".icns");
    }
    read_icon_file(&contents_dir.join("Resources").join(icon_name))
}

/// Reads `<key>{key}</key><string>value</string>` from an XML plist (vpk always writes XML).
#[allow(dead_code)]
fn plist_string_value(plist: &str, key: &str) -> Option<String> {
    let after_key = &plist[plist.find(&format!("<key>{key}</key>"))?..];
    let value = after_key[after_key.find("<string>")? + "<string>".len()..]
        .split("</string>")
        .next()?
        .trim();
    (!value.is_empty()).then(|| value.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ico_from_icon_group_rebuilds_ico() {
        // two images: 16x16 32bpp (id 5) and 256x256 32bpp (id 7)
        let mut group = vec![0, 0, 1, 0, 2, 0];
        group.extend_from_slice(&[16, 16, 0, 0, 1, 0, 32, 0, 3, 0, 0, 0, 5, 0]);
        group.extend_from_slice(&[0, 0, 0, 0, 1, 0, 32, 0, 2, 0, 0, 0, 7, 0]);
        let small: &[u8] = &[1, 2, 3];
        let big: &[u8] = &[4, 5];
        let ico = ico_from_icon_group(&group, |id| match id {
            5 => Some(small),
            7 => Some(big),
            _ => None,
        })
        .unwrap();

        let header_len = 6 + 2 * 16;
        let mut expected = vec![0, 0, 1, 0, 2, 0];
        expected.extend_from_slice(&[16, 16, 0, 0, 1, 0, 32, 0, 3, 0, 0, 0, header_len as u8, 0, 0, 0]);
        expected.extend_from_slice(&[0, 0, 0, 0, 1, 0, 32, 0, 2, 0, 0, 0, header_len as u8 + 3, 0, 0, 0]);
        expected.extend_from_slice(&[1, 2, 3, 4, 5]);
        assert_eq!(ico, expected);
        assert!(is_supported_icon(&ico));
    }

    #[test]
    fn ico_from_icon_group_rejects_missing_images() {
        let mut group = vec![0, 0, 1, 0, 1, 0];
        group.extend_from_slice(&[16, 16, 0, 0, 1, 0, 32, 0, 3, 0, 0, 0, 5, 0]);
        assert!(ico_from_icon_group(&group, |_| None).is_none());
        assert!(ico_from_icon_group(&[0, 0, 1, 0, 0, 0], |_| None).is_none());
        assert!(ico_from_icon_group(&[0, 0, 1, 0, 3, 0], |_| None).is_none());
    }

    #[test]
    fn plist_string_value_reads_icon_file() {
        let plist = r#"<dict>
    <key>CFBundleName</key>
    <string>MyApp</string>
    <key>CFBundleIconFile</key>
    <string>MyApp.icns</string>
</dict>"#;
        assert_eq!(plist_string_value(plist, "CFBundleIconFile").as_deref(), Some("MyApp.icns"));
        assert_eq!(plist_string_value(plist, "Missing"), None);
    }

    #[test]
    fn is_supported_icon_checks_format() {
        assert!(is_supported_icon(b"\x89PNG\r\n\x1a\nrest"));
        assert!(is_supported_icon(b"icns\0\0\0\x08"));
        assert!(!is_supported_icon(b"<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
    }
}
