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
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Administrator Permission Required
elevate-body = { $app_title } needs administrator permission to install version { $app_version }. Allow this update to continue?

# Restart required (prerequisite.rs)
restart-header = Restart Required
restart-body = Your computer needs to restart before setup can continue. Please restart your computer and run setup again.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Additional Components Required
missing-deps-body = { $app_title } needs the following to be installed first: { $deps }. Would you like to download and install them now?

# Uninstall with errors (uninstall)
uninstall-errors-header = Uninstall Finished with Problems
uninstall-errors-body = { $app_title } was uninstalled, but some files or folders could not be removed. You can delete them manually, or reinstall the application and try uninstalling again.
uninstall-errors-log = Details were saved to: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } is already installed
overwrite-repair-body = This application is already installed on your computer. If it is not working correctly, you can try repairing it by reinstalling.
overwrite-older-installed = { $app_title } is already installed
overwrite-update-body = Version { $old_version } is currently installed. Would you like to update to version { $app_version }?
overwrite-newer-installed = A newer version of { $app_title } is already installed
overwrite-downgrade-body = Version { $old_version } is currently installed, which is newer than this installer. Downgrading is not recommended and may cause problems. Continue anyway?
overwrite-footer = Installed at: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Uninstall Complete
uninstall-body = The application has been successfully removed from your computer.

# Install hook failed (install.rs)
install-hook-header = Install Partially Succeeded
install-hook-body = Installation has completed, but some steps may have failed. If the application does not work correctly you can try re-installing or contacting the application author.

# Splash fallback (splash.rs)
splash-header = Installing { $app_title }
splash-body = Setting up { $app_title } { $app_version }, please wait...

# Dependency download (prerequisite.rs)
deps-download-header = Downloading Required Component
deps-download-body = Downloading { $dep_name }, please wait...

# Apply progress (apply_*_impl.rs)
apply-header = Installing Update
apply-body = Updating to version { $app_version }, please wait...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installation Damaged
start-corrupt-body = This application cannot start because some of its files are missing or damaged. Please reinstall the application to fix this.

# Generic error
error-header = Something Went Wrong

# Setup error (wix msi)
setup-error-header = Setup Could Not Continue

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Setup
msi-btn-back = &Back
msi-btn-next = &Next
msi-btn-cancel = Cancel
msi-btn-finish = &Finish
msi-btn-ok = OK
msi-btn-yes = &Yes
msi-btn-no = &No
msi-btn-retry = &Retry
msi-btn-ignore = &Ignore

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Welcome to the { $app_title } Setup Wizard
msi-welcome-description = The Setup Wizard will install { $app_title } on your computer. Click Next to continue or Cancel to exit the Setup Wizard.
msi-welcome-update-description = The Setup Wizard will update { $app_title } on your computer. Click Next to continue or Cancel to exit the Setup Wizard.

# MSI Installer UI - Exit Dialog
msi-exit-title = Completed the { $app_title } Setup Wizard
msi-exit-description = Click the Finish button to exit the Setup Wizard.
msi-exit-launch-checkbox = Launch { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Welcome to the { $app_title } Setup Wizard
msi-prepare-description = Please wait while the Setup Wizard prepares to guide you through the installation.

# MSI Installer UI - License Agreement Dialog
msi-license-title = End-User License Agreement
msi-license-description = Please read the following license agreement carefully.
msi-license-checkbox = I &accept the terms in the License Agreement

# MSI Installer UI - Readme Dialog
msi-readme-title = Readme Information
msi-readme-description = Please read the following information before continuing.

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installation Scope
msi-scope-description = Select the installation scope.
msi-scope-per-user = Install just for &you
msi-scope-per-machine = Install for &all users
msi-scope-per-user-description = Installs for the current user only
msi-scope-no-per-user-description = Requires administrator privileges
msi-scope-per-machine-description = Requires administrator privileges

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Ready to install { $app_title }
msi-ready-install-text = Click Install to begin the installation. Click Back to review or change any of your installation settings.
msi-ready-change-title = Ready to change { $app_title }
msi-ready-change-text = Click Change to begin changing the installation. Click Back to review or change any of your installation settings.
msi-ready-repair-title = Ready to repair { $app_title }
msi-ready-repair-text = Click Repair to begin the repair. Click Back to review or change any of your installation settings.
msi-ready-remove-title = Ready to remove { $app_title }
msi-ready-remove-text = Click Remove to remove { $app_title } from your computer. Click Back to review or change any of your installation settings.
msi-ready-update-title = Ready to update { $app_title }
msi-ready-update-text = Click Update to begin the update. Click Back to review or change any of your installation settings.
msi-ready-btn-install = &Install
msi-ready-btn-change = &Change
msi-ready-btn-repair = &Repair
msi-ready-btn-remove = &Remove
msi-ready-btn-update = &Update

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Installing { $app_title }
msi-progress-installing-text = Please wait while the Setup Wizard installs { $app_title }.
msi-progress-changing-title = Changing { $app_title }
msi-progress-changing-text = Please wait while the Setup Wizard changes { $app_title }.
msi-progress-repairing-title = Repairing { $app_title }
msi-progress-repairing-text = Please wait while the Setup Wizard repairs { $app_title }.
msi-progress-removing-title = Removing { $app_title }
msi-progress-removing-text = Please wait while the Setup Wizard removes { $app_title }.
msi-progress-updating-title = Updating { $app_title }
msi-progress-updating-text = Please wait while the Setup Wizard updates { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Welcome to the { $app_title } Setup Wizard
msi-maint-welcome-description = The Setup Wizard will allow you to repair or remove { $app_title }. Click Next to continue or Cancel to exit the Setup Wizard.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Modify, Repair, or Remove installation
msi-maint-type-description = Select the operation you wish to perform.
msi-maint-change-button = &Change...
msi-maint-change-tooltip = Change...
msi-maint-change-text = Allows users to change which program features are installed and to change individual features.
msi-maint-change-disabled = Change is currently disabled.
msi-maint-repair-button = &Repair
msi-maint-repair-tooltip = Repair
msi-maint-repair-text = Repairs errors in the most recent installation - fixes missing or corrupt files, shortcuts, and registry entries.
msi-maint-repair-disabled = Repair is currently disabled.
msi-maint-remove-button = Re&move
msi-maint-remove-tooltip = Remove
msi-maint-remove-text = Removes { $app_title } from your computer.
msi-maint-remove-disabled = Remove is currently disabled.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Are you sure you want to cancel { $app_title } installation?

# MSI Installer UI - Browse Dialog
msi-browse-title = Change current destination folder
msi-browse-description = Browse to the destination folder.
msi-browse-combo-label = &Look in:
msi-browse-path-label = Fol&der name:
msi-browse-up-tooltip = Up One Level
msi-browse-new-folder-tooltip = Create A New Folder

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = The specified destination directory is either invalid or on an unsupported drive type.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Disk Space Requirements
msi-disk-cost-description = The disk space required for the installation of the selected features.
msi-disk-cost-text = The highlighted volumes do not have enough disk space available for the currently selected features. You can either remove some files from the highlighted volumes, or choose to install less features onto local drive(s), or select different destination drive(s).

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Installer Information

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } Setup Wizard ended prematurely
msi-fatal-description1 = { $app_title } setup was interrupted. Your system has not been modified. To install this program at a later time, please run the setup again.
msi-fatal-description2 = Click the Finish button to exit the Setup Wizard.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } Setup Wizard was interrupted
msi-user-exit-description1 = { $app_title } setup was interrupted. Your system has not been modified. To install this program at a later time, please run the setup again.
msi-user-exit-description2 = Click the Finish button to exit the Setup Wizard.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Files In Use
msi-files-in-use-description = Some files that need to be updated are currently in use.
msi-files-in-use-text = The following applications are using files that need to be updated by this setup. Close these applications and then click Retry to continue the installation or Cancel to exit it.
msi-files-in-use-exit = E&xit

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Files In Use
msi-rm-files-in-use-description = Some files that need to be updated are currently in use.
msi-rm-files-in-use-text = The following applications are using files that need to be updated by this setup. You can let Setup Wizard automatically close and attempt to restart these applications or you can close them manually and click OK to continue the installation.
msi-rm-files-in-use-use-rm = Automatically &close applications and attempt to restart them after setup is complete.
msi-rm-files-in-use-dont-use-rm = &Do not close applications. (A reboot will be required.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Resuming the { $app_title } Setup Wizard
msi-resume-description = The Setup Wizard will complete the installation of { $app_title } on your computer. Click Install to continue or Cancel to exit the Setup Wizard.
msi-resume-btn-install = &Install

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Desktop shortcut for { $app_title }
msi-start-menu-shortcut-description = Start Menu shortcut for { $app_title }
