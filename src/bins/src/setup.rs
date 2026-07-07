#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(dead_code)]

#[macro_use]
extern crate log;

use anyhow::Result;
use clap::{arg, value_parser, Command};
use memmap2::Mmap;
use std::ffi::OsString;
use std::fs::File;
use std::{
    env,
    path::{Path, PathBuf},
};
use velopack_bins::*;

#[used]
#[no_mangle]
static BUNDLE_PLACEHOLDER: [u8; 48] = [
    0, 0, 0, 0, 0, 0, 0, 0, // 8 bytes for package offset
    0, 0, 0, 0, 0, 0, 0, 0, // 8 bytes for package length
    0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29, // 32 bytes for bundle signature
    0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4, //
    0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5, //
    0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d, //
];

#[inline(never)]
pub fn header_offset_and_length() -> (i64, i64) {
    use core::ptr;
    // Perform volatile reads to avoid optimization issues
    // TODO: refactor to use little-endian, also need to update the writer in dotnet
    unsafe {
        let offset = i64::from_ne_bytes([
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[0]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[1]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[2]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[3]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[4]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[5]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[6]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[7]),
        ]);
        let length = i64::from_ne_bytes([
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[8]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[9]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[10]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[11]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[12]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[13]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[14]),
            ptr::read_volatile(&BUNDLE_PLACEHOLDER[15]),
        ]);
        (offset, length)
    }
}

fn main() {
    windows::mitigate::pre_main_sideload_mitigation();
    windows::splash::init_dpi_awareness();
    let result = dialogs::XDialogBuilder::new().run_result(real_main);
    std::process::exit(if result.is_ok() { 0 } else { 1 });
}

fn real_main() -> Result<()> {
    dialogs::init();
    if let Err(e) = main_inner() {
        // The command parser uses `ignore_errors(true)`, so clap won't print --help / --version
        // itself; those requests arrive here as an error which we render manually. Setup is
        // user-facing, so genuine errors are also surfaced in a dialog.
        if let Some(clap_err) = e.downcast_ref::<clap::Error>() {
            use clap::error::ErrorKind;
            if matches!(
                clap_err.kind(),
                ErrorKind::DisplayHelp | ErrorKind::DisplayHelpOnMissingArgumentOrSubcommand | ErrorKind::DisplayVersion
            ) {
                println!("{clap_err}");
                return Ok(());
            }
        }
        let error_string = e.to_string();
        error!("An error has occurred: {:?}", e);
        if let Some(setup_err) = e.downcast_ref::<setup_errors::SetupError>() {
            let body = setup_err.localized_body();
            match setup_err.app_title() {
                Some(app_title) => dialogs::show_setup_error(app_title, &body),
                None => dialogs::show_generic_error("Setup", &body),
            }
        } else {
            dialogs::show_generic_error("Setup", &error_string);
        }
        return Err(e);
    }
    Ok(())
}

fn main_inner() -> Result<()> {
    #[rustfmt::skip]
    let mut arg_config = Command::new("Setup")
        .about(format!("Velopack Setup ({}) installs applications.\nhttps://velopack.io", env!("NGBV_VERSION")))
        .arg(arg!(-s --silent "Hides all dialogs and answers 'yes' to all prompts"))
        .arg(arg!(-v --verbose "Print debug messages to console"))
        .arg(arg!(-l --log <FILE> "Enable file logging and set location").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!(-t --installto <DIR> "Installation directory to install the application").required(false).value_parser(value_parser!(PathBuf)))
        .arg(arg!([EXE_ARGS] "Arguments to pass to the started executable. Must be preceded by '--'.").required(false).last(true).num_args(0..))
        .ignore_errors(true);

    if cfg!(debug_assertions) {
        arg_config = arg_config.arg(
            arg!(-d --debug <FILE> "Debug mode, install from a nupkg file")
                .required(false)
                .value_parser(value_parser!(PathBuf)),
        );
    }

    let matches = arg_config.try_get_matches()?;

    let silent = matches.get_flag("silent");
    dialogs::set_silent(silent);
    if !silent {
        dialogs::set_dialog_timeout(Some(std::time::Duration::from_secs(300)));
    }

    let verbose = matches.get_flag("verbose");
    let logfile = matches.get_one::<PathBuf>("log");
    let desired_log_file = logfile
        .cloned()
        .unwrap_or(velopack::logging::default_logfile_path(velopack::logging::NoLocator));
    velopack::logging::init_logging("setup", Some(&desired_log_file), true, verbose, None);

    let debug = matches.get_one::<PathBuf>("debug");
    let install_to = matches.get_one::<PathBuf>("installto");
    let exe_args = matches.get_many::<OsString>("EXE_ARGS").map(|v| v.map(|f| f.to_os_string()).collect());

    info!("Starting Velopack Setup ({})", env!("NGBV_VERSION"));
    info!("    Location: {:?}", env::current_exe()?);
    info!("    Silent: {}", silent);
    info!("    Verbose: {}", verbose);
    info!("    Log: {:?}", desired_log_file);
    info!("    Install To: {:?}", install_to);
    if cfg!(debug_assertions) {
        info!("    Debug: {:?}", debug);
    }

    // change working directory to the containing directory of the exe
    let mut containing_dir = env::current_exe()?;
    containing_dir.pop();
    env::set_current_dir(containing_dir)?;

    // load the bundle which is embedded or if missing from the debug nupkg path
    let osinfo = os_info::get();
    let osarch = shared::runtime_arch::RuntimeArch::from_current_system();
    info!("OS: {osinfo}, Arch={osarch:#?}");

    if !windows::is_windows_7_sp1_or_greater() {
        return Err(setup_errors::SetupError::WindowsVersionUnsupported.into());
    }

    // in debug mode only, allow a nupkg to be passed in as the first argument
    if cfg!(debug_assertions) {
        if let Some(pkg) = debug {
            info!("Loading bundle from DEBUG nupkg file {:?}...", pkg);
            let mut bundle = velopack::bundle::load_bundle_from_file(pkg)?;
            if let Some(root_dir) = commands::install(&mut bundle, install_to, exe_args)? {
                match File::open(env::current_exe()?).and_then(|f| unsafe { Mmap::map(&f) }) {
                    Ok(mmap) => apply_signature_channel_override(&mmap, &root_dir),
                    Err(e) => warn!("Failed to map own executable to look for a channel override (non-fatal): {}", e),
                }
            }
            return Ok(());
        }
    }

    info!("Reading bundle header...");
    let (offset, length) = header_offset_and_length();
    info!("Bundle offset = {}, length = {}", offset, length);

    // try to load the bundle from embedded zip
    if offset > 0 && length > 0 {
        info!("Loading bundle from embedded zip...");
        let file = File::open(env::current_exe()?)?;
        let mmap = unsafe { Mmap::map(&file)? };
        let zip_range: &[u8] = &mmap[offset as usize..(offset + length) as usize];
        let mut bundle = velopack::bundle::load_bundle_from_memory(zip_range)?;
        if let Some(root_dir) = commands::install(&mut bundle, install_to, exe_args)? {
            // the mmap covers our whole exe, so reuse it to read our own Authenticode signature
            apply_signature_channel_override(&mmap, &root_dir);
        }
        return Ok(());
    }

    Err(setup_errors::SetupError::EmbeddedZipMissing.into())
}

/// Looks for a promoted-channel tag in our own Authenticode signature and, if present, patches the
/// `<channel>` in the freshly-installed `current\sq.version`. An absent or malformed tag must
/// never fault the install — all errors are logged and swallowed.
///
/// SECURITY: The tag is UNSIGNED, attacker-modifiable data — by construction it lives outside the
/// Authenticode hash. Treat it as untrusted input: a channel selector ONLY. Never place anything
/// trust-bearing there (feed URLs, keys, flags that gate trust/permissions). Validate the channel
/// charset strictly on read. Its only effect is which `releases.<channel>.json` the app polls;
/// every downloaded package is still signature/hash-verified as normal. This mirrors how Chrome
/// treats brand codes.
fn apply_signature_channel_override(exe_bytes: &[u8], root_app_dir: &Path) {
    use velopack::windows_channel_tag;

    // if several certificate entries carry valid tags, the last valid one wins (matching the
    // last-valid-wins rule inside a single signature blob)
    let mut channel: Option<String> = None;
    for cert in pe_attribute_certificates(exe_bytes) {
        if let Some(c) = windows_channel_tag::read_channel_from_signature(cert) {
            channel = Some(c);
        }
    }

    let Some(channel) = channel else {
        info!("No channel override tag found in installer signature.");
        return;
    };

    info!("Installer signature carries a channel override: '{}'. Patching installed manifest...", channel);
    match windows_channel_tag::patch_installed_channel(root_app_dir, &channel) {
        Ok(()) => info!("Installed channel patched to '{}'.", channel),
        Err(e) => warn!("Failed to apply channel override '{}' (non-fatal): {}", channel, e),
    }
}

/// Parses the attribute certificate table (`IMAGE_DIRECTORY_ENTRY_SECURITY`, data directory
/// index 4) out of a raw PE image and returns each `WIN_CERTIFICATE` `bCertificate` payload
/// (the raw Authenticode PKCS#7 blob). Returns an empty vec on any malformation; never panics.
fn pe_attribute_certificates(pe: &[u8]) -> Vec<&[u8]> {
    fn read_u16(pe: &[u8], off: usize) -> Option<u16> {
        let end = off.checked_add(2)?;
        Some(u16::from_le_bytes(pe.get(off..end)?.try_into().ok()?))
    }
    fn read_u32(pe: &[u8], off: usize) -> Option<u32> {
        let end = off.checked_add(4)?;
        Some(u32::from_le_bytes(pe.get(off..end)?.try_into().ok()?))
    }

    /// Returns (file_offset, size) of the attribute certificate table.
    fn security_directory(pe: &[u8]) -> Option<(usize, usize)> {
        if pe.get(0..2)? != b"MZ" {
            return None;
        }
        let e_lfanew = read_u32(pe, 0x3c)? as usize;
        if pe.get(e_lfanew..e_lfanew.checked_add(4)?)? != b"PE\0\0" {
            return None;
        }
        let coff = e_lfanew.checked_add(4)?;
        let opt = coff.checked_add(20)?;
        let (num_dirs_off, dirs_off) = match read_u16(pe, opt)? {
            0x10b => (opt.checked_add(92)?, opt.checked_add(96)?),   // PE32
            0x20b => (opt.checked_add(108)?, opt.checked_add(112)?), // PE32+
            _ => return None,
        };
        // the security directory is data directory index 4
        if read_u32(pe, num_dirs_off)? < 5 {
            return None;
        }
        let entry = dirs_off.checked_add(4 * 8)?;
        // uniquely for this directory, VirtualAddress is a FILE offset, not an RVA
        let table_offset = read_u32(pe, entry)? as usize;
        let table_size = read_u32(pe, entry.checked_add(4)?)? as usize;
        if table_offset == 0 || table_size == 0 {
            return None;
        }
        // whole table must be inside the file
        pe.get(table_offset..table_offset.checked_add(table_size)?)?;
        Some((table_offset, table_size))
    }

    let mut certs: Vec<&[u8]> = Vec::new();
    let Some((table_offset, table_size)) = security_directory(pe) else {
        return certs;
    };

    // WIN_CERTIFICATE: dwLength (u32 LE, includes this 8-byte header), wRevision (u16),
    // wCertificateType (u16), bCertificate[dwLength - 8]; entries are 8-byte aligned.
    let table_end = table_offset + table_size;
    let mut cur = table_offset;
    while cur.checked_add(8).is_some_and(|hdr_end| hdr_end <= table_end) {
        let Some(dw_length) = read_u32(pe, cur) else { break };
        let dw_length = dw_length as usize;
        let Some(entry_end) = cur.checked_add(dw_length) else { break };
        if dw_length < 8 || entry_end > table_end {
            break;
        }
        if let Some(payload) = pe.get(cur + 8..entry_end) {
            certs.push(payload);
        }
        // advance to the next 8-byte-aligned entry
        let Some(next) = entry_end.checked_add(7) else { break };
        let next = next & !7usize;
        if next <= cur {
            break;
        }
        cur = next;
    }
    certs
}

#[cfg(test)]
mod tests {
    use super::pe_attribute_certificates;

    /// The pinned golden vector from CONTRACTS_windows_installer.md — marker + BE outer length +
    /// record (MAGIC || 0x01 || u16le(4) || "beta"). Must match the lib-rust + C# fixtures.
    const GOLDEN_VECTOR_BETA: [u8; 56] = [
        0x06, 0x0b, 0x2b, 0x06, 0x01, 0x04, 0x01, 0xd6, 0x79, 0x02, 0x01, 0xce, 0x0f, 0x04, 0x82, //
        0x00, 0x27, //
        0x73, 0x12, 0x9e, 0x58, 0x64, 0xb5, 0x7b, 0x41, 0xfb, 0xca, 0xdb, 0x9d, 0x0b, 0xd5, 0x3f, 0x9d, //
        0x70, 0xb0, 0x23, 0x71, 0xe8, 0xc7, 0xfd, 0x6b, 0x7f, 0xfe, 0x30, 0x5f, 0x14, 0x47, 0x9e, 0x2f, //
        0x01, 0x04, 0x00, 0x62, 0x65, 0x74, 0x61, //
    ];

    /// Builds a minimal-but-valid PE32+ image with an attribute certificate table containing a
    /// single WIN_CERTIFICATE whose payload is `sig`.
    fn build_test_pe(sig: &[u8]) -> Vec<u8> {
        let e_lfanew = 0x80usize;
        let mut pe = vec![0u8; e_lfanew];
        pe[0] = b'M';
        pe[1] = b'Z';
        pe[0x3c..0x40].copy_from_slice(&(e_lfanew as u32).to_le_bytes());

        pe.extend_from_slice(b"PE\0\0");
        let coff = pe.len();
        pe.extend_from_slice(&[0u8; 20]);
        pe[coff + 16..coff + 18].copy_from_slice(&240u16.to_le_bytes()); // SizeOfOptionalHeader (PE32+)

        let opt = pe.len();
        pe.extend_from_slice(&[0u8; 240]);
        pe[opt..opt + 2].copy_from_slice(&0x20bu16.to_le_bytes()); // PE32+ magic
        pe[opt + 108..opt + 112].copy_from_slice(&16u32.to_le_bytes()); // NumberOfRvaAndSizes

        while pe.len() % 8 != 0 {
            pe.push(0);
        }
        let table_offset = pe.len();
        let dw_length = 8 + sig.len();
        pe.extend_from_slice(&(dw_length as u32).to_le_bytes());
        pe.extend_from_slice(&0x0200u16.to_le_bytes()); // WIN_CERT_REVISION_2_0
        pe.extend_from_slice(&0x0002u16.to_le_bytes()); // WIN_CERT_TYPE_PKCS_SIGNED_DATA
        pe.extend_from_slice(sig);
        while pe.len() % 8 != 0 {
            pe.push(0);
        }
        let table_size = pe.len() - table_offset;

        // data directory index 4 (security): file offset + size
        let entry = opt + 112 + 4 * 8;
        pe[entry..entry + 4].copy_from_slice(&(table_offset as u32).to_le_bytes());
        pe[entry + 4..entry + 8].copy_from_slice(&(table_size as u32).to_le_bytes());
        pe
    }

    #[test]
    fn extracts_certificate_payload_from_pe() {
        let mut sig = vec![0x30, 0x82, 0xde, 0xad]; // junk prefix, like a real PKCS#7 blob
        sig.extend_from_slice(&GOLDEN_VECTOR_BETA);
        sig.extend_from_slice(&[0x00, 0x01, 0x02]);
        let pe = build_test_pe(&sig);

        let certs = pe_attribute_certificates(&pe);
        assert_eq!(certs.len(), 1);
        assert_eq!(certs[0], sig.as_slice());
    }

    #[test]
    fn golden_vector_channel_read_from_pe_signature() {
        let mut sig = vec![0x30, 0x82, 0x01, 0x00];
        sig.extend_from_slice(&GOLDEN_VECTOR_BETA);
        let pe = build_test_pe(&sig);

        let mut channel = None;
        for cert in pe_attribute_certificates(&pe) {
            if let Some(c) = velopack::windows_channel_tag::read_channel_from_signature(cert) {
                channel = Some(c);
            }
        }
        assert_eq!(channel.as_deref(), Some("beta"));
    }

    #[test]
    fn unsigned_pe_yields_no_certificates() {
        // valid PE headers but a zeroed security directory
        let mut pe = build_test_pe(&[0u8; 16]);
        let opt = 0x80 + 4 + 20;
        let entry = opt + 112 + 4 * 8;
        pe[entry..entry + 8].copy_from_slice(&[0u8; 8]);
        assert!(pe_attribute_certificates(&pe).is_empty());
    }

    #[test]
    fn garbage_bytes_yield_no_certificates() {
        assert!(pe_attribute_certificates(&[]).is_empty());
        assert!(pe_attribute_certificates(&[0x4d]).is_empty());
        assert!(pe_attribute_certificates(&vec![0xa5u8; 4096]).is_empty());
        // MZ but bogus e_lfanew
        let mut pe = vec![0u8; 64];
        pe[0] = b'M';
        pe[1] = b'Z';
        pe[0x3c..0x40].copy_from_slice(&u32::MAX.to_le_bytes());
        assert!(pe_attribute_certificates(&pe).is_empty());
    }

    #[test]
    fn truncated_certificate_table_is_non_fatal() {
        let sig = vec![0xaau8; 40];
        let mut pe = build_test_pe(&sig);
        // lie about the table size so it extends past EOF
        let opt = 0x80 + 4 + 20;
        let entry = opt + 112 + 4 * 8;
        pe[entry + 4..entry + 8].copy_from_slice(&0xffff_0000u32.to_le_bytes());
        assert!(pe_attribute_certificates(&pe).is_empty());
    }
}
