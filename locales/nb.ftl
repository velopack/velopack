# Shared titles
title-update = { $app_title } Oppdatering
title-setup = { $app_title } Installasjon
title-uninstall = { $app_title } Avinstallasjon
error-title = { $program_name } Feil

# Shared buttons
btn-cancel = Avbryt
btn-install-update = Installer oppdatering
btn-install = Installer
btn-update = Oppdater
btn-downgrade = Nedgrader
btn-repair = Reparer
btn-open-log = Åpne logg
btn-open-install-dir = Åpne installasjonsmappe
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Administratorrettigheter kreves
elevate-body = { $app_title } trenger administratorrettigheter for å installere versjon { $app_version }. Tillate at denne oppdateringen fortsetter?

# Restart required (prerequisite.rs)
restart-header = Omstart kreves
restart-body = Datamaskinen må startes på nytt før installasjonen kan fortsette. Start datamaskinen på nytt og kjør installasjonen igjen.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Ekstra komponenter kreves
missing-deps-body = { $app_title } trenger at følgende installeres først: { $deps }. Vil du laste ned og installere dem nå?

# Uninstall with errors (uninstall)
uninstall-errors-header = Avinstallasjon fullført med problemer
uninstall-errors-body = { $app_title } ble avinstallert, men noen filer eller mapper kunne ikke fjernes. Du kan slette dem manuelt, eller installere programmet på nytt og prøve å avinstallere igjen.
uninstall-errors-log = Detaljer ble lagret til: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } er allerede installert
overwrite-repair-body = Dette programmet er allerede installert på datamaskinen din. Hvis det ikke fungerer som det skal, kan du prøve å reparere det ved å installere det på nytt.
overwrite-older-installed = { $app_title } er allerede installert
overwrite-update-body = Versjon { $old_version } er installert for øyeblikket. Vil du oppdatere til versjon { $app_version }?
overwrite-newer-installed = En nyere versjon av { $app_title } er allerede installert
overwrite-downgrade-body = Versjon { $old_version } er installert for øyeblikket, som er nyere enn denne installasjonen. Nedgradering anbefales ikke og kan forårsake problemer. Fortsette likevel?
overwrite-footer = Installert på: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Avinstallasjon fullført
uninstall-body = Programmet har blitt fjernet fra datamaskinen.

# Install hook failed (install.rs)
install-hook-header = Installasjonen lyktes delvis
install-hook-body = Installasjonen er fullført, men noen trinn kan ha mislyktes. Hvis programmet ikke fungerer som det skal, kan du prøve å installere det på nytt eller kontakte programmets utgiver.

# Splash fallback (splash.rs)
splash-header = Installerer { $app_title }
splash-body = Konfigurerer { $app_title } { $app_version }, vennligst vent...

# Dependency download (prerequisite.rs)
deps-download-header = Laster ned nødvendig komponent
deps-download-body = Laster ned { $dep_name }, vennligst vent...

# Apply progress (apply_*_impl.rs)
apply-header = Installerer oppdatering
apply-body = Oppdaterer til versjon { $app_version }, vennligst vent...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installasjonen er skadet
start-corrupt-body = Dette programmet kan ikke starte fordi noen av filene mangler eller er skadet. Installer programmet på nytt for å løse dette.

# Generic error
error-header = Noe gikk galt

# Setup error (wix msi)
setup-error-header = Installasjonen kunne ikke fortsette

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Installasjon
msi-btn-back = &Tilbake
msi-btn-next = &Neste
msi-btn-cancel = Avbryt
msi-btn-finish = &Fullfør
msi-btn-ok = OK
msi-btn-yes = &Ja
msi-btn-no = &Nei
msi-btn-retry = &Prøv på nytt
msi-btn-ignore = &Ignorer

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Velkommen til installasjonsveiviseren for { $app_title }
msi-welcome-description = Installasjonsveiviseren vil installere { $app_title } på datamaskinen din. Klikk Neste for å fortsette eller Avbryt for å avslutte installasjonsveiviseren.
msi-welcome-update-description = Installasjonsveiviseren vil oppdatere { $app_title } på datamaskinen din. Klikk Neste for å fortsette eller Avbryt for å avslutte installasjonsveiviseren.

# MSI Installer UI - Exit Dialog
msi-exit-title = Installasjonsveiviseren for { $app_title } er fullført
msi-exit-description = Klikk knappen Fullfør for å avslutte installasjonsveiviseren.
msi-exit-launch-checkbox = Start { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Velkommen til installasjonsveiviseren for { $app_title }
msi-prepare-description = Vent mens installasjonsveiviseren forbereder å lede deg gjennom installasjonen.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Lisensavtale for sluttbrukere
msi-license-description = Les nøye gjennom lisensavtalen nedenfor.
msi-license-checkbox = Jeg &godtar vilkårene i lisensavtalen

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installasjonsomfang
msi-scope-description = Velg installasjonsomfang.
msi-scope-per-user = Installer bare for &deg
msi-scope-per-machine = Installer for &alle brukere
msi-scope-per-user-description = Installerer kun for gjeldende bruker
msi-scope-no-per-user-description = Krever administratorrettigheter
msi-scope-per-machine-description = Krever administratorrettigheter

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Klar til å installere { $app_title }
msi-ready-install-text = Klikk Installer for å starte installasjonen. Klikk Tilbake for å kontrollere eller endre noen av installasjonsinnstillingene.
msi-ready-change-title = Klar til å endre { $app_title }
msi-ready-change-text = Klikk Endre for å starte å endre installasjonen. Klikk Tilbake for å kontrollere eller endre noen av installasjonsinnstillingene.
msi-ready-repair-title = Klar til å reparere { $app_title }
msi-ready-repair-text = Klikk Reparer for å starte reparasjonen. Klikk Tilbake for å kontrollere eller endre noen av installasjonsinnstillingene.
msi-ready-remove-title = Klar til å fjerne { $app_title }
msi-ready-remove-text = Klikk Fjern for å fjerne { $app_title } fra datamaskinen. Klikk Tilbake for å kontrollere eller endre noen av installasjonsinnstillingene.
msi-ready-update-title = Klar til å oppdatere { $app_title }
msi-ready-update-text = Klikk Oppdater for å starte oppdateringen. Klikk Tilbake for å kontrollere eller endre noen av installasjonsinnstillingene.
msi-ready-btn-install = &Installer
msi-ready-btn-change = &Endre
msi-ready-btn-repair = &Reparer
msi-ready-btn-remove = &Fjern
msi-ready-btn-update = &Oppdater

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Installerer { $app_title }
msi-progress-installing-text = Vent mens installasjonsveiviseren installerer { $app_title }.
msi-progress-changing-title = Endrer { $app_title }
msi-progress-changing-text = Vent mens installasjonsveiviseren endrer { $app_title }.
msi-progress-repairing-title = Reparerer { $app_title }
msi-progress-repairing-text = Vent mens installasjonsveiviseren reparerer { $app_title }.
msi-progress-removing-title = Fjerner { $app_title }
msi-progress-removing-text = Vent mens installasjonsveiviseren fjerner { $app_title }.
msi-progress-updating-title = Oppdaterer { $app_title }
msi-progress-updating-text = Vent mens installasjonsveiviseren oppdaterer { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Velkommen til installasjonsveiviseren for { $app_title }
msi-maint-welcome-description = Installasjonsveiviseren lar deg reparere eller fjerne { $app_title }. Klikk Neste for å fortsette eller Avbryt for å avslutte installasjonsveiviseren.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Endre, reparer eller fjern installasjonen
msi-maint-type-description = Angi operasjonen du vil utføre.
msi-maint-change-button = &Endre
msi-maint-change-tooltip = Endre installasjonen
msi-maint-change-text = Lar brukere endre hvilke programfunksjoner som er installert og endre individuelle funksjoner.
msi-maint-change-disabled = Endre er for øyeblikket deaktivert.
msi-maint-repair-button = &Reparer
msi-maint-repair-tooltip = Reparer installasjonen
msi-maint-repair-text = Reparerer feil i den siste installasjonen - retter opp manglende eller skadede filer, snarveier og registeroppføringer.
msi-maint-repair-disabled = Reparer er for øyeblikket deaktivert.
msi-maint-remove-button = &Fjern
msi-maint-remove-tooltip = Fjern installasjonen
msi-maint-remove-text = Fjerner { $app_title } fra datamaskinen.
msi-maint-remove-disabled = Fjern er for øyeblikket deaktivert.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Er du sikker på at du vil avbryte installasjonen av { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Endre gjeldende målmappe
msi-browse-description = Bla til målmappen.
msi-browse-combo-label = &Søk i:
msi-browse-path-label = &Mappenavn:
msi-browse-up-tooltip = Opp ett nivå
msi-browse-new-folder-tooltip = Opprett en ny mappe

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Den angitte målmappen er enten ugyldig eller på en stasjonstype som ikke støttes.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Krav til diskplass
msi-disk-cost-description = Diskplassen som kreves for installasjon av de valgte funksjonene.
msi-disk-cost-text = De merkede volumene har ikke nok ledig diskplass til de valgte funksjonene. Du kan enten fjerne noen filer fra de merkede volumene, velge å installere færre funksjoner til de lokale stasjonene eller velge andre målstasjoner.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Installasjonsinformasjon

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Installasjonsveiviseren for { $app_title } ble avsluttet for tidlig
msi-fatal-description1 = Installasjonen av { $app_title } ble avbrutt. Systemet ditt har ikke blitt endret. For å installere dette programmet senere, kjør installasjonen på nytt.
msi-fatal-description2 = Klikk knappen Fullfør for å avslutte installasjonsveiviseren.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Installasjonsveiviseren for { $app_title } ble avbrutt
msi-user-exit-description1 = Installasjonen av { $app_title } ble avbrutt. Systemet ditt har ikke blitt endret. For å installere dette programmet senere, kjør installasjonen på nytt.
msi-user-exit-description2 = Klikk knappen Fullfør for å avslutte installasjonsveiviseren.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Filer i bruk
msi-files-in-use-description = Noen av filene som må oppdateres, er for tiden i bruk.
msi-files-in-use-text = Følgende programmer bruker filer som må oppdateres av denne installasjonen. Lukk disse programmene og klikk deretter Prøv på nytt for å fortsette installasjonen eller Avbryt for å avslutte.
msi-files-in-use-exit = &Avslutt

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Filer i bruk
msi-rm-files-in-use-description = Noen av filene som må oppdateres, er for tiden i bruk.
msi-rm-files-in-use-text = Følgende programmer bruker filer som må oppdateres av denne installasjonen. Du kan la installasjonsveiviseren automatisk lukke og prøve å starte disse programmene på nytt, eller du kan lukke dem manuelt og klikke OK for å fortsette installasjonen.
msi-rm-files-in-use-use-rm = &Lukk programmene automatisk og prøv å starte dem på nytt etter at installasjonen er fullført.
msi-rm-files-in-use-dont-use-rm = &Ikke lukk programmer. (En omstart vil være nødvendig.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Gjenopptar installasjonsveiviseren for { $app_title }
msi-resume-description = Installasjonsveiviseren vil fullføre installasjonen av { $app_title } på datamaskinen. Klikk Installer for å fortsette eller Avbryt for å avslutte installasjonsveiviseren.
msi-resume-btn-install = &Installer

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Skrivebordssnarvei for { $app_title }
msi-start-menu-shortcut-description = Startmeny-snarvei for { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Viktig informasjon
msi-readme-description = Vennligst les følgende informasjon før du fortsetter.
