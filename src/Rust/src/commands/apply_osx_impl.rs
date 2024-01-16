use crate::shared::{
    self,
    bundle::{self, Manifest},
    dialogs,
};
use anyhow::{bail, Result};
use std::{fs, path::PathBuf, process::Command};

pub fn apply_package_impl<'a>(root_path: &PathBuf, app: &Manifest, pkg: &PathBuf, _runhooks: bool) -> Result<Manifest> {
    let tmp_path_new = format!("/tmp/velopack/{}/{}", app.id, shared::random_string(8));
    let tmp_path_old = format!("/tmp/velopack/{}/{}", app.id, shared::random_string(8));
    let bundle = bundle::load_bundle_from_file(pkg)?;
    let manifest = bundle.read_manifest()?;

    let action: Result<()> = (|| {
        // 1. extract the bundle to a temp dir
        fs::create_dir_all(&tmp_path_new)?;
        info!("Extracting bundle to {}", &tmp_path_new);
        bundle.extract_lib_contents_to_path(&tmp_path_new, |_| {})?;

        // 2. attempt to replace the current bundle with the new one
        let result: Result<()> = (|| {
            info!("Replacing bundle at {}", &root_path.to_string_lossy());
            fs::rename(&root_path, &tmp_path_old)?;
            fs::rename(&tmp_path_new, &root_path)?;
            Ok(())
        })();

        match result {
            Ok(()) => {
                info!("Bundle extracted successfully to {}", &root_path.to_string_lossy());
                Ok(())
            }
            Err(e) => {
                // 3. if fails for permission error, try again escallated via osascript
                if shared::is_error_permission_denied(&e) {
                    error!("A permissions error occurred ({}), will attempt to elevate permissions and try again...", e);
                    dialogs::ask_user_to_elevate(&manifest)?;
                    let script = format!(
                        "do shell script \"mv -f '{}' '{}' && mv -f '{}' '{}'\" with administrator privileges",
                        &root_path.to_string_lossy(),
                        &tmp_path_old,
                        &tmp_path_new,
                        &root_path.to_string_lossy()
                    );
                    info!("Running elevated process via osascript: {}", script);
                    let output = Command::new("osascript").arg("-e").arg(&script).status()?;
                    if output.success() {
                        info!("Bundle applied successfully via osascript.");
                        Ok(())
                    } else {
                        bail!("elevated process failed: exited with code: {}", output);
                    }
                } else {
                    bail!("Failed to extract bundle ({})", e);
                }
            }
        }
    })();
    let _ = fs::remove_dir_all(&tmp_path_new);
    let _ = fs::remove_dir_all(&tmp_path_old);
    action?;
    Ok(manifest)
}
