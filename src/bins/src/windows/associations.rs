//! Registration of file associations and custom URL protocols under `Software\Classes`: in HKCU for
//! Setup.exe and per-user MSI installs, in HKLM for per-machine MSI installs (see [`Hive`]).
//!
//! Every key this module creates (a file ProgId key or a URL protocol key) carries a string value
//! `VelopackRootDir` holding the install root. Keys are only ever removed, or modified for removal,
//! when that marker matches our own root dir, so we never destroy registrations owned by another
//! app or by another install of the same app.
//!
//! The core functions take the `Classes` base key as a parameter so tests can run against a scratch key.
//! Failures are logged as warnings: association problems must never fail install/apply/uninstall.

use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};
use velopack::{constants, locator::VelopackLocator};
use windows::Win32::UI::Shell::{SHChangeNotify, SHCNE_ASSOCCHANGED, SHCNF_IDLIST};
use winreg::{enums::*, types::FromRegValue, RegKey, RegValue};

use super::registry::{self, MsiScope};

const CLASSES_KEY: &str = "Software\\Classes";
const ROOT_DIR_VALUE: &str = "VelopackRootDir";
const OPEN_WITH_PROGIDS: &str = "OpenWithProgIds";
const SHELL_OPEN_COMMAND: &str = "shell\\open\\command";
const PROGID_MAX_LEN: usize = 39;

/// Everything needed to (un)register an app's associations, extracted from a locator.
#[derive(Debug, Clone, PartialEq)]
pub struct AppRegistration {
    pub app_id: String,
    pub app_title: String,
    pub root_dir: PathBuf,
    pub main_exe: PathBuf,
    pub file_associations: Vec<String>,
    pub url_protocols: Vec<String>,
}

impl AppRegistration {
    pub fn from_locator(locator: &VelopackLocator) -> Self {
        let app_id = locator.get_manifest_id();
        let mut app_title = locator.get_manifest_title();
        if app_title.trim().is_empty() {
            app_title = app_id.clone();
        }
        Self {
            app_id,
            app_title,
            root_dir: locator.get_root_dir(),
            main_exe: locator.get_main_exe_path(),
            file_associations: locator.get_manifest_file_associations(),
            url_protocols: locator.get_manifest_url_protocols(),
        }
    }

    fn is_empty(&self) -> bool {
        self.file_associations.is_empty() && self.url_protocols.is_empty()
    }
}

/// The registry hive an install registers its associations in.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Hive {
    /// `HKCU\Software\Classes`: Setup.exe installs and per-user MSI installs.
    CurrentUser,
    /// `HKLM\Software\Classes`: per-machine MSI installs, so the registrations exist for every user and can be
    /// made by a SYSTEM deployment (Intune/SCCM) where HKCU is meaningless. Needs an elevated process.
    LocalMachine,
}

impl Hive {
    fn name(self) -> &'static str {
        match self {
            Hive::CurrentUser => "HKCU",
            Hive::LocalMachine => "HKLM",
        }
    }

    fn open_classes(self) -> Result<RegKey> {
        let root = match self {
            Hive::CurrentUser => HKEY_CURRENT_USER,
            Hive::LocalMachine => HKEY_LOCAL_MACHINE,
        };
        let (classes, _) = RegKey::predef(root).create_subkey(CLASSES_KEY)?;
        Ok(classes)
    }

    /// The machine-wide classes (read-only), consulted when registering per-user so a per-user `.ext` default
    /// never shadows an existing system default handler. `None` when writing to HKLM itself (only HKLM matters
    /// then) or when it cannot be opened, in which case only the target hive is consulted.
    fn machine_classes_for_default_check(self) -> Option<RegKey> {
        match self {
            Hive::CurrentUser => RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey_with_flags(CLASSES_KEY, KEY_READ).ok(),
            Hive::LocalMachine => None,
        }
    }
}

/// Where a (non-portable) install's associations live: HKCU, except for a per-machine MSI install. An MSI
/// install's scope is read from the hive holding its ARP entry; when that is missing (deleted by hand) we
/// fall back to HKCU, which is at least harmless when wrong.
fn hive_for(locator: &VelopackLocator) -> Hive {
    if !locator.get_is_msi_install() {
        return Hive::CurrentUser;
    }
    match registry::get_msi_install_scope(locator) {
        Some(MsiScope::PerMachine) => Hive::LocalMachine,
        Some(MsiScope::PerUser) => Hive::CurrentUser,
        None => {
            warn!("Could not determine the MSI install scope (no ARP entry found), assuming a per-user install.");
            Hive::CurrentUser
        }
    }
}

/// Writes all of `next`'s associations (idempotent, also repairs a renamed main exe) and removes the
/// ones `prev` had which `next` no longer declares. Skipped for portable installs.
pub fn sync(next: &VelopackLocator, prev: Option<&VelopackLocator>) {
    if next.get_is_portable() {
        info!("Skipping file/URL association registration for portable app.");
        return;
    }
    sync_in_hive(next, prev, hive_for(next));
}

/// [`sync`] into an explicitly chosen hive (the MSI custom actions know their scope from the installer).
pub fn sync_in_hive(next: &VelopackLocator, prev: Option<&VelopackLocator>, hive: Hive) {
    let next_reg = AppRegistration::from_locator(next);
    let prev_reg = prev.map(AppRegistration::from_locator);
    if next_reg.is_empty() && prev_reg.as_ref().map_or(true, |p| p.is_empty()) {
        return;
    }
    info!("Registering file/URL associations in {}...", hive.name());
    match hive.open_classes() {
        Ok(classes) => {
            let machine_classes = hive.machine_classes_for_default_check();
            if sync_in(&classes, machine_classes.as_ref(), &next_reg, prev_reg.as_ref()) {
                notify_assoc_changed();
            }
        }
        Err(e) => warn!(
            "Failed to open {}\\{} ({}), associations were not registered.",
            hive.name(),
            CLASSES_KEY,
            e
        ),
    }
}

/// Removes every association owned by this install.
pub fn remove_all(locator: &VelopackLocator) {
    remove_all_in_hive(locator, hive_for(locator));
}

/// [`remove_all`] from an explicitly chosen hive.
pub fn remove_all_in_hive(locator: &VelopackLocator, hive: Hive) {
    let reg = AppRegistration::from_locator(locator);
    if reg.is_empty() {
        return;
    }
    info!("Removing file/URL associations from {}...", hive.name());
    match hive.open_classes() {
        Ok(classes) => {
            if remove_all_in(&classes, &reg) {
                notify_assoc_changed();
            }
        }
        Err(e) => warn!("Failed to open {}\\{} ({}), associations were not removed.", hive.name(), CLASSES_KEY, e),
    }
}

fn notify_assoc_changed() {
    unsafe { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, None, None) };
}

/// Returns true when any registry change was attempted. `machine_classes` is the HKLM classes key (if readable),
/// whose `.ext` defaults must not be shadowed by a per-user default.
pub fn sync_in(classes: &RegKey, machine_classes: Option<&RegKey>, next: &AppRegistration, prev: Option<&AppRegistration>) -> bool {
    let mut changed = false;

    // removals first, so a dropped ProgId which was the .ext default clears it before a possible re-registration
    if let Some(prev) = prev {
        let next_progids: Vec<String> = next.file_associations.iter().map(|e| make_progid(&next.app_id, e)).collect();
        for ext in &prev.file_associations {
            let progid = make_progid(&prev.app_id, ext);
            if next_progids.iter().any(|p| p.eq_ignore_ascii_case(&progid)) {
                continue;
            }
            changed = true;
            if let Err(e) = remove_file_association(classes, ext, &progid, &prev.root_dir) {
                warn!("Failed to remove file association '{}' ({}).", ext, e);
            }
        }
        for scheme in &prev.url_protocols {
            if next.url_protocols.iter().any(|s| s.eq_ignore_ascii_case(scheme)) {
                continue;
            }
            changed = true;
            if let Err(e) = remove_url_protocol(classes, scheme, &prev.root_dir) {
                warn!("Failed to remove URL protocol '{}' ({}).", scheme, e);
            }
        }
    }

    for ext in &next.file_associations {
        changed = true;
        if let Err(e) = register_file_association(classes, machine_classes, next, ext) {
            warn!("Failed to register file association '{}' ({}).", ext, e);
        }
    }
    for scheme in &next.url_protocols {
        changed = true;
        if let Err(e) = register_url_protocol(classes, next, scheme) {
            warn!("Failed to register URL protocol '{}' ({}).", scheme, e);
        }
    }
    changed
}

/// Returns true when any registry change was attempted.
pub fn remove_all_in(classes: &RegKey, reg: &AppRegistration) -> bool {
    let mut changed = false;
    for ext in &reg.file_associations {
        changed = true;
        let progid = make_progid(&reg.app_id, ext);
        if let Err(e) = remove_file_association(classes, ext, &progid, &reg.root_dir) {
            warn!("Failed to remove file association '{}' ({}).", ext, e);
        }
    }
    for scheme in &reg.url_protocols {
        changed = true;
        if let Err(e) = remove_url_protocol(classes, scheme, &reg.root_dir) {
            warn!("Failed to remove URL protocol '{}' ({}).", scheme, e);
        }
    }
    changed
}

/// Rejects names that are empty or would address a different key than intended (eg. nested paths).
fn validate_key_name(name: &str, what: &str) -> Result<()> {
    if name.trim().is_empty() {
        bail!("{} name is empty", what);
    }
    if name != name.trim() || name.contains(['\\', '/']) || name.chars().any(char::is_whitespace) {
        bail!("{} name '{}' contains invalid characters", what, name);
    }
    Ok(())
}

fn validate_extension(ext: &str) -> Result<()> {
    validate_key_name(ext, "File extension")?;
    if !ext.starts_with('.') || ext.len() < 2 {
        bail!("File extension '{}' must start with '.' followed by a name", ext);
    }
    Ok(())
}

fn validate_scheme(scheme: &str) -> Result<()> {
    validate_key_name(scheme, "URL scheme")?;
    if !scheme.chars().next().is_some_and(|c| c.is_ascii_alphabetic()) {
        bail!("URL scheme '{}' must start with a letter", scheme);
    }
    Ok(())
}

fn owner_of(key: &RegKey) -> Option<String> {
    key.get_value::<String, _>(ROOT_DIR_VALUE).ok()
}

fn is_owned_by(key: &RegKey, root_dir: &Path) -> bool {
    owner_of(key).is_some_and(|owner| registry::normalize_dir_path(Path::new(&owner)) == registry::normalize_dir_path(root_dir))
}

fn quoted_exe(exe: &Path) -> String {
    format!("\"{}\"", exe.to_string_lossy())
}

/// Creates (or takes over) `name` under `classes` and writes the ownership marker. Warns when the key
/// already exists and is not ours.
fn create_owned_key(classes: &RegKey, name: &str, reg: &AppRegistration, what: &str) -> Result<RegKey> {
    let (key, disposition) = classes.create_subkey(name).with_context(|| format!("create key '{}'", name))?;
    if disposition == REG_OPENED_EXISTING_KEY && !is_owned_by(&key, &reg.root_dir) {
        match owner_of(&key) {
            Some(owner) => warn!("{} '{}' is registered by another Velopack app in '{}', overwriting.", what, name, owner),
            None => warn!("{} '{}' is already registered by another application, overwriting.", what, name),
        }
    }
    key.set_value(ROOT_DIR_VALUE, &reg.root_dir.to_string_lossy().as_ref())?;
    Ok(key)
}

fn write_handler_keys(key: &RegKey, reg: &AppRegistration, hook_flag: &str) -> Result<()> {
    // a stale shell subtree (eg. verbs from a previous registration) must not linger
    match key.delete_subkey_all("shell") {
        Ok(()) => {}
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
        Err(e) => return Err(e).context("delete 'shell' subtree"),
    }
    let (icon, _) = key.create_subkey("DefaultIcon")?;
    icon.set_value("", &format!("{},0", quoted_exe(&reg.main_exe)))?;
    let (command, _) = key.create_subkey(SHELL_OPEN_COMMAND)?;
    command.set_value("", &format!("{} {} \"%1\"", quoted_exe(&reg.main_exe), hook_flag))?;
    Ok(())
}

fn register_url_protocol(classes: &RegKey, reg: &AppRegistration, scheme: &str) -> Result<()> {
    validate_scheme(scheme)?;
    info!("Registering URL protocol '{}'...", scheme);
    let key = create_owned_key(classes, scheme, reg, "URL protocol")?;
    key.set_value("", &format!("URL:{}", reg.app_title))?;
    key.set_value("URL Protocol", &"")?;
    write_handler_keys(&key, reg, constants::HOOK_CLI_URL_PROTO)
}

fn register_file_association(classes: &RegKey, machine_classes: Option<&RegKey>, reg: &AppRegistration, ext: &str) -> Result<()> {
    validate_extension(ext)?;
    let progid = make_progid(&reg.app_id, ext);
    info!("Registering file association '{}' as '{}'...", ext, progid);

    let key = create_owned_key(classes, &progid, reg, "ProgId")?;
    key.set_value("", &format!("{} {} File", reg.app_title, ext.trim_start_matches('.').to_uppercase()))?;
    write_handler_keys(&key, reg, constants::HOOK_CLI_FILE_ASSOC)?;

    let (ext_key, _) = classes.create_subkey(ext).with_context(|| format!("create key '{}'", ext))?;
    let (open_with, _) = ext_key.create_subkey(OPEN_WITH_PROGIDS)?;
    open_with.set_value(&progid, &"")?;

    // only claim the default handler when nobody else has it. HKCR merges HKCU over HKLM, so a per-user default
    // would shadow a machine-wide handler (eg. .txt -> txtfile) which HKCU does not mention. UserChoice is never touched.
    let mut default: String = ext_key.get_value("").unwrap_or_default();
    if default.is_empty() {
        default = machine_classes
            .and_then(|m| m.open_subkey(ext).ok())
            .and_then(|k| k.get_value::<String, _>("").ok())
            .unwrap_or_default();
    }
    if default.is_empty() || default.eq_ignore_ascii_case(&progid) {
        ext_key.set_value("", &progid)?;
    } else {
        info!(
            "'{}' already defaults to '{}', leaving it and registering as an alternative handler.",
            ext, default
        );
    }
    Ok(())
}

fn remove_url_protocol(classes: &RegKey, scheme: &str, root_dir: &Path) -> Result<()> {
    validate_scheme(scheme)?;
    let key = match classes.open_subkey(scheme) {
        Ok(key) => key,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e).with_context(|| format!("open key '{}'", scheme)),
    };
    if !is_owned_by(&key, root_dir) {
        info!("URL protocol '{}' is not registered by this install, leaving it.", scheme);
        return Ok(());
    }
    drop(key);
    info!("Removing URL protocol '{}'...", scheme);
    classes.delete_subkey_all(scheme).with_context(|| format!("delete key '{}'", scheme))
}

fn remove_file_association(classes: &RegKey, ext: &str, progid: &str, root_dir: &Path) -> Result<()> {
    validate_extension(ext)?;
    validate_key_name(progid, "ProgId")?;
    let key = match classes.open_subkey(progid) {
        Ok(key) => key,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e).with_context(|| format!("open key '{}'", progid)),
    };
    if !is_owned_by(&key, root_dir) {
        info!("ProgId '{}' is not registered by this install, leaving it.", progid);
        return Ok(());
    }
    drop(key);
    info!("Removing file association '{}' ('{}')...", ext, progid);
    classes.delete_subkey_all(progid).with_context(|| format!("delete key '{}'", progid))?;

    let ext_key = match classes.open_subkey_with_flags(ext, KEY_ALL_ACCESS) {
        Ok(key) => key,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e).with_context(|| format!("open key '{}'", ext)),
    };
    if let Ok(open_with) = ext_key.open_subkey_with_flags(OPEN_WITH_PROGIDS, KEY_ALL_ACCESS) {
        match open_with.delete_value(progid) {
            Ok(()) => {}
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => warn!("Failed to remove '{}' from {}\\{} ({}).", progid, ext, OPEN_WITH_PROGIDS, e),
        }
    }
    let default: String = ext_key.get_value("").unwrap_or_default();
    if default.eq_ignore_ascii_case(progid) {
        ext_key.delete_value("")?;
    }
    let empty = is_ext_key_empty(&ext_key)?;
    drop(ext_key);
    if empty {
        info!("'{}' has no other handlers, removing it.", ext);
        classes.delete_subkey_all(ext).with_context(|| format!("delete key '{}'", ext))?;
    }
    Ok(())
}

/// True when the key holds no values with data and no subkeys other than an empty `OpenWithProgIds`.
fn is_ext_key_empty(ext_key: &RegKey) -> Result<bool> {
    for value in ext_key.enum_values() {
        let (_, value) = value?;
        if value_has_data(&value) {
            return Ok(false);
        }
    }
    for subkey in ext_key.enum_keys() {
        let subkey = subkey?;
        if !subkey.eq_ignore_ascii_case(OPEN_WITH_PROGIDS) {
            return Ok(false);
        }
        let open_with = ext_key.open_subkey(&subkey)?;
        let info = open_with.query_info()?;
        if info.sub_keys > 0 || info.values > 0 {
            return Ok(false);
        }
    }
    Ok(true)
}

fn value_has_data(value: &RegValue) -> bool {
    match value.vtype {
        REG_SZ | REG_EXPAND_SZ => String::from_reg_value(value).map_or(true, |s| !s.is_empty()),
        _ => !value.bytes.is_empty(),
    }
}

/// Builds the ProgId for an (app id, extension) pair: deterministic, at most 39 characters, only
/// `[A-Za-z0-9.]`, never starting with a digit. Normally `{AppId}.{ext}`; when characters had to be
/// dropped or the result is too long, a hash of the full pair keeps it unique.
pub fn make_progid(app_id: &str, ext: &str) -> String {
    const HASH_LEN: usize = 8;
    const EXT_MAX_LEN: usize = 12;

    let ext_no_dot = ext.trim_start_matches('.');
    let ext_part: String = ext_no_dot.chars().filter(char::is_ascii_alphanumeric).collect();
    let ext_part = if ext_part.is_empty() {
        "File".to_owned()
    } else {
        ext_part
    };

    let mut id_part: String = app_id.chars().filter(|c| c.is_ascii_alphanumeric() || *c == '.').collect();
    id_part = collapse_dots(&id_part);
    let lossless = id_part == app_id && ext_part == ext_no_dot;
    if !id_part.chars().next().is_some_and(|c| c.is_ascii_alphabetic()) {
        id_part = format!("App{}", id_part);
    }

    let simple = format!("{}.{}", id_part, ext_part);
    if lossless && simple.len() <= PROGID_MAX_LEN {
        return simple;
    }

    let mut hasher = sha1_smol::Sha1::new();
    hasher.update(app_id.as_bytes());
    hasher.update(b"|");
    hasher.update(ext.as_bytes());
    let hash = &hasher.digest().to_string()[..HASH_LEN];

    let ext_part: String = ext_part.chars().take(EXT_MAX_LEN).collect();
    let id_max = PROGID_MAX_LEN - HASH_LEN - 2 - ext_part.len();
    let id_part: String = id_part.chars().take(id_max).collect();
    let id_part = collapse_dots(&id_part);
    let id_part = if id_part.is_empty() {
        "App".to_owned()
    } else {
        id_part
    };
    format!("{}.{}.{}", id_part, hash, ext_part)
}

/// Removes leading/trailing dots and squashes runs of dots, so no segment is empty.
fn collapse_dots(s: &str) -> String {
    s.split('.').filter(|p| !p.is_empty()).collect::<Vec<_>>().join(".")
}

#[cfg(test)]
mod tests {
    use super::*;
    use pretty_assertions::assert_eq;

    /// A scratch `...\Classes` key under HKCU\Software\VelopackTests, deleted on drop.
    struct ScratchClasses {
        hkcu: RegKey,
        path: String,
        classes: RegKey,
    }

    impl ScratchClasses {
        fn new() -> Self {
            let hkcu = RegKey::predef(HKEY_CURRENT_USER);
            let path = format!("Software\\VelopackTests\\{}", crate::shared::random_string(16));
            let (classes, _) = hkcu.create_subkey(format!("{}\\Classes", path)).unwrap();
            Self { hkcu, path, classes }
        }

        fn key(&self, path: &str) -> Option<RegKey> {
            self.classes.open_subkey(path).ok()
        }

        fn default_of(&self, path: &str) -> Option<String> {
            self.key(path).and_then(|k| k.get_value::<String, _>("").ok())
        }

        fn value_of(&self, path: &str, name: &str) -> Option<String> {
            self.key(path).and_then(|k| k.get_value::<String, _>(name).ok())
        }

        fn subkeys(&self) -> Vec<String> {
            let mut keys: Vec<String> = self.classes.enum_keys().map(|k| k.unwrap()).collect();
            keys.sort();
            keys
        }
    }

    impl Drop for ScratchClasses {
        fn drop(&mut self) {
            let _ = self.hkcu.delete_subkey_all(&self.path);
        }
    }

    fn registration(root: &str, exe: &str, exts: &[&str], schemes: &[&str]) -> AppRegistration {
        AppRegistration {
            app_id: "MyCompany.MyApp".to_owned(),
            app_title: "My App".to_owned(),
            root_dir: PathBuf::from(root),
            main_exe: PathBuf::from(root).join("current").join(exe),
            file_associations: exts.iter().map(|s| s.to_string()).collect(),
            url_protocols: schemes.iter().map(|s| s.to_string()).collect(),
        }
    }

    const ROOT: &str = "C:\\Users\\Test\\AppData\\Local\\MyApp";
    const OTHER_ROOT: &str = "D:\\Other\\MyApp";

    #[test]
    fn registers_url_protocol_and_file_association() {
        let s = ScratchClasses::new();
        let reg = registration(ROOT, "MyApp.exe", &[".myproj"], &["myapp"]);
        assert!(sync_in(&s.classes, None, &reg, None));

        let exe = format!("\"{}\\current\\MyApp.exe\"", ROOT);
        assert_eq!(s.default_of("myapp"), Some("URL:My App".to_owned()));
        assert_eq!(s.value_of("myapp", "URL Protocol"), Some(String::new()));
        assert_eq!(s.value_of("myapp", ROOT_DIR_VALUE), Some(ROOT.to_owned()));
        assert_eq!(s.default_of("myapp\\DefaultIcon"), Some(format!("{},0", exe)));
        assert_eq!(
            s.default_of("myapp\\shell\\open\\command"),
            Some(format!("{} --veloapp-urlproto \"%1\"", exe))
        );

        assert_eq!(s.default_of("MyCompany.MyApp.myproj"), Some("My App MYPROJ File".to_owned()));
        assert_eq!(s.value_of("MyCompany.MyApp.myproj", ROOT_DIR_VALUE), Some(ROOT.to_owned()));
        assert_eq!(s.default_of("MyCompany.MyApp.myproj\\DefaultIcon"), Some(format!("{},0", exe)));
        assert_eq!(
            s.default_of("MyCompany.MyApp.myproj\\shell\\open\\command"),
            Some(format!("{} --veloapp-fileassoc \"%1\"", exe))
        );
        assert_eq!(s.default_of(".myproj"), Some("MyCompany.MyApp.myproj".to_owned()));
        assert_eq!(s.value_of(".myproj\\OpenWithProgIds", "MyCompany.MyApp.myproj"), Some(String::new()));
        assert_eq!(s.subkeys(), vec![".myproj", "MyCompany.MyApp.myproj", "myapp"]);
    }

    #[test]
    fn re_registering_is_idempotent() {
        let s = ScratchClasses::new();
        let reg = registration(ROOT, "MyApp.exe", &[".myproj", ".mytpl"], &["myapp"]);
        sync_in(&s.classes, None, &reg, None);
        sync_in(&s.classes, None, &reg, Some(&reg));
        sync_in(&s.classes, None, &reg, Some(&reg));

        assert_eq!(
            s.subkeys(),
            vec![".myproj", ".mytpl", "MyCompany.MyApp.myproj", "MyCompany.MyApp.mytpl", "myapp"]
        );
        assert_eq!(s.default_of(".myproj"), Some("MyCompany.MyApp.myproj".to_owned()));
        let open_with = s.key(".myproj\\OpenWithProgIds").unwrap();
        assert_eq!(open_with.query_info().unwrap().values, 1);
    }

    #[test]
    fn sync_removes_dropped_associations_and_adds_new_ones() {
        let s = ScratchClasses::new();
        let prev = registration(ROOT, "MyApp.exe", &[".myproj", ".mytpl"], &["myapp", "myapp-beta"]);
        sync_in(&s.classes, None, &prev, None);

        let next = registration(ROOT, "MyApp.exe", &[".myproj", ".mynew"], &["myapp"]);
        sync_in(&s.classes, None, &next, Some(&prev));

        assert_eq!(
            s.subkeys(),
            vec![".mynew", ".myproj", "MyCompany.MyApp.mynew", "MyCompany.MyApp.myproj", "myapp"]
        );
    }

    #[test]
    fn main_exe_rename_updates_commands() {
        let s = ScratchClasses::new();
        let prev = registration(ROOT, "OldName.exe", &[".myproj"], &["myapp"]);
        sync_in(&s.classes, None, &prev, None);
        let next = registration(ROOT, "NewName.exe", &[".myproj"], &["myapp"]);
        sync_in(&s.classes, None, &next, Some(&prev));

        let exe = format!("\"{}\\current\\NewName.exe\"", ROOT);
        assert_eq!(
            s.default_of("myapp\\shell\\open\\command"),
            Some(format!("{} --veloapp-urlproto \"%1\"", exe))
        );
        assert_eq!(s.default_of("myapp\\DefaultIcon"), Some(format!("{},0", exe)));
        assert_eq!(
            s.default_of("MyCompany.MyApp.myproj\\shell\\open\\command"),
            Some(format!("{} --veloapp-fileassoc \"%1\"", exe))
        );
        assert_eq!(s.default_of("MyCompany.MyApp.myproj\\DefaultIcon"), Some(format!("{},0", exe)));
    }

    #[test]
    fn app_id_change_replaces_progid_for_same_extension() {
        let s = ScratchClasses::new();
        let prev = registration(ROOT, "MyApp.exe", &[".myproj"], &[]);
        sync_in(&s.classes, None, &prev, None);
        let mut next = prev.clone();
        next.app_id = "MyCompany.Renamed".to_owned();
        sync_in(&s.classes, None, &next, Some(&prev));

        assert_eq!(s.subkeys(), vec![".myproj", "MyCompany.Renamed.myproj"]);
        assert_eq!(s.default_of(".myproj"), Some("MyCompany.Renamed.myproj".to_owned()));
        let open_with = s.key(".myproj\\OpenWithProgIds").unwrap();
        assert!(open_with.get_value::<String, _>("MyCompany.MyApp.myproj").is_err());
    }

    #[test]
    fn keys_owned_by_another_install_are_not_removed() {
        let s = ScratchClasses::new();
        let other = registration(OTHER_ROOT, "MyApp.exe", &[".myproj"], &["myapp"]);
        sync_in(&s.classes, None, &other, None);

        let ours = registration(ROOT, "MyApp.exe", &[".myproj"], &["myapp"]);
        assert!(remove_all_in(&s.classes, &ours));
        assert_eq!(s.subkeys(), vec![".myproj", "MyCompany.MyApp.myproj", "myapp"]);
        assert_eq!(s.value_of("myapp", ROOT_DIR_VALUE), Some(OTHER_ROOT.to_owned()));
        assert_eq!(s.default_of(".myproj"), Some("MyCompany.MyApp.myproj".to_owned()));

        // a sync that drops them behaves the same way
        let empty = registration(ROOT, "MyApp.exe", &[], &[]);
        sync_in(&s.classes, None, &empty, Some(&ours));
        assert_eq!(s.subkeys(), vec![".myproj", "MyCompany.MyApp.myproj", "myapp"]);
    }

    #[test]
    fn ownership_compares_root_dir_case_insensitively() {
        let s = ScratchClasses::new();
        let reg = registration(ROOT, "MyApp.exe", &[".myproj"], &["myapp"]);
        sync_in(&s.classes, None, &reg, None);

        let mut upper = reg.clone();
        upper.root_dir = PathBuf::from(format!("{}\\", ROOT.to_uppercase()));
        remove_all_in(&s.classes, &upper);
        assert_eq!(s.subkeys(), Vec::<String>::new());
    }

    #[test]
    fn foreign_keys_are_overwritten_and_foreign_shell_verbs_dropped() {
        let s = ScratchClasses::new();
        let (foreign, _) = s.classes.create_subkey("myapp").unwrap();
        foreign.set_value("", &"URL:Foreign").unwrap();
        let (verb, _) = foreign.create_subkey("shell\\edit\\command").unwrap();
        verb.set_value("", &"foreign.exe %1").unwrap();
        drop((foreign, verb));

        let reg = registration(ROOT, "MyApp.exe", &[], &["myapp"]);
        sync_in(&s.classes, None, &reg, None);
        assert_eq!(s.default_of("myapp"), Some("URL:My App".to_owned()));
        assert_eq!(s.value_of("myapp", ROOT_DIR_VALUE), Some(ROOT.to_owned()));
        assert!(s.key("myapp\\shell\\edit").is_none());
        assert!(s.key("myapp\\shell\\open\\command").is_some());
    }

    #[test]
    fn existing_ext_default_is_preserved_and_restored_state_on_removal() {
        let s = ScratchClasses::new();
        let (ext, _) = s.classes.create_subkey(".myproj").unwrap();
        ext.set_value("", &"Other.ProgId").unwrap();
        let (open_with, _) = ext.create_subkey(OPEN_WITH_PROGIDS).unwrap();
        open_with.set_value("Other.ProgId", &"").unwrap();
        drop((ext, open_with));

        let reg = registration(ROOT, "MyApp.exe", &[".myproj"], &[]);
        sync_in(&s.classes, None, &reg, None);
        assert_eq!(s.default_of(".myproj"), Some("Other.ProgId".to_owned()));
        assert_eq!(s.value_of(".myproj\\OpenWithProgIds", "MyCompany.MyApp.myproj"), Some(String::new()));

        remove_all_in(&s.classes, &reg);
        // the .ext key still has another handler, so it stays, minus our entry
        assert_eq!(s.subkeys(), vec![".myproj"]);
        assert_eq!(s.default_of(".myproj"), Some("Other.ProgId".to_owned()));
        assert_eq!(s.value_of(".myproj\\OpenWithProgIds", "Other.ProgId"), Some(String::new()));
        assert!(s.value_of(".myproj\\OpenWithProgIds", "MyCompany.MyApp.myproj").is_none());
    }

    #[test]
    fn machine_wide_ext_default_is_not_shadowed() {
        let user = ScratchClasses::new();
        let machine = ScratchClasses::new();
        let (ext, _) = machine.classes.create_subkey(".myproj").unwrap();
        ext.set_value("", &"Other.ProgId").unwrap();
        drop(ext);

        let reg = registration(ROOT, "MyApp.exe", &[".myproj"], &[]);
        sync_in(&user.classes, Some(&machine.classes), &reg, None);
        // HKCU has no default (HKCR would merge ours over the HKLM one), but we are an alternative handler
        assert_eq!(user.default_of(".myproj"), None);
        assert_eq!(user.value_of(".myproj\\OpenWithProgIds", "MyCompany.MyApp.myproj"), Some(String::new()));
        assert_eq!(machine.default_of(".myproj"), Some("Other.ProgId".to_owned()));

        remove_all_in(&user.classes, &reg);
        assert_eq!(user.subkeys(), Vec::<String>::new());
        assert_eq!(machine.subkeys(), vec![".myproj"]);
    }

    #[test]
    fn empty_ext_default_is_claimed_and_key_removed_when_nothing_else_remains() {
        let s = ScratchClasses::new();
        let (ext, _) = s.classes.create_subkey(".myproj").unwrap();
        ext.set_value("", &"").unwrap();
        drop(ext);

        let reg = registration(ROOT, "MyApp.exe", &[".myproj"], &[]);
        sync_in(&s.classes, None, &reg, None);
        assert_eq!(s.default_of(".myproj"), Some("MyCompany.MyApp.myproj".to_owned()));

        remove_all_in(&s.classes, &reg);
        assert_eq!(s.subkeys(), Vec::<String>::new());
    }

    #[test]
    fn ext_key_with_other_values_or_subkeys_is_kept_on_removal() {
        let s = ScratchClasses::new();
        let reg = registration(ROOT, "MyApp.exe", &[".myproj"], &[]);
        sync_in(&s.classes, None, &reg, None);
        let (ext, _) = s.classes.create_subkey(".myproj").unwrap();
        ext.set_value("Content Type", &"application/x-myproj").unwrap();
        drop(ext);

        remove_all_in(&s.classes, &reg);
        assert_eq!(s.subkeys(), vec![".myproj"]);
        assert_eq!(s.default_of(".myproj"), None);
        assert_eq!(s.value_of(".myproj", "Content Type"), Some("application/x-myproj".to_owned()));

        // and a foreign subkey (eg. ShellNew) also keeps it alive
        let s = ScratchClasses::new();
        sync_in(&s.classes, None, &reg, None);
        s.classes.create_subkey(".myproj\\ShellNew").unwrap();
        remove_all_in(&s.classes, &reg);
        assert_eq!(s.subkeys(), vec![".myproj"]);
    }

    #[test]
    fn empty_lists_are_a_no_op_and_never_delete_anything() {
        let s = ScratchClasses::new();
        let (foreign, _) = s.classes.create_subkey("foreign").unwrap();
        foreign.set_value("", &"keep me").unwrap();
        s.classes.set_value("", &"classes default").unwrap();
        drop(foreign);

        let empty = registration(ROOT, "MyApp.exe", &[], &[]);
        assert!(!sync_in(&s.classes, None, &empty, None));
        assert!(!sync_in(&s.classes, None, &empty, Some(&empty)));
        assert!(!remove_all_in(&s.classes, &empty));

        // empty / malformed names coming from a bad manifest are rejected before any registry call
        let bad = registration(
            ROOT,
            "MyApp.exe",
            &["", " ", ".", "myproj", ".a\\b"],
            &["", "://", ".x", "a\\b", "my app"],
        );
        sync_in(&s.classes, None, &bad, None);
        remove_all_in(&s.classes, &bad);
        sync_in(&s.classes, None, &empty, Some(&bad));

        assert_eq!(s.subkeys(), vec!["foreign"]);
        assert_eq!(s.default_of("foreign"), Some("keep me".to_owned()));
        assert_eq!(s.classes.get_value::<String, _>("").unwrap(), "classes default");
    }

    #[test]
    fn progid_is_simple_when_it_fits() {
        assert_eq!(make_progid("MyCompany.MyApp", ".myproj"), "MyCompany.MyApp.myproj");
        assert_eq!(make_progid("MyApp", ".x"), "MyApp.x");
        assert_eq!(
            make_progid("App", ".abcdefghijklmnopqrstuvwxyz1234567"),
            "App.abcdefghijklmnopqrstuvwxyz1234567"
        );
    }

    #[test]
    fn progid_obeys_length_and_charset_rules() {
        let cases = [
            ("MyCompany.MyApp", ".myproj"),
            ("my-company_app", ".my-proj"),
            ("my company app", ".my_tpl"),
            ("123App", ".proj"),
            ("1", ".1"),
            ("..", "._-"),
            ("", ".x"),
            ("A.Very.Long.Application.Identifier.That.Goes.On", ".extension"),
            ("ShortId", ".a_very_long_extension_name_indeed_yes"),
            ("Ünïcödé.App", ".prøj"),
        ];
        for (app_id, ext) in cases {
            let progid = make_progid(app_id, ext);
            assert!(!progid.is_empty(), "{app_id} {ext}");
            assert!(progid.len() <= PROGID_MAX_LEN, "{progid} is {} chars", progid.len());
            assert!(progid.chars().all(|c| c.is_ascii_alphanumeric() || c == '.'), "{progid}");
            assert!(progid.chars().next().unwrap().is_ascii_alphabetic(), "{progid}");
            assert!(!progid.contains("..") && !progid.ends_with('.'), "{progid}");
            assert_eq!(progid, make_progid(app_id, ext), "deterministic");
        }
    }

    #[test]
    fn progid_is_unique_when_sanitization_collides() {
        assert_ne!(make_progid("my-app", ".proj"), make_progid("myapp", ".proj"));
        assert_ne!(make_progid("my-app", ".proj"), make_progid("my_app", ".proj"));
        assert_ne!(make_progid("MyApp", ".my-proj"), make_progid("MyApp", ".myproj"));
        assert_ne!(make_progid("MyApp", ".my-proj"), make_progid("MyApp", ".my_proj"));
        let long_a = make_progid("A.Very.Long.Application.Identifier.That.Goes.On.AndOn.A", ".ext");
        let long_b = make_progid("A.Very.Long.Application.Identifier.That.Goes.On.AndOn.B", ".ext");
        assert_ne!(long_a, long_b);
    }
}
