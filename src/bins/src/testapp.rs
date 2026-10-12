use anyhow::{bail, Result};
use simplelog::*;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::sync::{mpsc, Arc};
use std::time::{Duration, Instant};
use std::{env, fs, fs::OpenOptions, io::Write, process};
use velopack::bundle::Manifest;
use velopack::constants::{HOOK_CLI_FILE_ASSOC, HOOK_CLI_URL_PROTO};
use velopack::ipc::{self, IpcConnection, IpcEndpoint, IpcItem, IpcMessage};
use velopack::locator::{VelopackLocator, VelopackLocatorConfig};
use velopack::lockfile::LockFile;
use velopack::single_instance::{elect, start, Election};
use velopack::{locator, sources, UpdateCheck, UpdateManager, VelopackApp};

fn main() -> Result<()> {
    let _ = TermLogger::init(LevelFilter::Info, Config::default(), TerminalMode::Stderr, ColorChoice::Never);
    let args: Vec<String> = env::args().skip(1).collect();

    // the cross-language IPC test commands never start VelopackApp
    if let Some(code) = run_ipc_command(&args) {
        process::exit(code);
    }

    // Log args to args.txt BEFORE VelopackApp::run(), which may exit on fast hooks.
    // Existing Windows tests depend on this file being written during install hooks.
    if !args.is_empty() {
        let line = args.join(" ") + "\n";
        if let Ok(mut file) = OpenOptions::new().create(true).append(true).open("args.txt") {
            let _ = file.write_all(line.as_bytes());
        }
    }

    let should_auto_update = args.iter().any(|a| a.eq_ignore_ascii_case("--autoupdate"));

    // association callbacks write the value they received next to the install dir, exactly like the C# TestApp
    let exe_dir = env::current_exe()?.parent().unwrap().to_path_buf();
    let out_dir = exe_dir.join("..");
    let (file_dir, url_dir) = (out_dir.clone(), out_dir.clone());

    VelopackApp::build()
        .set_auto_apply_on_startup(should_auto_update)
        .on_file_assoc(move |path| record_association(&file_dir, "file-assoc", "OnFileAssoc", &path))
        .on_url_proto(move |url| record_association(&url_dir, "url-proto", "OnUrlProto", &url))
        .run();

    if should_auto_update {
        process::exit(-1);
    }

    if args.len() == 1 && args[0] == "version" {
        match locator::auto_locate_app_manifest(locator::LocationContext::FromCurrentExe) {
            Ok(loc) => println!("{}", loc.get_manifest_version_full_string()),
            Err(_) => println!("unknown_version"),
        }
        return Ok(());
    }

    if args.len() == 1 && args[0] == "test" {
        let path = exe_dir.join("test_string.txt");
        match fs::read_to_string(&path) {
            Ok(s) => println!("{}", s.trim()),
            Err(_) => println!("no_test_string"),
        }
        return Ok(());
    }

    if args.len() == 1 && args[0] == "wait" {
        // stays alive (serving SingleInstance activations) until the test writes the stop sentinel
        append_line(&out_dir.join("ready"), &format!("Ready: {}", process::id()));
        let stop = out_dir.join("stop");
        let deadline = Instant::now() + Duration::from_secs(120);
        while !stop.exists() && Instant::now() < deadline {
            std::thread::sleep(Duration::from_millis(100));
        }
        return Ok(());
    }

    if args.len() == 2 {
        if args[0] == "check" {
            let source = sources::FileSource::new(&args[1]);
            let um = UpdateManager::new(source, None, None)?;
            match um.check_for_updates()? {
                UpdateCheck::UpdateAvailable(info) => {
                    println!("update: {}", info.TargetFullRelease.Version);
                }
                _ => println!("no updates"),
            }
            return Ok(());
        }

        if args[0] == "download" {
            let source = sources::FileSource::new(&args[1]);
            let um = UpdateManager::new(source, None, None)?;
            match um.check_for_updates()? {
                UpdateCheck::UpdateAvailable(info) => {
                    um.download_updates(&info, None)?;
                }
                _ => {
                    println!("no updates");
                    process::exit(-1);
                }
            }
            return Ok(());
        }

        if args[0] == "apply" {
            let source = sources::FileSource::new(&args[1]);
            let um = UpdateManager::new(source, None, None)?;
            match um.get_update_pending_restart() {
                Some(asset) => {
                    println!("applying...");
                    um.apply_updates_and_restart_with_args(&asset, vec!["test", "args !!"])?;
                }
                None => {
                    println!("not pending restart");
                    process::exit(-1);
                }
            }
            return Ok(());
        }
    }

    Ok(())
}

fn append_line(path: &Path, line: &str) {
    if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{line}");
    }
}

fn record_association(dir: &Path, file: &str, hook: &str, value: &str) {
    append_line(&dir.join(file), &format!("{hook}: {value}"));
    append_line(&dir.join("assoc-pid"), &format!("Pid: {}", process::id()));
}

/// The `ipc-*` commands used by the C# `IpcInteropTests` to prove that both libraries derive the same endpoint and
/// speak the same protocol. Returns the exit code, or `None` when `args` is not an IPC command.
fn run_ipc_command(args: &[String]) -> Option<i32> {
    let command = args.first()?;
    if !command.starts_with("ipc-") {
        return None;
    }
    Some(match ipc_command(command, &args[1..]) {
        Ok(code) => code,
        Err(e) => {
            eprintln!("{command} failed: {e}");
            2
        }
    })
}

fn ipc_command(command: &str, args: &[String]) -> Result<i32> {
    match (command, args) {
        ("ipc-endpoint", [app_id, root]) => {
            let ep = IpcEndpoint::for_app(app_id, Path::new(root))?;
            println!("{}\n{}\n{}", ep.key, ep.address.display(), ep.lock_path.display());
            Ok(0)
        }
        ("ipc-send", [app_id, root, flag, value, rest @ ..]) if rest.len() <= 1 => {
            let ep = IpcEndpoint::for_app(app_id, Path::new(root))?;
            let mut msg = IpcMessage::new(rest.first().map(String::as_str).unwrap_or(ipc::TYPE_ASSOCIATION));
            if rest.is_empty() {
                msg.items = vec![IpcItem {
                    flag: flag.clone(),
                    value: value.clone(),
                }];
            }
            let reply = ipc::request(connect_with_retries(&ep)?, &msg, ipc::EXCHANGE_TIMEOUT)?;
            println!("{} {}", reply.message_type, reply.message.as_deref().unwrap_or(""));
            Ok(if reply.is_ack() { 0 } else { 1 })
        }
        ("ipc-serve", [app_id, root, out_file]) => {
            let ep = IpcEndpoint::for_app(app_id, Path::new(root))?;
            let lock = LockFile::try_acquire_lock_once(&ep.lock_path)?;
            let out = PathBuf::from(out_file);
            let handler = Arc::new(move |mut conn: IpcConnection| {
                let out = out.clone();
                ipc::serve_request(&mut conn, move |msg| {
                    if msg.message_type != ipc::TYPE_ASSOCIATION {
                        return IpcMessage::error(ipc::ERR_UNKNOWN_MESSAGE_TYPE).reply_to(&msg);
                    }
                    for item in &msg.items {
                        append_line(&out, &format!("{} {}", item.flag, item.value));
                    }
                    IpcMessage::ack().reply_to(&msg)
                });
            });
            ipc::start_server(&ep, Some(lock), handler)?;
            println!("ready");
            wait_for_stdin_eof();
            Ok(0)
        }
        ("ipc-elect", [app_id, root, out_file, rest @ ..]) if rest.is_empty() || rest.len() == 2 => {
            let config = VelopackLocatorConfig {
                RootAppDir: PathBuf::from(root),
                PackagesDir: Path::new(root).join("packages"),
                ..Default::default()
            };
            let manifest = Manifest {
                id: app_id.clone(),
                ..Default::default()
            };
            let locator = VelopackLocator::new_with_manifest(config, manifest);
            let associations: Vec<(&'static str, String)> = rest
                .chunks(2)
                .map(|pair| {
                    let flag = if pair[0].eq_ignore_ascii_case(HOOK_CLI_FILE_ASSOC) {
                        HOOK_CLI_FILE_ASSOC
                    } else {
                        HOOK_CLI_URL_PROTO
                    };
                    (flag, pair[1].clone())
                })
                .collect();
            match elect(&locator, &associations) {
                Election::Forwarded => println!("Forwarded"),
                Election::Standalone => println!("Standalone"),
                Election::Server(ep, lock) => {
                    println!("Server");
                    let (file_out, url_out) = (PathBuf::from(out_file), PathBuf::from(out_file));
                    start(
                        ep,
                        lock,
                        Some(Arc::new(move |v| append_line(&file_out, &format!("{HOOK_CLI_FILE_ASSOC} {v}")))),
                        Some(Arc::new(move |v| append_line(&url_out, &format!("{HOOK_CLI_URL_PROTO} {v}")))),
                    );
                    println!("ready");
                    wait_for_stdin_eof();
                }
            }
            Ok(0)
        }
        _ => bail!(
            "usage: ipc-endpoint <appId> <root> | ipc-send <appId> <root> <flag> <value> [type] | \
             ipc-serve <appId> <root> <outFile> | ipc-elect <appId> <root> <outFile> [<flag> <value>]"
        ),
    }
}

fn connect_with_retries(ep: &IpcEndpoint) -> Result<IpcConnection> {
    let deadline = Instant::now() + Duration::from_secs(5);
    loop {
        if let Some(conn) = ipc::connect(ep)? {
            return Ok(conn);
        }
        if Instant::now() >= deadline {
            bail!("no server at {}", ep.address.display());
        }
        std::thread::sleep(Duration::from_millis(50));
    }
}

/// Blocks until the parent closes our stdin (or 60 s pass), keeping the server alive meanwhile.
fn wait_for_stdin_eof() {
    let _ = std::io::stdout().flush();
    let (tx, rx) = mpsc::channel();
    std::thread::spawn(move || {
        let mut sink = Vec::new();
        let _ = std::io::stdin().read_to_end(&mut sink);
        let _ = tx.send(());
    });
    let _ = rx.recv_timeout(Duration::from_secs(60));
}
