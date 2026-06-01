use anyhow::Result;
use simplelog::*;
use std::{env, fs, fs::OpenOptions, io::Write, process};
use velopack::{locator, sources, UpdateCheck, UpdateManager, VelopackApp};

fn main() -> Result<()> {
    let _ = TermLogger::init(LevelFilter::Info, Config::default(), TerminalMode::Stderr, ColorChoice::Never);
    let args: Vec<String> = env::args().skip(1).collect();

    let should_auto_update = args.iter().any(|a| a.eq_ignore_ascii_case("--autoupdate"));

    VelopackApp::build().set_auto_apply_on_startup(should_auto_update).run();

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
        let exe_dir = env::current_exe()?.parent().unwrap().to_path_buf();
        let path = exe_dir.join("test_string.txt");
        match fs::read_to_string(&path) {
            Ok(s) => println!("{}", s.trim()),
            Err(_) => println!("no_test_string"),
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

    // Fallback: original behavior (log args to args.txt)
    let line = args.join(" ") + "\n";
    let mut file = OpenOptions::new().create(true).append(true).open("args.txt")?;
    file.write_all(line.as_bytes())?;
    Ok(())
}
