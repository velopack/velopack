//! Reader + patcher for the Windows installer channel tag.
//!
//! The promotion worker embeds the target channel as a dummy, self-signed certificate added to the
//! `SignedData.certificates` SET of the installer's existing Authenticode signature (the
//! Chrome/Omaha `certificate_tag` technique). The channel payload lives in a non-critical X.509
//! extension under OID `1.3.6.1.4.1.11129.2.1.9999`. Adding a certificate changes no
//! Authenticode-hashed bytes, so the existing signature stays valid with no re-signing.
//!
//! This module is the single source of truth for the tag format: both the Setup.exe stub and the
//! MSI wix-dll custom action call [`read_channel_from_signature`] + [`patch_installed_channel`]
//! and differ only in how they obtain the raw signature bytes (PE attribute certificate table vs.
//! the MSI `\x05DigitalSignature` compound-file stream).
//!
//! SECURITY: The tag is UNSIGNED, attacker-modifiable data — by construction it lives outside the
//! Authenticode hash. Treat it as untrusted input: a channel selector ONLY. Never place anything
//! trust-bearing there (feed URLs, keys, flags that gate trust/permissions). Validate the channel
//! charset strictly on read. Its only effect is which `releases.<channel>.json` the app polls;
//! every downloaded package is still signature/hash-verified as normal. This mirrors how Chrome
//! treats brand codes.

use std::fs;
use std::path::Path;

use anyhow::{bail, Context, Result};

/// The OID of the non-critical X.509 extension carrying the channel tag payload
/// (Google Omaha `certificate_tag`; `oidChromeTag`).
pub const CHANNEL_TAG_OID: &str = "1.3.6.1.4.1.11129.2.1.9999";

/// The 15-byte DER marker that precedes the payload — the reader's search key. This is the DER
/// encoding of the OID above followed by `04 82` (OCTET STRING with a 2-byte big-endian length).
pub static OID_SEARCH_BYTES: [u8; 15] = [0x06, 0x0b, 0x2b, 0x06, 0x01, 0x04, 0x01, 0xd6, 0x79, 0x02, 0x01, 0xce, 0x0f, 0x04, 0x82];

/// The 32-byte magic prefix of the channel tag record:
/// `SHA-256("velopack windows installer channel tag")`.
pub static CHANNEL_TAG_MAGIC: [u8; 32] = [
    0x73, 0x12, 0x9e, 0x58, 0x64, 0xb5, 0x7b, 0x41, 0xfb, 0xca, 0xdb, 0x9d, 0x0b, 0xd5, 0x3f, 0x9d, //
    0x70, 0xb0, 0x23, 0x71, 0xe8, 0xc7, 0xfd, 0x6b, 0x7f, 0xfe, 0x30, 0x5f, 0x14, 0x47, 0x9e, 0x2f, //
];

/// The channel tag record format version this reader accepts.
pub const FORMAT_VERSION: u8 = 0x01;

/// The maximum permitted channel length, in bytes.
pub const MAX_CHANNEL_LEN: usize = 64;

/// Fixed record header size: MAGIC (32) + VERSION (1) + LENGTH (2).
const RECORD_HEADER_LEN: usize = 35;

/// True if `b` is in the strict channel slug charset: `-` | `0-9` | `a-z`.
fn is_valid_channel_byte(b: u8) -> bool {
    b == b'-' || b.is_ascii_digit() || b.is_ascii_lowercase()
}

/// True if `channel` matches `^[a-z0-9-]{1,64}$`.
fn is_valid_channel(channel: &str) -> bool {
    !channel.is_empty() && channel.len() <= MAX_CHANNEL_LEN && channel.bytes().all(is_valid_channel_byte)
}

/// Given raw Authenticode signature bytes (the PE `WIN_CERTIFICATE` payload, or the MSI
/// `\x05DigitalSignature` stream), scan for embedded channel tag records and return the validated
/// channel of the last valid record in file order (last-valid-wins).
///
/// Record layout (after the [`OID_SEARCH_BYTES`] marker and the 2-byte big-endian outer length):
/// `MAGIC (32) || VERSION (1, 0x01) || LENGTH (u16 LE, 1..=64) || CHANNEL (ASCII [a-z0-9-])`.
///
/// Never panics; returns `None` on any malformation.
///
/// SECURITY: The tag is UNSIGNED, attacker-modifiable data — by construction it lives outside the
/// Authenticode hash. Treat it as untrusted input: a channel selector ONLY. Never place anything
/// trust-bearing there (feed URLs, keys, flags that gate trust/permissions). Validate the channel
/// charset strictly on read. Its only effect is which `releases.<channel>.json` the app polls;
/// every downloaded package is still signature/hash-verified as normal. This mirrors how Chrome
/// treats brand codes.
pub fn read_channel_from_signature(raw_sig: &[u8]) -> Option<String> {
    let mut last_valid: Option<String> = None;
    let mut pos = 0usize;

    while pos < raw_sig.len() {
        // find the next occurrence of the 15-byte DER marker
        let Some(rel) = raw_sig[pos..].windows(OID_SEARCH_BYTES.len()).position(|w| w == OID_SEARCH_BYTES) else {
            break;
        };
        let marker = pos + rel;
        // regardless of whether this record validates, resume scanning just past this marker
        pos = marker + 1;

        // 2 bytes big-endian outer length N (certificate_tag's own framing)
        let len_off = marker + OID_SEARCH_BYTES.len();
        let Some(n_bytes) = raw_sig.get(len_off..len_off + 2) else { continue };
        let n = u16::from_be_bytes([n_bytes[0], n_bytes[1]]) as usize;
        if n < RECORD_HEADER_LEN + 1 {
            continue;
        }
        let rec_off = len_off + 2;
        let Some(record) = raw_sig.get(rec_off..rec_off + n) else { continue };

        // MAGIC
        if record[0..32] != CHANNEL_TAG_MAGIC {
            continue;
        }
        // VERSION
        if record[32] != FORMAT_VERSION {
            continue;
        }
        // LENGTH (u16 little-endian; our field — the outer N above is big-endian and not ours)
        let l = u16::from_le_bytes([record[33], record[34]]) as usize;
        if !(1..=MAX_CHANNEL_LEN).contains(&l) || RECORD_HEADER_LEN + l > n {
            continue;
        }
        // CHANNEL charset
        let channel_bytes = &record[RECORD_HEADER_LEN..RECORD_HEADER_LEN + l];
        if !channel_bytes.iter().copied().all(is_valid_channel_byte) {
            continue;
        }

        // the charset is a subset of ASCII, so this cannot fail
        if let Ok(channel) = std::str::from_utf8(channel_bytes) {
            last_valid = Some(channel.to_string());
        }
    }

    last_valid
}

/// Rewrite the `<channel>` element in `{root_app_dir}/current/sq.version` to `channel`.
///
/// `channel` is validated against `^[a-z0-9-]{1,64}$` first; anything else is rejected. Returns an
/// error if the manifest or its `<channel>` element cannot be found. Never panics.
pub fn patch_installed_channel(root_app_dir: &Path, channel: &str) -> Result<()> {
    if !is_valid_channel(channel) {
        bail!("Refusing to patch invalid channel (must match ^[a-z0-9-]{{1,64}}$)");
    }

    let manifest_path = root_app_dir.join("current").join("sq.version");
    let xml = fs::read_to_string(&manifest_path).with_context(|| format!("Failed to read manifest at {:?}", manifest_path))?;

    const START_TAG: &str = "<channel>";
    const END_TAG: &str = "</channel>";
    let start = xml.find(START_TAG).with_context(|| format!("No <channel> element found in {:?}", manifest_path))?;
    let inner_start = start + START_TAG.len();
    let inner_len = xml[inner_start..].find(END_TAG).with_context(|| format!("Unterminated <channel> element in {:?}", manifest_path))?;

    let mut patched = String::with_capacity(xml.len() + channel.len());
    patched.push_str(&xml[..inner_start]);
    patched.push_str(channel);
    patched.push_str(&xml[inner_start + inner_len..]);

    if patched != xml {
        fs::write(&manifest_path, patched).with_context(|| format!("Failed to write patched manifest at {:?}", manifest_path))?;
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Golden vector from the contract: marker + BE outer length + record for channel "beta".
    /// Byte-for-byte shared with the C# mirror (`WindowsChannelTag.cs`) and the api worker tests.
    const GOLDEN_VECTOR_BETA: [u8; 56] = [
        // 15-byte DER marker (OID 1.3.6.1.4.1.11129.2.1.9999 + 04 82)
        0x06, 0x0b, 0x2b, 0x06, 0x01, 0x04, 0x01, 0xd6, 0x79, 0x02, 0x01, 0xce, 0x0f, 0x04, 0x82, //
        // outer length N = 39 (0x0027), big-endian
        0x00, 0x27, //
        // record: MAGIC (32 bytes)
        0x73, 0x12, 0x9e, 0x58, 0x64, 0xb5, 0x7b, 0x41, 0xfb, 0xca, 0xdb, 0x9d, 0x0b, 0xd5, 0x3f, 0x9d, //
        0x70, 0xb0, 0x23, 0x71, 0xe8, 0xc7, 0xfd, 0x6b, 0x7f, 0xfe, 0x30, 0x5f, 0x14, 0x47, 0x9e, 0x2f, //
        // VERSION = 0x01
        0x01, //
        // LENGTH = 4, u16 little-endian
        0x04, 0x00, //
        // CHANNEL = "beta"
        0x62, 0x65, 0x74, 0x61, //
    ];

    /// Build a full tag blob (marker + BE length + record) for the given record fields.
    fn build_tag(magic: &[u8], version: u8, length_field: u16, channel: &[u8], outer_len: Option<u16>) -> Vec<u8> {
        let mut record = Vec::new();
        record.extend_from_slice(magic);
        record.push(version);
        record.extend_from_slice(&length_field.to_le_bytes());
        record.extend_from_slice(channel);
        let n = outer_len.unwrap_or(record.len() as u16);
        let mut blob = OID_SEARCH_BYTES.to_vec();
        blob.extend_from_slice(&n.to_be_bytes());
        blob.extend_from_slice(&record);
        blob
    }

    fn valid_tag(channel: &str) -> Vec<u8> {
        build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, channel.len() as u16, channel.as_bytes(), None)
    }

    /// Wrap a tag in surrounding junk bytes, emulating its position inside a larger signature blob.
    fn embed(tag: &[u8]) -> Vec<u8> {
        let mut sig = vec![0x30, 0x82, 0x10, 0x00, 0xde, 0xad, 0xbe, 0xef];
        sig.extend_from_slice(tag);
        sig.extend_from_slice(&[0x05, 0x00, 0xa0, 0x03, 0x02, 0x01, 0x02]);
        sig
    }

    #[test]
    fn golden_vector_round_trips() {
        // the exact pinned bytes from the contract must parse to "beta"
        assert_eq!(read_channel_from_signature(&GOLDEN_VECTOR_BETA), Some("beta".to_string()));
        // and our builder must reproduce the pinned bytes exactly
        assert_eq!(valid_tag("beta"), GOLDEN_VECTOR_BETA.to_vec());
    }

    #[test]
    fn valid_tag_embedded_in_junk() {
        assert_eq!(read_channel_from_signature(&embed(&valid_tag("stable-2"))), Some("stable-2".to_string()));
    }

    #[test]
    fn max_length_channel_is_accepted() {
        let channel = "a".repeat(MAX_CHANNEL_LEN);
        assert_eq!(read_channel_from_signature(&embed(&valid_tag(&channel))), Some(channel));
    }

    #[test]
    fn empty_input_returns_none() {
        assert_eq!(read_channel_from_signature(&[]), None);
    }

    #[test]
    fn absent_marker_returns_none() {
        let sig = vec![0xffu8; 4096];
        assert_eq!(read_channel_from_signature(&sig), None);
    }

    #[test]
    fn wrong_oid_returns_none() {
        let mut tag = valid_tag("beta");
        tag[7] = 0xd7; // corrupt one OID byte
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn truncated_outer_length_returns_none() {
        // marker present but fewer than 2 length bytes follow
        let mut sig = OID_SEARCH_BYTES.to_vec();
        sig.push(0x00);
        assert_eq!(read_channel_from_signature(&sig), None);
    }

    #[test]
    fn outer_length_exceeding_remaining_returns_none() {
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 4, b"beta", Some(1000));
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn outer_length_too_small_returns_none() {
        // N < 36 can never hold a record
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 4, b"beta", Some(35));
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn wrong_magic_returns_none() {
        let mut magic = CHANNEL_TAG_MAGIC;
        magic[0] ^= 0xff;
        let tag = build_tag(&magic, FORMAT_VERSION, 4, b"beta", None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn wrong_version_returns_none() {
        let tag = build_tag(&CHANNEL_TAG_MAGIC, 0x02, 4, b"beta", None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn zero_length_channel_returns_none() {
        // LENGTH = 0 is out of range, even if the outer length allows a record
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 0, b"x", None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn oversized_length_channel_returns_none() {
        let channel = "a".repeat(65);
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 65, channel.as_bytes(), None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn length_exceeding_record_returns_none() {
        // LENGTH claims more channel bytes than the outer record contains
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 10, b"beta", None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn uppercase_channel_returns_none() {
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 4, b"Beta", None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn channel_with_space_returns_none() {
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 6, b"be ta ", None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn non_ascii_channel_returns_none() {
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 5, &[0x62, 0x65, 0x74, 0xc3, 0xa4], None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), None);
    }

    #[test]
    fn multiple_valid_tags_last_wins() {
        let mut sig = embed(&valid_tag("alpha"));
        sig.extend_from_slice(&embed(&valid_tag("beta")));
        assert_eq!(read_channel_from_signature(&sig), Some("beta".to_string()));
    }

    #[test]
    fn invalid_tag_then_valid_tag_returns_valid() {
        let bad = build_tag(&CHANNEL_TAG_MAGIC, 0x7f, 4, b"nope", None);
        let mut sig = embed(&bad);
        sig.extend_from_slice(&embed(&valid_tag("stable")));
        assert_eq!(read_channel_from_signature(&sig), Some("stable".to_string()));
    }

    #[test]
    fn valid_tag_then_invalid_tag_returns_valid() {
        let bad = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 4, b"NOPE", None);
        let mut sig = embed(&valid_tag("stable"));
        sig.extend_from_slice(&embed(&bad));
        assert_eq!(read_channel_from_signature(&sig), Some("stable".to_string()));
    }

    #[test]
    fn trailing_bytes_after_record_are_ignored() {
        // outer N larger than 35+L is allowed as long as the declared channel validates
        let mut record_channel = b"beta".to_vec();
        record_channel.extend_from_slice(&[0x00, 0x00]); // padding inside the record
        let tag = build_tag(&CHANNEL_TAG_MAGIC, FORMAT_VERSION, 4, &record_channel, None);
        assert_eq!(read_channel_from_signature(&embed(&tag)), Some("beta".to_string()));
    }

    // ---- patch_installed_channel ----

    const SQ_VERSION_FIXTURE: &str = r#"<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
<metadata>
<id>MyTestApp</id>
<title>MyTestApp</title>
<description>MyTestApp</description>
<authors>MyTestApp</authors>
<version>1.0.11</version>
<channel>win</channel>
<mainExe>MyTestApp.exe</mainExe>
<os>win</os>
<rid>win-x64</rid>
</metadata>
</package>"#;

    fn make_install_fixture() -> (tempfile::TempDir, std::path::PathBuf) {
        let tmp = tempfile::tempdir().unwrap();
        let current = tmp.path().join("current");
        fs::create_dir_all(&current).unwrap();
        let manifest = current.join("sq.version");
        fs::write(&manifest, SQ_VERSION_FIXTURE).unwrap();
        (tmp, manifest)
    }

    #[test]
    fn patch_rewrites_channel_element_only() {
        let (tmp, manifest) = make_install_fixture();
        patch_installed_channel(tmp.path(), "beta").unwrap();
        let patched = fs::read_to_string(&manifest).unwrap();
        assert_eq!(patched, SQ_VERSION_FIXTURE.replace("<channel>win</channel>", "<channel>beta</channel>"));
    }

    #[test]
    fn patch_is_idempotent_for_same_channel() {
        let (tmp, manifest) = make_install_fixture();
        patch_installed_channel(tmp.path(), "win").unwrap();
        assert_eq!(fs::read_to_string(&manifest).unwrap(), SQ_VERSION_FIXTURE);
    }

    #[test]
    fn patch_rejects_invalid_channels() {
        let (tmp, manifest) = make_install_fixture();
        for bad in ["", "Beta", "be ta", "beta\n", "../evil", "b\\eta", "b/eta", &"a".repeat(65), "bét"] {
            assert!(patch_installed_channel(tmp.path(), bad).is_err(), "channel {:?} should be rejected", bad);
        }
        // file untouched
        assert_eq!(fs::read_to_string(&manifest).unwrap(), SQ_VERSION_FIXTURE);
    }

    #[test]
    fn patch_errors_when_manifest_missing() {
        let tmp = tempfile::tempdir().unwrap();
        assert!(patch_installed_channel(tmp.path(), "beta").is_err());
    }

    #[test]
    fn patch_errors_when_channel_element_missing() {
        let tmp = tempfile::tempdir().unwrap();
        let current = tmp.path().join("current");
        fs::create_dir_all(&current).unwrap();
        fs::write(current.join("sq.version"), "<package><metadata><id>x</id></metadata></package>").unwrap();
        assert!(patch_installed_channel(tmp.path(), "beta").is_err());
    }
}
