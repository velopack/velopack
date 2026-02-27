# Buttons
btn-cancel = Cancel
btn-install-update = Install Update
btn-open-log = Open Log
btn-open-install-dir = Open Install Directory

# Generic errors (cli_host.rs)
error-title = { $program_name } Error

# Elevation (dialogs_common.rs)
elevate-title = { $app } Update
elevate-body = { $app } would like to update to version { $version }, but requires elevated permissions to do so. Would you like to proceed?

# Restart required (prerequisite.rs)
restart-title = { $app } Setup { $version }
restart-header = Restart Required
restart-body = A restart is required before Setup can continue. Please restart your computer and try again.

# Update missing dependencies (prerequisite.rs)
update-deps-title = { $app } Update
update-deps-header = { $app } would like to update from { $from } to { $to }
update-deps-body = { $app } { $to } has missing dependencies which need to be installed: { $deps }, would you like to continue?
update-deps-button = Install & Update

# Setup missing dependencies (prerequisite.rs)
setup-deps-title = { $app } Setup { $version }
setup-deps-header = { $app } has missing system dependencies.
setup-deps-body = { $app } requires the following packages to be installed: { $deps }, would you like to continue?
setup-deps-button = Install

# Uninstall with errors (uninstall)
uninstall-errors-title = { $app } Uninstall
uninstall-errors-header = { $app } uninstall has completed with errors.
uninstall-errors-body = There may be left-over files or directories on your system. You can attempt to remove these manually or re-install the application and try again.
uninstall-errors-log = Log file: { $path }

# Process locking (locksmith.rs)
locking-title = { $app } Update { $version }
locking-header = { $app } Update
locking-body = There are programs ({ $processes }) preventing the { $app } update from proceeding. You can press Continue to have this updater attempt to close them automatically, or if you've closed them yourself press Retry for the updater to check again.
locking-retry = Retry
locking-continue = Continue
locking-cancel = Cancel

# Overwrite/repair dialog (install.rs)
overwrite-title = { $app } Setup { $version }
overwrite-already-installed = { $app } is already installed.
overwrite-repair-body = This application is installed on your computer. If it is not functioning correctly, you can attempt to repair it.
overwrite-repair-button = Repair
overwrite-older-installed = An older version of { $app } is installed.
overwrite-update-body = Would you like to update from { $old } to { $version }?
overwrite-update-button = Update
overwrite-newer-installed = A newer version of { $app } is installed.
overwrite-downgrade-body = You already have { $old } installed. Would you like to downgrade this application to an older version?
overwrite-downgrade-button = Downgrade
overwrite-footer-default = The install directory is %LocalAppData%\{ $id }
overwrite-footer-custom = The install directory is { $path }

# Uninstall complete (uninstall.rs)
uninstall-title = { $app } Uninstall
uninstall-body = The application was successfully uninstalled.

# Install hook failed (install.rs)
install-hook-title = { $app } Setup { $id }
install-hook-body = Installation has completed, but the application install hook failed. It may not have installed correctly.

# Splash fallback (splash.rs)
splash-setup-title = { $app } Setup
splash-setup-body = Installing { $app }...

# Apply progress (apply_*_impl.rs)
apply-title = { $app } Update
apply-body = Installing update { $version }...

# Start error (start_windows_impl.rs)
start-corrupt-header = Unable to start app
start-corrupt-body = This app installation has been corrupted and cannot be started. Please re-install the app.
