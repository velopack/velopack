# Shared titles
title-update = { $app } Update
title-setup = { $app } Setup
title-uninstall = { $app } Uninstall
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
elevate-body = { $app } would like to update to version { $version }, but requires elevated permissions to do so. Would you like to proceed?

# Restart required (prerequisite.rs)
restart-body = A restart is required before Setup can continue. Please restart your computer and try again.

# Missing dependencies (prerequisite.rs)
missing-deps-body = { $app } requires the following packages to be installed: { $deps }. Would you like to continue?

# Uninstall with errors (uninstall)
uninstall-errors-body = { $app } uninstall has completed with errors. There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again.
uninstall-errors-log = Log file: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app } is already installed.
overwrite-repair-body = This application is installed on your computer. If it is not functioning correctly, you can attempt to repair it.
overwrite-older-installed = An older version of { $app } is installed.
overwrite-update-body = Would you like to update from { $old } to { $version }?
overwrite-newer-installed = A newer version of { $app } is installed.
overwrite-downgrade-body = You already have { $old } installed. Would you like to downgrade this application to an older version?
overwrite-footer = The install directory is { $path }

# Uninstall complete (uninstall.rs)
uninstall-body = The application was successfully uninstalled.

# Install hook failed (install.rs)
install-hook-body = Installation has completed, but the application install hook failed. It may not have installed correctly.

# Splash fallback (splash.rs)
splash-body = Installing { $app }...

# Apply progress (apply_*_impl.rs)
apply-body = Installing update { $version }...

# Start error (start_windows_impl.rs)
start-corrupt-body = This app installation has been corrupted and cannot be started. Please re-install the app.
