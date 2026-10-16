//! Named-pipe specifics (`\\.\pipe\velopack-{key}`): the pipe is owned by, and only accessible to, the current user,
//! whatever the elevation of either side, and a client checks that owner before it writes anything.

use super::{IpcConnection, IpcEndpoint};
use crate::{misc::io_error_from_win32, Error};
use interprocess::local_socket::ListenerOptions;
use interprocess::os::windows::{local_socket::ListenerOptionsExt, security_descriptor::SecurityDescriptor, ToWtf16};
use std::io;
use std::os::windows::io::{AsHandle, AsRawHandle};
use std::path::PathBuf;
use windows::core::PWSTR;
use windows::Win32::Foundation::{CloseHandle, LocalFree, HANDLE, HLOCAL};
use windows::Win32::Security::Authorization::{ConvertSidToStringSidW, GetSecurityInfo, SE_KERNEL_OBJECT};
use windows::Win32::Security::{GetTokenInformation, TokenUser, OWNER_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR, PSID, TOKEN_QUERY, TOKEN_USER};
use windows::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};
use windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow;

pub(crate) fn endpoint_for_key(key: &str) -> Result<IpcEndpoint, Error> {
    let lock_dir = crate::known_path::get_local_app_data()
        .map_err(|e| Error::Other(format!("LocalAppData unavailable: {e}")))?
        .join("velopack")
        .join("ipc");
    std::fs::create_dir_all(&lock_dir)?;
    Ok(IpcEndpoint {
        key: key.to_owned(),
        address: PathBuf::from(format!("\\\\.\\pipe\\velopack-{key}")),
        lock_path: lock_dir.join(format!("{key}.lock")),
    })
}

fn sid_to_string(sid: PSID) -> io::Result<String> {
    let mut text = PWSTR::null();
    unsafe {
        ConvertSidToStringSidW(sid, &mut text).map_err(io_error_from_win32)?;
        let result = text.to_string().map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e));
        let _ = LocalFree(Some(HLOCAL(text.0.cast())));
        result
    }
}

/// The string SID of the current process token's user, eg. `S-1-5-21-...` (the same for elevated and
/// non-elevated processes of that user).
pub(crate) fn current_user() -> Result<String, Error> {
    let mut token = HANDLE::default();
    unsafe { OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) }?;
    let user = (|| {
        let mut size = 0u32;
        let _ = unsafe { GetTokenInformation(token, TokenUser, None, 0, &mut size) };
        // u64 elements keep the TOKEN_USER properly aligned
        let mut buf = vec![0u64; (size as usize).div_ceil(8)];
        unsafe { GetTokenInformation(token, TokenUser, Some(buf.as_mut_ptr().cast()), size, &mut size) }.map_err(io_error_from_win32)?;
        // SAFETY: the buffer holds a TOKEN_USER whose Sid points inside the same buffer
        sid_to_string(unsafe { (*buf.as_ptr().cast::<TOKEN_USER>()).User.Sid })
    })();
    unsafe {
        let _ = CloseHandle(token);
    }
    Ok(user?)
}

/// Gives the pipe a DACL granting access only to the current user (the same descriptor as the C# server; `interprocess`
/// rejects remote clients via the pipe mode), and makes that user its owner explicitly (an elevated token would otherwise
/// make the Administrators group the owner), so clients can verify it.
pub(crate) fn listener_options(options: ListenerOptions<'_>) -> Result<ListenerOptions<'_>, Error> {
    let user = current_user()?;
    let sddl = format!("O:{user}D:P(A;;GA;;;{user})")
        .to_wtf_16()
        .map_err(|e| Error::Other(e.to_string()))?;
    Ok(options.security_descriptor(SecurityDescriptor::deserialize(&sddl)?))
}

/// Refuses a pipe whose owner is not the current user (eg. one squatted by another user), then allows the server
/// process to take the foreground (failures of that are ignored).
pub(crate) fn verify_server(conn: &IpcConnection) -> Result<(), Error> {
    let IpcConnection::NamedPipe(pipe) = conn;
    let mut owner = PSID::default();
    let mut descriptor = PSECURITY_DESCRIPTOR::default();
    let handle = HANDLE(pipe.as_handle().as_raw_handle());
    let status = unsafe {
        GetSecurityInfo(
            handle,
            SE_KERNEL_OBJECT,
            OWNER_SECURITY_INFORMATION,
            Some(&mut owner),
            None,
            None,
            None,
            Some(&mut descriptor),
        )
    };
    let owner = status.ok().map_err(io_error_from_win32).and_then(|_| sid_to_string(owner));
    unsafe {
        let _ = LocalFree(Some(HLOCAL(descriptor.0)));
    }
    match owner {
        Ok(owner) if owner == current_user()? => {}
        Ok(_) => return Err(Error::Other("IPC server could not be verified: the pipe is owned by another user".into())),
        Err(e) => return Err(Error::Other(format!("IPC server could not be verified: {e}"))),
    }
    match pipe.inner().server_process_id() {
        Ok(pid) => {
            if let Err(e) = unsafe { AllowSetForegroundWindow(pid) } {
                debug!("IPC: AllowSetForegroundWindow failed: {e}");
            }
        }
        Err(e) => debug!("IPC: server process id unavailable: {e}"),
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use interprocess::local_socket::{prelude::*, GenericFilePath};

    #[test]
    fn test_current_user_is_a_user_sid() {
        let user = current_user().unwrap();
        assert!(user.starts_with("S-1-5-"), "{user}");
    }

    #[test]
    fn test_connect_refuses_squatted_pipe() {
        // a pipe only SYSTEM may open: our own open fails with access denied, which must be an error (never
        // "nobody listening", which would make the caller retry and eventually handle in-process)
        let ep = IpcEndpoint::from_key(&format!("squat-{}", crate::misc::random_string(12).to_ascii_lowercase())).unwrap();
        let sddl = "D:P(A;;GA;;;SY)".to_wtf_16().unwrap();
        let _squatter = ListenerOptions::new()
            .name(ep.address.as_os_str().to_fs_name::<GenericFilePath>().unwrap())
            .security_descriptor(SecurityDescriptor::deserialize(&sddl).unwrap())
            .create_sync()
            .unwrap();
        match super::super::connect(&ep) {
            Err(Error::Io(e)) => assert_eq!(e.kind(), io::ErrorKind::PermissionDenied, "{e:?}"),
            other => panic!("expected an access-denied error, got {:?}", other.map(|c| c.is_some())),
        }
    }
}
