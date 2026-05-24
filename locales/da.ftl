# Shared titles
title-update = { $app_title } Opdatering
title-setup = { $app_title } Installation
title-uninstall = { $app_title } Afinstallation
error-title = { $program_name } Fejl

# Shared buttons
btn-cancel = Annuller
btn-install-update = Installer opdatering
btn-install = Installer
btn-update = Opdater
btn-downgrade = Nedgrader
btn-repair = Reparer
btn-open-log = Åbn log
btn-open-install-dir = Åbn installationsmappe

# Elevation (dialogs_common.rs)
elevate-header = Administratortilladelse kræves
elevate-body = { $app_title } har brug for administratortilladelse for at installere version { $app_version }. Tillad denne opdatering at fortsætte?

# Restart required (prerequisite.rs)
restart-header = Genstart kræves
restart-body = Din computer skal genstartes, før installationen kan fortsætte. Genstart computeren, og kør installationen igen.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Yderligere komponenter kræves
missing-deps-body = { $app_title } kræver, at følgende installeres først: { $deps }. Vil du downloade og installere dem nu?

# Uninstall with errors (uninstall)
uninstall-errors-header = Afinstallation afsluttet med problemer
uninstall-errors-body = { $app_title } blev afinstalleret, men nogle filer eller mapper kunne ikke fjernes. Du kan slette dem manuelt eller geninstallere programmet og prøve at afinstallere igen.
uninstall-errors-log = Detaljer blev gemt til: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } er allerede installeret
overwrite-repair-body = Dette program er allerede installeret på din computer. Hvis det ikke fungerer korrekt, kan du prøve at reparere det ved at geninstallere.
overwrite-older-installed = { $app_title } er allerede installeret
overwrite-update-body = Version { $old_version } er installeret i øjeblikket. Vil du opdatere til version { $app_version }?
overwrite-newer-installed = En nyere version af { $app_title } er allerede installeret
overwrite-downgrade-body = Version { $old_version } er installeret i øjeblikket, som er nyere end denne installation. Nedgradering anbefales ikke og kan forårsage problemer. Fortsæt alligevel?
overwrite-footer = Installeret på: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Afinstallation fuldført
uninstall-body = Programmet er blevet fjernet fra din computer.

# Install hook failed (install.rs)
install-hook-header = Installation lykkedes delvist
install-hook-body = Installationen er fuldført, men nogle trin er muligvis mislykkedes. Hvis programmet ikke fungerer korrekt, kan du prøve at geninstallere eller kontakte programmets udgiver.

# Splash fallback (splash.rs)
splash-header = Installerer { $app_title }
splash-body = Konfigurerer { $app_title } { $app_version }, vent venligst...

# Dependency download (prerequisite.rs)
deps-download-header = Downloader påkrævet komponent
deps-download-body = Downloader { $dep_name }, vent venligst...

# Apply progress (apply_*_impl.rs)
apply-header = Installerer opdatering
apply-body = Opdaterer til version { $app_version }, vent venligst...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installationen er beskadiget
start-corrupt-body = Dette program kan ikke starte, fordi nogle af dets filer mangler eller er beskadiget. Geninstaller programmet for at løse dette.

# Generic error
error-header = Noget gik galt

# Setup error (wix msi)
setup-error-header = Installationen kunne ikke fortsætte

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Installation
msi-btn-back = &Tilbage
msi-btn-next = &Næste
msi-btn-cancel = Annuller
msi-btn-finish = &Udfør
msi-btn-ok = OK
msi-btn-yes = &Ja
msi-btn-no = &Nej
msi-btn-retry = &Prøv igen
msi-btn-ignore = &Ignorer

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Velkommen til guiden { $app_title } Installation
msi-welcome-description = Guiden Installation installerer { $app_title } på computeren. Klik på Næste for at fortsætte, eller klik på Annuller for at afslutte guiden Installation.
msi-welcome-update-description = Guiden Installation opdaterer { $app_title } på computeren. Klik på Næste for at fortsætte, eller klik på Annuller for at afslutte guiden Installation.

# MSI Installer UI - Exit Dialog
msi-exit-title = Guiden { $app_title } Installation er fuldført
msi-exit-description = Klik på knappen Udfør for at afslutte guiden Installation.
msi-exit-launch-checkbox = Start { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Velkommen til guiden { $app_title } Installation
msi-prepare-description = Vent, mens guiden Installation forbereder at vejlede dig gennem installationen.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Slutbrugerlicensaftale
msi-license-description = Læs følgende licensaftale grundigt.
msi-license-checkbox = Jeg &accepterer vilkårene i licensaftalen

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installationsområde
msi-scope-description = Vælg installationsområde.
msi-scope-per-user = Installer kun for &dig
msi-scope-per-machine = Installer for &alle brugere
msi-scope-per-user-description = Installerer kun for den aktuelle bruger
msi-scope-no-per-user-description = Kræver administratorrettigheder
msi-scope-per-machine-description = Kræver administratorrettigheder

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Klar til at installere { $app_title }
msi-ready-install-text = Klik på Installer for at starte installationen. Klik på Tilbage for at gennemgå eller ændre installationsindstillingerne.
msi-ready-change-title = Klar til at ændre { $app_title }
msi-ready-change-text = Klik på Skift for at begynde at ændre installationen. Klik på Tilbage for at gennemgå eller ændre installationsindstillingerne.
msi-ready-repair-title = Klar til at reparere { $app_title }
msi-ready-repair-text = Klik på Reparer for at begynde reparationen. Klik på Tilbage for at gennemgå eller ændre installationsindstillingerne.
msi-ready-remove-title = Klar til at fjerne { $app_title }
msi-ready-remove-text = Klik på Fjern for at fjerne { $app_title } fra computeren. Klik på Tilbage for at gennemgå eller ændre installationsindstillingerne.
msi-ready-update-title = Klar til at opdatere { $app_title }
msi-ready-update-text = Klik på Opdater for at starte opdateringen. Klik på Tilbage for at gennemgå eller ændre installationsindstillingerne.
msi-ready-btn-install = &Installer
msi-ready-btn-change = &Skift
msi-ready-btn-repair = Re&parer
msi-ready-btn-remove = &Fjern
msi-ready-btn-update = &Opdater

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Installerer { $app_title }
msi-progress-installing-text = Vent, mens guiden Installation installerer { $app_title }.
msi-progress-changing-title = Ændrer { $app_title }
msi-progress-changing-text = Vent, mens guiden Installation ændrer { $app_title }.
msi-progress-repairing-title = Reparerer { $app_title }
msi-progress-repairing-text = Vent, mens guiden Installation reparerer { $app_title }.
msi-progress-removing-title = Fjerner { $app_title }
msi-progress-removing-text = Vent, mens guiden Installation fjerner { $app_title }.
msi-progress-updating-title = Opdaterer { $app_title }
msi-progress-updating-text = Vent, mens guiden Installation opdaterer { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Velkommen til guiden { $app_title } Installation
msi-maint-welcome-description = Guiden Installation giver dig mulighed for at reparere eller fjerne { $app_title }. Klik på Næste for at fortsætte, eller klik på Annuller for at afslutte guiden Installation.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Skift, reparer eller fjern installation
msi-maint-type-description = Vælg den handling, som du vil udføre.
msi-maint-change-button = &Skift
msi-maint-change-tooltip = Skift installationen
msi-maint-change-text = Giver brugere mulighed for at ændre, hvilke programfunktioner der er installeret, og at ændre individuelle funktioner.
msi-maint-change-disabled = Skift er deaktiveret i øjeblikket.
msi-maint-repair-button = Re&parer
msi-maint-repair-tooltip = Reparer installationen
msi-maint-repair-text = Reparerer fejl i den seneste installation ved at rette manglende eller fejlbehæftede filer, genveje og poster i registreringsdatabasen.
msi-maint-repair-disabled = Reparer er deaktiveret i øjeblikket.
msi-maint-remove-button = &Fjern
msi-maint-remove-tooltip = Fjern installationen
msi-maint-remove-text = Fjerner { $app_title } fra computeren.
msi-maint-remove-disabled = Fjern er deaktiveret i øjeblikket.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Er du sikker på, at du vil annullere installationen af { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Skift den aktuelle destinationsmappe
msi-browse-description = Gå til destinationsmappen.
msi-browse-combo-label = &Søg i:
msi-browse-path-label = &Mappenavn:
msi-browse-up-tooltip = Ét niveau op
msi-browse-new-folder-tooltip = Opret en ny mappe

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Den angivne destinationsmappe er enten ugyldig eller på en drevtype, der ikke understøttes.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Krav til diskplads
msi-disk-cost-description = Den krævede diskplads til installation af de valgte funktioner.
msi-disk-cost-text = De markerede diskenheder har ikke nok ledig diskplads til de valgte funktioner. Du kan enten fjerne nogle filer fra de markerede diskenheder, vælge at installere færre funktioner på de lokale drev eller vælge andre destinationsdrev.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Installationsoplysninger

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Guiden { $app_title } Installation blev afsluttet før tid
msi-fatal-description1 = Installationen af { $app_title } blev afbrudt. Dit system er ikke blevet ændret. Hvis du vil installere programmet på et senere tidspunkt, skal du køre installationen igen.
msi-fatal-description2 = Klik på knappen Udfør for at afslutte guiden Installation.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Guiden { $app_title } Installation blev afbrudt
msi-user-exit-description1 = Installationen af { $app_title } blev afbrudt. Dit system er ikke blevet ændret. Hvis du vil installere programmet på et senere tidspunkt, skal du køre installationen igen.
msi-user-exit-description2 = Klik på knappen Udfør for at afslutte guiden Installation.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Filer, der er i brug
msi-files-in-use-description = Visse filer, som skal opdateres, er i brug.
msi-files-in-use-text = Følgende programmer bruger filer, der skal opdateres af denne installation. Luk disse programmer, og klik derefter på Prøv igen for at fortsætte installationen, eller klik på Annuller for at afslutte.
msi-files-in-use-exit = &Afslut

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Filer, der er i brug
msi-rm-files-in-use-description = Visse filer, som skal opdateres, er i brug.
msi-rm-files-in-use-text = Følgende programmer bruger filer, der skal opdateres af denne installation. Du kan lade guiden Installation automatisk lukke og forsøge at genstarte disse programmer, eller du kan lukke dem manuelt og klikke på OK for at fortsætte installationen.
msi-rm-files-in-use-use-rm = &Luk automatisk programmerne, og forsøg at genstarte dem, når installationen er fuldført.
msi-rm-files-in-use-dont-use-rm = &Luk ikke programmerne. (Det er nødvendigt at genstarte.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Fortsætter guiden { $app_title } Installation
msi-resume-description = Guiden Installation fuldfører installationen af { $app_title } på computeren. Klik på Installer for at fortsætte, eller klik på Annuller for at afslutte guiden Installation.
msi-resume-btn-install = &Installer

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Skrivebordsgenvej til { $app_title }
msi-start-menu-shortcut-description = Genvej til { $app_title } i menuen Start
# MSI Installer UI - Readme Dialog
msi-readme-title = Vigtig information
msi-readme-description = Læs venligst følgende oplysninger, før du fortsætter.
