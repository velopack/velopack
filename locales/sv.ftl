# Shared titles
title-update = { $app_title } Uppdatering
title-setup = { $app_title } Installation
title-uninstall = { $app_title } Avinstallation
error-title = { $program_name } Fel

# Shared buttons
btn-cancel = Avbryt
btn-install-update = Installera uppdatering
btn-install = Installera
btn-update = Uppdatera
btn-downgrade = Nedgradera
btn-repair = Reparera
btn-open-log = Öppna logg
btn-open-install-dir = Öppna installationsmapp
btn-ok = OK
btn-hide = Dölj
# Elevation (dialogs_common.rs)
elevate-header = Administratörsbehörighet krävs
elevate-body = { $app_title } behöver administratörsbehörighet för att installera version { $app_version }. Tillåt att denna uppdatering fortsätter?

# Restart required (prerequisite.rs)
restart-header = Omstart krävs
restart-body = Din dator måste startas om innan installationen kan fortsätta. Starta om datorn och kör installationen igen.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Ytterligare komponenter krävs
missing-deps-body = { $app_title } behöver att följande installeras först: { $deps }. Vill du ladda ner och installera dem nu?

# Uninstall with errors (uninstall)
uninstall-errors-header = Avinstallationen slutfördes med problem
uninstall-errors-body = { $app_title } avinstallerades, men vissa filer eller mappar kunde inte tas bort. Du kan ta bort dem manuellt eller installera om programmet och försöka avinstallera igen.
uninstall-errors-log = Detaljer sparades till: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } är redan installerat
overwrite-repair-body = Det här programmet är redan installerat på din dator. Om det inte fungerar korrekt kan du försöka reparera det genom att installera om.
overwrite-older-installed = { $app_title } är redan installerat
overwrite-update-body = Version { $old_version } är installerad för närvarande. Vill du uppdatera till version { $app_version }?
overwrite-newer-installed = En nyare version av { $app_title } är redan installerad
overwrite-downgrade-body = Version { $old_version } är installerad för närvarande, vilket är nyare än det här installationsprogrammet. Nedgradering rekommenderas inte och kan orsaka problem. Vill du fortsätta ändå?
overwrite-footer = Installerad på: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Avinstallation klar
uninstall-body = Programmet har tagits bort från din dator.

# Install hook failed (install.rs)
install-hook-header = Installationen lyckades delvis
install-hook-body = Installationen har slutförts, men några steg kan ha misslyckats. Om programmet inte fungerar korrekt kan du försöka installera om eller kontakta programmets utgivare.

# Splash fallback (splash.rs)
splash-header = Installerar { $app_title }
splash-body = Konfigurerar { $app_title } { $app_version }, vänligen vänta...

# Dependency download (prerequisite.rs)
deps-download-header = Hämtar nödvändig komponent
deps-download-body = Hämtar { $dep_name }, vänligen vänta...

# Apply progress (apply_*_impl.rs)
apply-header = Installerar uppdatering
apply-body = Uppdaterar till version { $app_version }, vänligen vänta...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installationen är skadad
start-corrupt-body = Det här programmet kan inte starta eftersom några av dess filer saknas eller är skadade. Installera om programmet för att åtgärda detta.

# Generic error
error-header = Något gick fel

# Setup error (wix msi)
setup-error-header = Installationen kunde inte fortsätta

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Installation
msi-btn-back = &Föregående
msi-btn-next = &Nästa
msi-btn-cancel = Avbryt
msi-btn-finish = &Slutför
msi-btn-ok = OK
msi-btn-yes = &Ja
msi-btn-no = N&ej
msi-btn-retry = F&örsök igen
msi-btn-ignore = Ign&orera

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Välkommen till installationsguiden för { $app_title }
msi-welcome-description = Installationsguiden installerar { $app_title } på din dator. Klicka på Nästa för att fortsätta eller Avbryt för att avsluta installationsguiden.
msi-welcome-update-description = Installationsguiden uppdaterar { $app_title } på din dator. Klicka på Nästa för att fortsätta eller Avbryt för att avsluta installationsguiden.

# MSI Installer UI - Exit Dialog
msi-exit-title = Installationsguiden för { $app_title } slutfördes
msi-exit-description = Klicka på knappen Slutför för att avsluta installationsguiden.
msi-exit-launch-checkbox = Starta { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Välkommen till installationsguiden för { $app_title }
msi-prepare-description = Vänta medan installationsguiden förbereder att guida dig genom installationen.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Licensavtal för slutanvändare
msi-license-description = Läs följande licensavtal noggrant.
msi-license-checkbox = Jag &accepterar villkoren i licensavtalet

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installationsomfattning
msi-scope-description = Välj installationsomfattning.
msi-scope-per-user = Installera bara för &dig
msi-scope-per-machine = Installera för &alla användare
msi-scope-per-user-description = Installerar endast för den aktuella användaren
msi-scope-no-per-user-description = Kräver administratörsprivilegier
msi-scope-per-machine-description = Kräver administratörsprivilegier

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Klart för installation av { $app_title }
msi-ready-install-text = Klicka på Installera för att påbörja installationen. Klicka på Föregående för att granska eller ändra någon installationsinställning.
msi-ready-change-title = Klart för ändring av { $app_title }
msi-ready-change-text = Klicka på Ändra för att börja ändra installationen. Klicka på Föregående för att granska eller ändra någon installationsinställning.
msi-ready-repair-title = Klart för reparation av { $app_title }
msi-ready-repair-text = Klicka på Reparera för att börja reparationen. Klicka på Föregående för att granska eller ändra någon installationsinställning.
msi-ready-remove-title = Klart för borttagning av { $app_title }
msi-ready-remove-text = Klicka på Ta bort för att ta bort { $app_title } från din dator. Klicka på Föregående för att granska eller ändra någon installationsinställning.
msi-ready-update-title = Klart för uppdatering av { $app_title }
msi-ready-update-text = Klicka på Uppdatera för att börja uppdateringen. Klicka på Föregående för att granska eller ändra någon installationsinställning.
msi-ready-btn-install = &Installera
msi-ready-btn-change = &Ändra
msi-ready-btn-repair = Re&parera
msi-ready-btn-remove = &Ta bort
msi-ready-btn-update = &Uppdatera

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Installerar { $app_title }
msi-progress-installing-text = Vänta medan installationsguiden installerar { $app_title }.
msi-progress-changing-title = Ändrar { $app_title }
msi-progress-changing-text = Vänta medan installationsguiden ändrar { $app_title }.
msi-progress-repairing-title = Reparerar { $app_title }
msi-progress-repairing-text = Vänta medan installationsguiden reparerar { $app_title }.
msi-progress-removing-title = Tar bort { $app_title }
msi-progress-removing-text = Vänta medan installationsguiden tar bort { $app_title }.
msi-progress-updating-title = Uppdaterar { $app_title }
msi-progress-updating-text = Vänta medan installationsguiden uppdaterar { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Välkommen till installationsguiden för { $app_title }
msi-maint-welcome-description = Installationsguiden låter dig reparera eller ta bort { $app_title }. Klicka på Nästa för att fortsätta eller Avbryt för att avsluta installationsguiden.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Ändra, reparera eller ta bort installationen
msi-maint-type-description = Välj den åtgärd du vill utföra.
msi-maint-change-button = &Ändra
msi-maint-change-tooltip = Ändra installationen
msi-maint-change-text = Här kan du ändra hur olika programfunktioner är installerade.
msi-maint-change-disabled = Ändra är inte tillgängligt för närvarande.
msi-maint-repair-button = Re&parera
msi-maint-repair-tooltip = Reparera installationen
msi-maint-repair-text = Reparerar fel i den senaste installationen genom att åtgärda saknade eller skadade filer, genvägar och registerposter.
msi-maint-repair-disabled = Reparera är inte tillgängligt för närvarande.
msi-maint-remove-button = &Ta bort
msi-maint-remove-tooltip = Ta bort installationen
msi-maint-remove-text = Tar bort { $app_title } från din dator.
msi-maint-remove-disabled = Ta bort är inte tillgängligt för närvarande.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Är du säker på att du vill avbryta installationen av { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Byt aktuell målmapp
msi-browse-description = Bläddra till målmappen.
msi-browse-combo-label = &Sök i:
msi-browse-path-label = &Mappnamn:
msi-browse-up-tooltip = Upp en nivå
msi-browse-new-folder-tooltip = Skapa en ny mapp

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Den angivna målkatalogen är antingen ogiltig eller på en typ av enhet som inte stöds.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Diskutrymmeskrav
msi-disk-cost-description = Det diskutrymme som krävs för installation av de valda funktionerna.
msi-disk-cost-text = De markerade volymerna har inte tillräckligt med diskutrymme för de valda funktionerna. Du kan antingen ta bort några filer från de markerade volymerna, välja att installera färre funktioner till de lokala enheterna eller välja andra målenheter.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Installationsinformation

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Installationsguiden för { $app_title } avslutades för tidigt
msi-fatal-description1 = Installationen av { $app_title } avbröts. Ditt system har inte ändrats. Om du vill installera programmet vid ett senare tillfälle kan du köra installationen igen.
msi-fatal-description2 = Klicka på knappen Slutför för att avsluta installationsguiden.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Installationsguiden för { $app_title } avbröts
msi-user-exit-description1 = Installationen av { $app_title } avbröts. Ditt system har inte ändrats. Om du vill installera programmet vid ett senare tillfälle kan du köra installationen igen.
msi-user-exit-description2 = Klicka på knappen Slutför för att avsluta installationsguiden.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Filer som används
msi-files-in-use-description = Några filer som behöver uppdateras används just nu.
msi-files-in-use-text = Följande program använder filer som behöver uppdateras av den här installationen. Stäng dessa program och klicka sedan på Försök igen för att fortsätta installationen eller Avbryt för att avsluta.
msi-files-in-use-exit = A&vsluta

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Filer som används
msi-rm-files-in-use-description = Några filer som behöver uppdateras används just nu.
msi-rm-files-in-use-text = Följande program använder filer som behöver uppdateras av den här installationen. Du kan låta installationsguiden automatiskt stänga och försöka starta om dessa program eller så kan du stänga dem manuellt och klicka på OK för att fortsätta installationen.
msi-rm-files-in-use-use-rm = Stäng &automatiskt program och försök starta om dem efter att installationen är klar.
msi-rm-files-in-use-dont-use-rm = Stäng &inte programmen. (En omstart kommer att krävas.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Återupptar installationsguiden för { $app_title }
msi-resume-description = Installationsguiden slutför installationen av { $app_title } på din dator. Klicka på Installera för att fortsätta eller Avbryt för att avsluta installationsguiden.
msi-resume-btn-install = &Installera

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Skrivbordsgenväg för { $app_title }
msi-start-menu-shortcut-description = Startmenygenväg för { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Viktig information
msi-readme-description = Läs följande information innan du fortsätter.
