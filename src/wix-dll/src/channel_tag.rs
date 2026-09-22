//! MSI-side reader for the Windows installer channel tag.
//!
//! In an MSI, the Authenticode signature lives in the `\x05DigitalSignature` compound-file
//! stream. This module extracts that stream's raw bytes with the pure-Rust `cfb` crate and hands
//! them to the shared parser/patcher in `velopack::windows_channel_tag` — the single source of
//! truth for the tag format. The Setup.exe stub does the same with the PE attribute certificate
//! table.

use std::io::Read;
use std::path::Path;

use anyhow::{bail, Context, Result};
use velopack::windows_channel_tag::{patch_installed_channel, read_channel_from_signature};

/// The compound-file stream holding the MSI's raw Authenticode `SignedData` blob. The leading
/// `\x05` byte marks it as a control stream (like `\x05SummaryInformation`); it is stored
/// literally, exempt from MSI's usual stream-name mangling.
const DIGITAL_SIGNATURE_STREAM: &str = "\u{5}DigitalSignature";

/// Sanity cap on the signature stream size. Real Authenticode blobs are tens of KB even with
/// timestamps and the ~1 KB dummy tag certificate; anything larger indicates a corrupt or
/// malicious directory entry and is not worth buffering.
const MAX_SIGNATURE_STREAM_LEN: u64 = 16 * 1024 * 1024;

/// Reads the raw Authenticode signature bytes from the MSI at `msi_path`.
///
/// Returns `Ok(None)` when the MSI is unsigned (no `\x05DigitalSignature` stream) — an unsigned
/// MSI simply carries no tag. Errors only on I/O or a malformed compound file; callers must treat
/// any error as "no override" and never fault the install.
pub fn read_msi_signature(msi_path: &Path) -> Result<Option<Vec<u8>>> {
    let mut compound = cfb::open(msi_path).with_context(|| format!("Failed to open MSI compound file at {:?}", msi_path))?;
    if !compound.exists(DIGITAL_SIGNATURE_STREAM) {
        // unsigned MSI — nothing to tag, nothing to read
        return Ok(None);
    }
    let mut stream = compound
        .open_stream(DIGITAL_SIGNATURE_STREAM)
        .with_context(|| format!("Failed to open DigitalSignature stream in {:?}", msi_path))?;
    if stream.len() > MAX_SIGNATURE_STREAM_LEN {
        bail!("DigitalSignature stream in {:?} is implausibly large ({} bytes)", msi_path, stream.len());
    }
    let mut raw_sig = Vec::with_capacity(stream.len() as usize);
    stream
        .read_to_end(&mut raw_sig)
        .with_context(|| format!("Failed to read DigitalSignature stream from {:?}", msi_path))?;
    Ok(Some(raw_sig))
}

/// Reads the channel tag from the MSI's own Authenticode signature and, if a valid tag is
/// present, patches `<channel>` in `{install_folder}\current\sq.version`.
///
/// Returns `Ok(Some(channel))` when a patch was applied, `Ok(None)` when the MSI is unsigned or
/// carries no valid tag (the common case — nothing to do). The caller (`PatchChannelDeferred`)
/// logs and swallows all errors; this must never fault the install.
pub fn apply_msi_channel_override(msi_path: &Path, install_folder: &Path) -> Result<Option<String>> {
    let Some(raw_sig) = read_msi_signature(msi_path)? else {
        return Ok(None);
    };
    let Some(channel) = read_channel_from_signature(&raw_sig) else {
        return Ok(None);
    };
    patch_installed_channel(install_folder, &channel)?;
    Ok(Some(channel))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use velopack::windows_channel_tag::test_support::{valid_tag, write_install_fixture, SQ_VERSION_FIXTURE};

    /// Emulates a PKCS#7 SignedData blob: DER-ish junk surrounding the tag certificate bytes.
    fn fake_signature(tag: Option<&[u8]>) -> Vec<u8> {
        let mut sig = vec![0x30, 0x82, 0x20, 0x00, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x07, 0x02];
        sig.extend_from_slice(&[0xa5; 512]); // filler "certificates"
        if let Some(tag) = tag {
            sig.extend_from_slice(tag);
        }
        sig.extend_from_slice(&[0x5a; 256]); // trailing filler
        sig
    }

    /// Creates a synthetic MSI-shaped compound file. Real MSIs are CFB containers; the
    /// `\x05DigitalSignature` stream (when present) holds the raw SignedData bytes.
    fn write_synthetic_msi(path: &Path, signature: Option<&[u8]>) {
        use std::io::Write;
        let mut compound = cfb::create(path).unwrap();
        {
            // an unrelated stream, so the signature stream is not the only directory entry
            let mut s = compound.create_stream("\u{5}SummaryInformation").unwrap();
            s.write_all(&[0u8; 48]).unwrap();
        }
        if let Some(signature) = signature {
            let mut s = compound.create_stream(DIGITAL_SIGNATURE_STREAM).unwrap();
            s.write_all(signature).unwrap();
        }
        compound.flush().unwrap();
    }

    #[test]
    fn signature_stream_bytes_round_trip() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        let sig = fake_signature(Some(&valid_tag("beta")));
        write_synthetic_msi(&msi, Some(&sig));
        assert_eq!(read_msi_signature(&msi).unwrap(), Some(sig));
    }

    #[test]
    fn unsigned_msi_returns_none() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        write_synthetic_msi(&msi, None);
        assert_eq!(read_msi_signature(&msi).unwrap(), None);
    }

    #[test]
    fn non_cfb_file_errors() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("garbage.msi");
        fs::write(&msi, b"this is not a compound file").unwrap();
        assert!(read_msi_signature(&msi).is_err());
    }

    #[test]
    fn missing_file_errors() {
        let tmp = tempfile::tempdir().unwrap();
        assert!(read_msi_signature(&tmp.path().join("nope.msi")).is_err());
    }

    #[test]
    fn tagged_msi_patches_installed_channel() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        write_synthetic_msi(&msi, Some(&fake_signature(Some(&valid_tag("staging")))));
        let install = tmp.path().join("install");
        let manifest = write_install_fixture(&install);

        let result = apply_msi_channel_override(&msi, &install).unwrap();
        assert_eq!(result, Some("staging".to_string()));
        let patched = fs::read_to_string(&manifest).unwrap();
        assert_eq!(
            patched,
            SQ_VERSION_FIXTURE.replace("<channel>win</channel>", "<channel>staging</channel>")
        );
    }

    #[test]
    fn signed_msi_without_tag_is_a_noop() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        write_synthetic_msi(&msi, Some(&fake_signature(None)));
        let install = tmp.path().join("install");
        let manifest = write_install_fixture(&install);

        assert_eq!(apply_msi_channel_override(&msi, &install).unwrap(), None);
        assert_eq!(fs::read_to_string(&manifest).unwrap(), SQ_VERSION_FIXTURE);
    }

    #[test]
    fn unsigned_msi_is_a_noop() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        write_synthetic_msi(&msi, None);
        let install = tmp.path().join("install");
        let manifest = write_install_fixture(&install);

        assert_eq!(apply_msi_channel_override(&msi, &install).unwrap(), None);
        assert_eq!(fs::read_to_string(&manifest).unwrap(), SQ_VERSION_FIXTURE);
    }

    #[test]
    fn multiple_tags_last_valid_wins_through_msi_path() {
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        let mut sig = fake_signature(Some(&valid_tag("alpha")));
        sig.extend_from_slice(&valid_tag("prod"));
        write_synthetic_msi(&msi, Some(&sig));
        let install = tmp.path().join("install");
        write_install_fixture(&install);

        assert_eq!(apply_msi_channel_override(&msi, &install).unwrap(), Some("prod".to_string()));
    }

    #[test]
    fn tagged_msi_with_missing_manifest_errors_without_faulting() {
        // patch_installed_channel errors when sq.version is absent; PatchChannelDeferred logs and
        // swallows this — the install must still succeed.
        let tmp = tempfile::tempdir().unwrap();
        let msi = tmp.path().join("app.msi");
        write_synthetic_msi(&msi, Some(&fake_signature(Some(&valid_tag("beta")))));
        let install = tmp.path().join("install"); // no current/sq.version
        assert!(apply_msi_channel_override(&msi, &install).is_err());
    }
}
