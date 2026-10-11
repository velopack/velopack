//! `SingleInstance` association launches: the first instance of an app holds the endpoint lock and serves
//! association activations over IPC; a later launch carrying `--veloapp-fileassoc` / `--veloapp-urlproto`
//! arguments forwards them to that instance and exits. Everything here keeps the association VALUES out of the
//! logs (only counts and lengths are logged). Public only with the `public-utils` feature, for the Rust test app's
//! `ipc-elect` command (the cross-language election tests).

use crate::app::{canonical_association_flag, Association, AssociationHook};
use crate::constants::HOOK_CLI_FILE_ASSOC;
use crate::ipc::{self, IpcConnection, IpcEndpoint, IpcItem, IpcMessage};
use crate::locator::VelopackLocator;
use crate::lockfile::{is_lock_contention, LockFile};
use std::sync::{Arc, Mutex};
use std::time::Instant;

/// The outcome of [`elect`].
pub enum Election {
    /// Handle the activation (if any) in this process; there is no server, or it could not be reached.
    Standalone,
    /// This process holds the endpoint lock and should start the server ([`start`]).
    Server(IpcEndpoint, LockFile),
    /// The activation was delivered to the running instance (acked); this process should exit.
    Forwarded,
}

fn lock_once(ep: &IpcEndpoint) -> Result<Option<LockFile>, std::io::Error> {
    match LockFile::try_acquire_lock_once(&ep.lock_path) {
        Ok(lock) => Ok(Some(lock)),
        Err(e) if is_lock_contention(&e) => Ok(None),
        Err(e) => Err(e),
    }
}

/// Decides this process's role. The endpoint is keyed on the app id, the install root and the current user; the
/// lock makes the choice race-free. A plain launch (no `associations`) never forwards. A launch with
/// associations keeps trying for [`ipc::CONNECT_WINDOW`] to either reach the server or take over the lock; when
/// neither happens, or the delivery fails in any way, it handles the activation itself (never losing it, at the
/// price of a possible duplicate).
pub fn elect(locator: &VelopackLocator, associations: &[Association]) -> Election {
    #[cfg(not(target_os = "linux"))]
    let root = locator.get_root_dir();
    #[cfg(target_os = "linux")]
    let root = locator.get_appimage_path();
    let ep = match IpcEndpoint::for_app(&locator.get_manifest_id(), &root) {
        Ok(ep) => ep,
        Err(e) => {
            warn!("SingleInstance: IPC unavailable, handling activations in-process: {e}");
            return Election::Standalone;
        }
    };

    match lock_once(&ep) {
        Ok(Some(lock)) => return Election::Server(ep, lock),
        Ok(None) if associations.is_empty() => return Election::Standalone,
        Ok(None) => {}
        Err(e) => {
            warn!("SingleInstance: cannot use the endpoint lock, handling activations in-process: {e}");
            return Election::Standalone;
        }
    }

    let mut msg = IpcMessage::new(ipc::TYPE_ASSOCIATION);
    msg.items = associations
        .iter()
        .map(|(flag, value)| IpcItem {
            flag: flag.to_string(),
            value: value.clone(),
        })
        .collect();
    if serde_json::to_vec(&msg).map(|b| b.len()).unwrap_or(usize::MAX) > ipc::MAX_MESSAGE_BYTES {
        warn!("SingleInstance: activation is too large to forward, handling it in-process");
        return Election::Standalone;
    }

    let deadline = Instant::now() + ipc::CONNECT_WINDOW;
    loop {
        match ipc::connect(&ep) {
            Ok(Some(conn)) => {
                return match ipc::request(conn, &msg, ipc::EXCHANGE_TIMEOUT) {
                    Ok(reply) if reply.is_ack() => Election::Forwarded,
                    Ok(reply) => {
                        warn!(
                            "SingleInstance: running instance answered '{}' ({}), handling the activation in-process",
                            reply.message_type,
                            reply.message.as_deref().unwrap_or("")
                        );
                        Election::Standalone
                    }
                    Err(e) => {
                        warn!("SingleInstance: forwarding failed, handling the activation in-process: {e}");
                        Election::Standalone
                    }
                };
            }
            Ok(None) => {}
            Err(e) => {
                warn!("SingleInstance: cannot connect to the running instance, handling the activation in-process: {e}");
                return Election::Standalone;
            }
        }
        if Instant::now() >= deadline {
            warn!("SingleInstance: no running instance answered, handling the activation in-process");
            return Election::Standalone;
        }
        match lock_once(&ep) {
            Ok(Some(lock)) => return Election::Server(ep, lock),
            Ok(None) => std::thread::sleep(ipc::RETRY_INTERVAL),
            Err(e) => {
                warn!("SingleInstance: cannot use the endpoint lock, handling the activation in-process: {e}");
                return Election::Standalone;
            }
        }
    }
}

/// Starts serving association activations on `ep` for the rest of the process, holding `lock`. Hooks run on the
/// connection thread, serialized through one mutex, and the `ack` is sent only after they have returned. A
/// failure to start simply leaves the process without a server (the lock is released).
pub fn start(ep: IpcEndpoint, lock: LockFile, file_hook: Option<AssociationHook>, url_hook: Option<AssociationHook>) {
    let gate = Arc::new(Mutex::new(()));
    let handler = Arc::new(move |mut conn: IpcConnection| {
        let (file_hook, url_hook, gate) = (file_hook.clone(), url_hook.clone(), gate.clone());
        ipc::serve_request(&mut conn, move |msg| handle(msg, file_hook.as_ref(), url_hook.as_ref(), &gate));
    });
    match ipc::start_server(&ep, Some(lock), handler) {
        Ok(()) => info!("SingleInstance: serving association activations for this app."),
        Err(e) => warn!("SingleInstance: could not start the IPC server, later activations start new instances: {e}"),
    }
}

fn handle(msg: IpcMessage, file_hook: Option<&AssociationHook>, url_hook: Option<&AssociationHook>, gate: &Mutex<()>) -> IpcMessage {
    if msg.message_type != ipc::TYPE_ASSOCIATION {
        return IpcMessage::error(ipc::ERR_UNKNOWN_MESSAGE_TYPE).reply_to(&msg);
    }
    let mut resolved: Vec<(&'static str, &AssociationHook, &str)> = Vec::with_capacity(msg.items.len());
    for item in &msg.items {
        let hook = canonical_association_flag(&item.flag).and_then(|flag| {
            if flag == HOOK_CLI_FILE_ASSOC {
                file_hook
            } else {
                url_hook
            }
            .map(|h| (flag, h))
        });
        match hook {
            Some((flag, hook)) if !item.value.is_empty() => resolved.push((flag, hook, &item.value)),
            _ => return IpcMessage::error(ipc::ERR_UNHANDLED_ASSOCIATION).reply_to(&msg),
        }
    }
    let _serialized = gate.lock().unwrap_or_else(|e| e.into_inner());
    for (flag, hook, value) in resolved {
        info!("IPC: association ({}), {} chars", flag, value.len());
        if std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| hook(value.to_owned()))).is_err() {
            error!("IPC: the {} hook panicked", flag);
        }
    }
    IpcMessage::ack().reply_to(&msg)
}

// POSIX record locks are per process, so a second election in the same process cannot observe a held lock; the
// Unix election is covered cross-process by the C# interop tests (`testapp ipc-elect`).
#[cfg(all(test, windows))]
mod tests {
    use super::*;
    use crate::bundle::Manifest;
    use crate::constants::HOOK_CLI_URL_PROTO;
    use crate::locator::VelopackLocatorConfig;
    use std::time::Duration;

    fn locator(root: &std::path::Path) -> VelopackLocator {
        let config = VelopackLocatorConfig {
            RootAppDir: root.to_path_buf(),
            ..Default::default()
        };
        let manifest = Manifest {
            id: "SingleInstanceTest".into(),
            ..Default::default()
        };
        VelopackLocator::new_with_manifest(config, manifest)
    }

    fn wait_for(received: &Mutex<Vec<String>>, count: usize) -> Vec<String> {
        let deadline = Instant::now() + Duration::from_secs(5);
        while received.lock().unwrap().len() < count && Instant::now() < deadline {
            std::thread::sleep(Duration::from_millis(20));
        }
        received.lock().unwrap().clone()
    }

    #[test]
    fn test_election_server_forward_and_standalone() {
        let tmp = tempfile::TempDir::new().unwrap();
        let locator = locator(tmp.path());
        let received = Arc::new(Mutex::new(Vec::<String>::new()));

        let (ep, lock) = match elect(&locator, &[]) {
            Election::Server(ep, lock) => (ep, lock),
            _ => panic!("the first instance must become the server"),
        };
        let sink = received.clone();
        let url_hook: AssociationHook = Arc::new(move |v| sink.lock().unwrap().push(v));
        start(ep, lock, None, Some(url_hook));

        // a later launch with an association forwards it and the server invokes the hook (on its own thread)
        let x = (HOOK_CLI_URL_PROTO, "myapp://x?q=\"1 2\"".to_string());
        assert!(matches!(elect(&locator, std::slice::from_ref(&x)), Election::Forwarded));
        assert_eq!(wait_for(&received, 1), vec![x.1.clone()]);

        // a plain launch is just another instance
        assert!(matches!(elect(&locator, &[]), Election::Standalone));

        // an association without a registered hook is refused by the server, so it is handled in-process
        let started = Instant::now();
        assert!(matches!(
            elect(&locator, &[(HOOK_CLI_FILE_ASSOC, "C:\\a.myproj".to_string())]),
            Election::Standalone
        ));
        assert!(started.elapsed() < Duration::from_secs(4));
        assert_eq!(received.lock().unwrap().len(), 1);

        // several items arrive in order; the ack comes after both hooks ran
        let y = (HOOK_CLI_URL_PROTO, "myapp://y".to_string());
        let z = (HOOK_CLI_URL_PROTO, "myapp://z".to_string());
        assert!(matches!(elect(&locator, &[y.clone(), z.clone()]), Election::Forwarded));
        assert_eq!(wait_for(&received, 3)[1..], [y.1, z.1]);
    }
}
