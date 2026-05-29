# Shared titles
title-update = { $app_title } Update
title-setup = { $app_title } Setup
title-uninstall = { $app_title } Deinstallation
error-title = { $program_name } Fehler

# Shared buttons
btn-cancel = Abbrechen
btn-install-update = Update installieren
btn-install = Installieren
btn-update = Aktualisieren
btn-downgrade = Downgrade
btn-repair = Reparieren
btn-open-log = Protokoll öffnen
btn-open-install-dir = Installationsverzeichnis öffnen
btn-ok = OK
btn-hide = Ausblenden
# Elevation (dialogs_common.rs)
elevate-header = Administratorberechtigung erforderlich
elevate-body = { $app_title } benötigt Administratorberechtigungen, um Version { $app_version } zu installieren. Möchten Sie das Update fortsetzen?

# Restart required (prerequisite.rs)
restart-header = Neustart erforderlich
restart-body = Ihr Computer muss neu gestartet werden, bevor das Setup fortgesetzt werden kann. Bitte starten Sie den Computer neu und führen Sie das Setup erneut aus.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Zusätzliche Komponenten erforderlich
missing-deps-body = { $app_title } benötigt zunächst die Installation von Folgendem: { $deps }. Möchten Sie diese jetzt herunterladen und installieren?

# Uninstall with errors (uninstall)
uninstall-errors-header = Deinstallation mit Problemen abgeschlossen
uninstall-errors-body = { $app_title } wurde deinstalliert, aber einige Dateien oder Ordner konnten nicht entfernt werden. Sie können sie manuell löschen oder die Anwendung erneut installieren und die Deinstallation wiederholen.
uninstall-errors-log = Details wurden gespeichert unter: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } ist bereits installiert
overwrite-repair-body = Diese Anwendung ist bereits auf Ihrem Computer installiert. Falls sie nicht korrekt funktioniert, können Sie versuchen, sie durch eine Neuinstallation zu reparieren.
overwrite-older-installed = { $app_title } ist bereits installiert
overwrite-update-body = Version { $old_version } ist derzeit installiert. Möchten Sie auf Version { $app_version } aktualisieren?
overwrite-newer-installed = Eine neuere Version von { $app_title } ist bereits installiert
overwrite-downgrade-body = Version { $old_version } ist derzeit installiert und ist neuer als dieser Installer. Ein Downgrade wird nicht empfohlen und kann zu Problemen führen. Möchten Sie trotzdem fortfahren?
overwrite-footer = Installiert unter: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Deinstallation abgeschlossen
uninstall-body = Die Anwendung wurde erfolgreich von Ihrem Computer entfernt.

# Install hook failed (install.rs)
install-hook-header = Installation teilweise erfolgreich
install-hook-body = Die Installation wurde abgeschlossen, aber einige Schritte sind möglicherweise fehlgeschlagen. Falls die Anwendung nicht korrekt funktioniert, können Sie sie erneut installieren oder den Anwendungsautor kontaktieren.

# Splash fallback (splash.rs)
splash-header = { $app_title } wird installiert
splash-body = { $app_title } { $app_version } wird eingerichtet, bitte warten...

# Dependency download (prerequisite.rs)
deps-download-header = Erforderliche Komponente wird heruntergeladen
deps-download-body = { $dep_name } wird heruntergeladen, bitte warten...

# Apply progress (apply_*_impl.rs)
apply-header = Update wird installiert
apply-body = Aktualisierung auf Version { $app_version }, bitte warten...
progress-cancelling = Abbrechen...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installation beschädigt
start-corrupt-body = Diese Anwendung kann nicht gestartet werden, da einige ihrer Dateien fehlen oder beschädigt sind. Bitte installieren Sie die Anwendung erneut, um dies zu beheben.

# Generic error
error-header = Ein Fehler ist aufgetreten

# Setup error (wix msi)
setup-error-header = Setup konnte nicht fortgesetzt werden

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Setup
msi-btn-back = &Zurück
msi-btn-next = &Weiter
msi-btn-cancel = Abbrechen
msi-btn-finish = &Fertig stellen
msi-btn-ok = OK
msi-btn-yes = &Ja
msi-btn-no = &Nein
msi-btn-retry = &Wiederholen
msi-btn-ignore = &Ignorieren

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Willkommen beim { $app_title } Setup-Assistenten
msi-welcome-description = Der Setup-Assistent installiert { $app_title } auf Ihrem Computer. Klicken Sie auf "Weiter", um fortzufahren, oder auf "Abbrechen", um den Setup-Assistenten zu beenden.
msi-welcome-update-description = Der Setup-Assistent aktualisiert { $app_title } auf Ihrem Computer. Klicken Sie auf "Weiter", um fortzufahren, oder auf "Abbrechen", um den Setup-Assistenten zu beenden.

# MSI Installer UI - Exit Dialog
msi-exit-title = Der { $app_title } Setup-Assistent wurde abgeschlossen
msi-exit-description = Klicken Sie auf "Fertig stellen", um den Setup-Assistenten zu beenden.
msi-exit-launch-checkbox = { $app_title } starten

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Willkommen beim { $app_title } Setup-Assistenten
msi-prepare-description = Bitte warten Sie, während der Setup-Assistent die Installation vorbereitet.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Endbenutzer-Lizenzvereinbarung
msi-license-description = Bitte lesen Sie die folgende Lizenzvereinbarung sorgfältig durch.
msi-license-checkbox = Ich &stimme den Bedingungen der Lizenzvereinbarung zu

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installationsumfang
msi-scope-description = Wählen Sie den Installationsumfang.
msi-scope-per-user = Nur für &Sie installieren
msi-scope-per-machine = Für &alle Benutzer installieren
msi-scope-per-user-description = Installation nur für den aktuellen Benutzer
msi-scope-no-per-user-description = Erfordert Administratorberechtigungen
msi-scope-per-machine-description = Erfordert Administratorberechtigungen

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Bereit zur Installation von { $app_title }
msi-ready-install-text = Klicken Sie auf "Installieren", um die Installation zu starten. Klicken Sie auf "Zurück", um Ihre Installationseinstellungen zu überprüfen oder zu ändern.
msi-ready-change-title = Bereit zur Änderung von { $app_title }
msi-ready-change-text = Klicken Sie auf "Ändern", um die Änderung der Installation zu starten. Klicken Sie auf "Zurück", um Ihre Installationseinstellungen zu überprüfen oder zu ändern.
msi-ready-repair-title = Bereit zur Reparatur von { $app_title }
msi-ready-repair-text = Klicken Sie auf "Reparieren", um die Reparatur zu starten. Klicken Sie auf "Zurück", um Ihre Installationseinstellungen zu überprüfen oder zu ändern.
msi-ready-remove-title = Bereit zur Entfernung von { $app_title }
msi-ready-remove-text = Klicken Sie auf "Entfernen", um { $app_title } von Ihrem Computer zu entfernen. Klicken Sie auf "Zurück", um Ihre Installationseinstellungen zu überprüfen oder zu ändern.
msi-ready-update-title = Bereit zum Aktualisieren von { $app_title }
msi-ready-update-text = Klicken Sie auf "Aktualisieren", um das Update zu starten. Klicken Sie auf "Zurück", um Ihre Installationseinstellungen zu überprüfen oder zu ändern.
msi-ready-btn-install = &Installieren
msi-ready-btn-change = &Ändern
msi-ready-btn-repair = Re&parieren
msi-ready-btn-remove = &Entfernen
msi-ready-btn-update = &Aktualisieren

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } wird installiert
msi-progress-installing-text = Bitte warten Sie, während der Setup-Assistent { $app_title } installiert.
msi-progress-changing-title = { $app_title } wird geändert
msi-progress-changing-text = Bitte warten Sie, während der Setup-Assistent { $app_title } ändert.
msi-progress-repairing-title = { $app_title } wird repariert
msi-progress-repairing-text = Bitte warten Sie, während der Setup-Assistent { $app_title } repariert.
msi-progress-removing-title = { $app_title } wird entfernt
msi-progress-removing-text = Bitte warten Sie, während der Setup-Assistent { $app_title } entfernt.
msi-progress-updating-title = { $app_title } wird aktualisiert
msi-progress-updating-text = Bitte warten Sie, während der Setup-Assistent { $app_title } aktualisiert.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Willkommen beim { $app_title } Setup-Assistenten
msi-maint-welcome-description = Der Setup-Assistent ermöglicht es Ihnen, { $app_title } zu reparieren oder zu entfernen. Klicken Sie auf "Weiter", um fortzufahren, oder auf "Abbrechen", um den Setup-Assistenten zu beenden.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Ändern, Reparieren oder Entfernen der Installation
msi-maint-type-description = Wählen Sie den gewünschten Vorgang aus.
msi-maint-change-button = &Ändern...
msi-maint-change-tooltip = Ändern...
msi-maint-change-text = Ermöglicht Benutzern, die zu installierenden Programmfunktionen zu ändern und einzelne Funktionen zu ändern.
msi-maint-change-disabled = Ändern ist derzeit deaktiviert.
msi-maint-repair-button = Re&parieren
msi-maint-repair-tooltip = Reparieren
msi-maint-repair-text = Repariert Fehler der letzten Installation - behebt fehlende oder beschädigte Dateien, Verknüpfungen und Registrierungseinträge.
msi-maint-repair-disabled = Reparieren ist derzeit deaktiviert.
msi-maint-remove-button = &Entfernen
msi-maint-remove-tooltip = Entfernen
msi-maint-remove-text = Entfernt { $app_title } von Ihrem Computer.
msi-maint-remove-disabled = Entfernen ist derzeit deaktiviert.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Möchten Sie die Installation von { $app_title } wirklich abbrechen?

# MSI Installer UI - Browse Dialog
msi-browse-title = Aktuellen Zielordner ändern
msi-browse-description = Suchen Sie den Zielordner.
msi-browse-combo-label = &Suchen in:
msi-browse-path-label = &Ordnername:
msi-browse-up-tooltip = Eine Ebene nach oben
msi-browse-new-folder-tooltip = Neuen Ordner erstellen

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Das angegebene Zielverzeichnis ist entweder ungültig oder befindet sich auf einem nicht unterstützten Laufwerkstyp.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Speicherplatzbedarf
msi-disk-cost-description = Der für die Installation der ausgewählten Funktionen erforderliche Speicherplatz.
msi-disk-cost-text = Die hervorgehobenen Volumes verfügen nicht über genügend Speicherplatz für die aktuell ausgewählten Funktionen. Sie können entweder einige Dateien von den hervorgehobenen Volumes entfernen, weniger Funktionen auf das/die lokale(n) Laufwerk(e) installieren oder andere Ziellaufwerk(e) auswählen.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Installer-Information

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Der { $app_title } Setup-Assistent wurde vorzeitig beendet
msi-fatal-description1 = Das Setup von { $app_title } wurde unterbrochen. Ihr System wurde nicht verändert. Um dieses Programm zu einem späteren Zeitpunkt zu installieren, führen Sie das Setup bitte erneut aus.
msi-fatal-description2 = Klicken Sie auf "Fertig stellen", um den Setup-Assistenten zu beenden.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Der { $app_title } Setup-Assistent wurde unterbrochen
msi-user-exit-description1 = Das Setup von { $app_title } wurde unterbrochen. Ihr System wurde nicht verändert. Um dieses Programm zu einem späteren Zeitpunkt zu installieren, führen Sie das Setup bitte erneut aus.
msi-user-exit-description2 = Klicken Sie auf "Fertig stellen", um den Setup-Assistenten zu beenden.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Dateien werden verwendet
msi-files-in-use-description = Einige Dateien, die aktualisiert werden müssen, werden zurzeit verwendet.
msi-files-in-use-text = Folgende Anwendungen verwenden Dateien, die von diesem Setup aktualisiert werden müssen. Schließen Sie diese Anwendungen und klicken Sie dann auf "Wiederholen", um die Installation fortzusetzen, oder auf "Abbrechen", um sie zu beenden.
msi-files-in-use-exit = Be&enden

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Dateien werden verwendet
msi-rm-files-in-use-description = Einige Dateien, die aktualisiert werden müssen, werden zurzeit verwendet.
msi-rm-files-in-use-text = Folgende Anwendungen verwenden Dateien, die von diesem Setup aktualisiert werden müssen. Sie können den Setup-Assistenten diese Anwendungen automatisch schließen und neu starten lassen, oder Sie können sie manuell schließen und auf "OK" klicken, um die Installation fortzusetzen.
msi-rm-files-in-use-use-rm = Anwendungen automatisch &schließen und nach Abschluss des Setups neu starten.
msi-rm-files-in-use-dont-use-rm = Anwendungen &nicht schließen. (Ein Neustart wird erforderlich sein.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Der { $app_title } Setup-Assistent wird fortgesetzt
msi-resume-description = Der Setup-Assistent wird die Installation von { $app_title } auf Ihrem Computer abschließen. Klicken Sie auf "Installieren", um fortzufahren, oder auf "Abbrechen", um den Setup-Assistenten zu beenden.
msi-resume-btn-install = &Installieren

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Desktop-Verknüpfung für { $app_title }
msi-start-menu-shortcut-description = Startmenü-Verknüpfung für { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Wichtige Informationen
msi-readme-description = Bitte lesen Sie die folgenden Informationen, bevor Sie fortfahren.
