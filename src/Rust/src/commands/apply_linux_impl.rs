

fn apply_package_impl<'a>(
    root_path: &PathBuf,
    app: &Manifest,
    package: Option<&PathBuf>,
    exe_args: Option<Vec<&str>>,
    noelevate: bool,
    _runhooks: bool,
) -> Result<()> {
    // on linux, the current "dir" is actually an AppImage file which we need to replace.
    let pkg = package.ok_or(anyhow!("Package is required"))?;

    info!("Loading bundle from {}", pkg.to_string_lossy());
    let bundle = bundle::load_bundle_from_file(pkg)?;
    let mut tmp_path = root_path.to_string_lossy().to_string();
    tmp_path = tmp_path + "_" + shared::random_string(8).as_ref();

    info!("Extracting AppImage to temp file");

    let result: Result<()> = (|| {
        bundle.extract_zip_predicate_to_path(|z| z.ends_with(".AppImage"), &tmp_path)?;
        std::fs::set_permissions(&tmp_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;
        std::fs::rename(&tmp_path, &root_path)?;
        Ok(())
    })();

    match result {
        Ok(()) => {
            info!("AppImage extracted successfully to {}", &root_path.to_string_lossy());
        }
        Err(e) => {
            let _ = std::fs::remove_file(&tmp_path);
            if shared::is_error_permission_denied(&e) {
                error!("An error occurred {}, will attempt to elevate permissions and try again...", e);
                ask_user_to_elevate(&app, noelevate, package, exe_args)?;
            } else {
                bail!("Unable to extract AppImage ({})", e);
            }
        }
    }
    Ok(())
}

fn run_apply_elevated(package: Option<&PathBuf>, exe_args: Option<Vec<&str>>) -> Result<()> {
    // in linux, as soon as the main AppImage process exits, the fs is unmounted
    // so we need to write self to a temporary file before we can use pkexec
    let temp_path = format!("/tmp/{}_update", shared::random_string(8));
    shared::copy_own_fd_to_file(&temp_path)?;
    std::fs::set_permissions(&temp_path, <std::fs::Permissions as std::os::unix::fs::PermissionsExt>::from_mode(0o755))?;

    let path = std::env::var("APPIMAGE")?;

    let mut args: Vec<String> = Vec::new();
    args.push("env".to_string());
    args.push(format!("APPIMAGE={}", path));
    args.push(temp_path.to_owned());
    args.push("apply".to_string());
    args.push("--noelevate".to_string());

    let package = package.map(|p| p.to_string_lossy().to_string());
    if let Some(pkg) = package {
        args.push("--package".to_string());
        args.push(pkg);
    }

    if let Some(a) = exe_args {
        args.push("--".to_string());
        a.iter().for_each(|a| args.push(a.to_string()));
    }

    info!("Attempting to elevate: pkexec {:?}", args);
    let status = std::process::Command::new("pkexec").args(args).status();
    let _ = std::fs::remove_file(&temp_path);
    info!("pkexec exited with status: {}", status?);
    Ok(())
}