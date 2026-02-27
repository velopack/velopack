# Shared titles
title-update = { $app_title } Update
title-setup = { $app_title } Setup
title-uninstall = { $app_title } Uninstall
error-title = { $program_name } Error

# Shared buttons
btn-cancel = Cancel
btn-install-update = Install Update
btn-install = Install
btn-update = Update
btn-downgrade = Downgrade
btn-repair = Repair
btn-open-log = Open Log
btn-open-install-dir = Open Install Directory

# Elevation (dialogs_common.rs)
elevate-header = Elevated Permissions Required
elevate-body = { $app_title } would like to update to version { $app_version }, but requires elevated permissions to do so. Would you like to proceed?

# Restart required (prerequisite.rs)
restart-header = Restart Required
restart-body = A restart is required before Setup can continue. Please restart your computer and try again.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Missing Dependencies
missing-deps-body = { $app_title } requires the following packages to be installed: { $deps }. Would you like to continue?

# Uninstall with errors (uninstall)
uninstall-errors-header = Uninstall Completed with Errors
uninstall-errors-body = { $app_title } uninstall has completed with errors. There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again.
uninstall-errors-log = Log file: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } is already installed.
overwrite-repair-body = This application is installed on your computer. If it is not functioning correctly, you can attempt to repair it.
overwrite-older-installed = An older version of { $app_title } is installed.
overwrite-update-body = Would you like to update from { $old_version } to { $app_version }?
overwrite-newer-installed = A newer version of { $app_title } is installed.
overwrite-downgrade-body = You already have { $old_version } installed. Would you like to downgrade this application to an older version?
overwrite-footer = The install directory is { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Uninstall Complete
uninstall-body = The application was successfully uninstalled.

# Install hook failed (install.rs)
install-hook-header = Install Hook Failed
install-hook-body = Installation has completed, but the application install hook failed. It may not have installed correctly.

# Splash fallback (splash.rs)
splash-header = Installing { $app_title }
splash-body = Installing { $app_title } { $app_version }...

# Dependency download (prerequisite.rs)
deps-download-header = Downloading Dependency
deps-download-body = { $dep_name }...

# Apply progress (apply_*_impl.rs)
apply-header = Installing Update
apply-body = Installing update { $app_version }...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installation Corrupted
start-corrupt-body = This app installation has been corrupted and cannot be started. Please re-install the app.

# Generic error
error-header = An Error Has Occurred

# Setup error (wix msi)
setup-error-header = Setup Cannot Continue
