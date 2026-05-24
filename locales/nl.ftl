# Shared titles
title-update = { $app_title } Update
title-setup = { $app_title } Installatie
title-uninstall = { $app_title } Verwijderen
error-title = { $program_name } Fout

# Shared buttons
btn-cancel = Annuleren
btn-install-update = Update installeren
btn-install = Installeren
btn-update = Bijwerken
btn-downgrade = Downgraden
btn-repair = Herstellen
btn-open-log = Logboek openen
btn-open-install-dir = Installatiemap openen

# Elevation (dialogs_common.rs)
elevate-header = Beheerdersrechten vereist
elevate-body = { $app_title } heeft beheerdersrechten nodig om versie { $app_version } te installeren. Toestaan dat deze update wordt voortgezet?

# Restart required (prerequisite.rs)
restart-header = Opnieuw opstarten vereist
restart-body = Uw computer moet opnieuw worden opgestart voordat de installatie kan worden voortgezet. Start uw computer opnieuw op en voer de installatie opnieuw uit.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Aanvullende onderdelen vereist
missing-deps-body = { $app_title } vereist dat de volgende onderdelen eerst worden geïnstalleerd: { $deps }. Wilt u deze nu downloaden en installeren?

# Uninstall with errors (uninstall)
uninstall-errors-header = Verwijderen voltooid met problemen
uninstall-errors-body = { $app_title } is verwijderd, maar sommige bestanden of mappen konden niet worden verwijderd. U kunt ze handmatig verwijderen, of de toepassing opnieuw installeren en opnieuw proberen te verwijderen.
uninstall-errors-log = Details zijn opgeslagen in: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } is al geïnstalleerd
overwrite-repair-body = Deze toepassing is al op uw computer geïnstalleerd. Als deze niet correct werkt, kunt u proberen deze te herstellen door opnieuw te installeren.
overwrite-older-installed = { $app_title } is al geïnstalleerd
overwrite-update-body = Versie { $old_version } is momenteel geïnstalleerd. Wilt u bijwerken naar versie { $app_version }?
overwrite-newer-installed = Er is al een nieuwere versie van { $app_title } geïnstalleerd
overwrite-downgrade-body = Versie { $old_version } is momenteel geïnstalleerd en is nieuwer dan dit installatieprogramma. Downgraden wordt niet aanbevolen en kan problemen veroorzaken. Toch doorgaan?
overwrite-footer = Geïnstalleerd in: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Verwijderen voltooid
uninstall-body = De toepassing is met succes van uw computer verwijderd.

# Install hook failed (install.rs)
install-hook-header = Installatie gedeeltelijk gelukt
install-hook-body = De installatie is voltooid, maar sommige stappen kunnen zijn mislukt. Als de toepassing niet correct werkt, kunt u proberen deze opnieuw te installeren of contact opnemen met de auteur van de toepassing.

# Splash fallback (splash.rs)
splash-header = { $app_title } wordt geïnstalleerd
splash-body = { $app_title } { $app_version } wordt ingesteld, een ogenblik geduld...

# Dependency download (prerequisite.rs)
deps-download-header = Vereist onderdeel wordt gedownload
deps-download-body = { $dep_name } wordt gedownload, een ogenblik geduld...

# Apply progress (apply_*_impl.rs)
apply-header = Update wordt geïnstalleerd
apply-body = Bijwerken naar versie { $app_version }, een ogenblik geduld...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installatie beschadigd
start-corrupt-body = Deze toepassing kan niet worden gestart omdat sommige bestanden ontbreken of beschadigd zijn. Installeer de toepassing opnieuw om dit op te lossen.

# Generic error
error-header = Er is iets misgegaan

# Setup error (wix msi)
setup-error-header = Installatie kan niet worden voortgezet

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Installatie
msi-btn-back = V&orige
msi-btn-next = V&olgende
msi-btn-cancel = Annuleren
msi-btn-finish = &Voltooien
msi-btn-ok = OK
msi-btn-yes = &Ja
msi-btn-no = &Nee
msi-btn-retry = &Opnieuw
msi-btn-ignore = &Negeren

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Welkom bij de wizard Setup van { $app_title }
msi-welcome-description = Met de wizard Setup wordt { $app_title } op uw computer geïnstalleerd. Klik op Volgende om door te gaan of op Annuleren als u de wizard Setup wilt afsluiten.
msi-welcome-update-description = Met de wizard Setup wordt { $app_title } op uw computer bijgewerkt. Klik op Volgende om door te gaan of op Annuleren als u de wizard Setup wilt afsluiten.

# MSI Installer UI - Exit Dialog
msi-exit-title = De wizard Setup van { $app_title } is voltooid
msi-exit-description = Klik op de knop Voltooien om de wizard Setup af te sluiten.
msi-exit-launch-checkbox = { $app_title } starten

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Welkom bij de wizard Setup van { $app_title }
msi-prepare-description = De wizard Setup wordt voorbereid. Een ogenblik geduld.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Gebruiksrechtovereenkomst
msi-license-description = Lees de volgende gebruiksrechtovereenkomst aandachtig door.
msi-license-checkbox = Ik ga &akkoord met de voorwaarden in de overeenkomst

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installatiebereik
msi-scope-description = Selecteer het installatiebereik.
msi-scope-per-user = Alleen voor &uzelf installeren
msi-scope-per-machine = Installeren voor &alle gebruikers
msi-scope-per-user-description = Installeert alleen voor de huidige gebruiker
msi-scope-no-per-user-description = Vereist beheerdersrechten
msi-scope-per-machine-description = Vereist beheerdersrechten

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Gereed om { $app_title } te installeren
msi-ready-install-text = Klik op Installeren om de installatie te starten. Klik op Vorige om uw installatie-instellingen te bekijken of te wijzigen.
msi-ready-change-title = Gereed om { $app_title } te wijzigen
msi-ready-change-text = Klik op Wijzigen om het wijzigen van de installatie te starten. Klik op Vorige om uw installatie-instellingen te bekijken of te wijzigen.
msi-ready-repair-title = Gereed om { $app_title } te herstellen
msi-ready-repair-text = Klik op Herstellen om het herstel te starten. Klik op Vorige om uw installatie-instellingen te bekijken of te wijzigen.
msi-ready-remove-title = Gereed om { $app_title } te verwijderen
msi-ready-remove-text = Klik op Verwijderen om { $app_title } van uw computer te verwijderen. Klik op Vorige om uw installatie-instellingen te bekijken of te wijzigen.
msi-ready-update-title = Gereed om { $app_title } bij te werken
msi-ready-update-text = Klik op Bijwerken om de update te starten. Klik op Vorige om uw installatie-instellingen te bekijken of te wijzigen.
msi-ready-btn-install = &Installeren
msi-ready-btn-change = &Wijzigen
msi-ready-btn-repair = &Herstellen
msi-ready-btn-remove = &Verwijderen
msi-ready-btn-update = &Bijwerken

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } installeren
msi-progress-installing-text = { $app_title } wordt geïnstalleerd door de wizard Setup. Een ogenblik geduld.
msi-progress-changing-title = { $app_title } wijzigen
msi-progress-changing-text = { $app_title } wordt gewijzigd door de wizard Setup. Een ogenblik geduld.
msi-progress-repairing-title = { $app_title } herstellen
msi-progress-repairing-text = { $app_title } wordt hersteld door de wizard Setup. Een ogenblik geduld.
msi-progress-removing-title = { $app_title } verwijderen
msi-progress-removing-text = { $app_title } wordt verwijderd door de wizard Setup. Een ogenblik geduld.
msi-progress-updating-title = { $app_title } bijwerken
msi-progress-updating-text = { $app_title } wordt bijgewerkt door de wizard Setup. Een ogenblik geduld.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Welkom bij de wizard Setup van { $app_title }
msi-maint-welcome-description = Met de wizard Setup kunt u { $app_title } herstellen of verwijderen. Klik op Volgende om door te gaan of op Annuleren als u de wizard Setup wilt afsluiten.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Installatie wijzigen, herstellen of verwijderen
msi-maint-type-description = Selecteer de bewerking die u wilt uitvoeren.
msi-maint-change-button = &Wijzigen...
msi-maint-change-tooltip = Wijzigen...
msi-maint-change-text = Hiermee kunnen gebruikers wijzigen welke programmaonderdelen worden geïnstalleerd en afzonderlijke onderdelen wijzigen.
msi-maint-change-disabled = Wijzigen is momenteel uitgeschakeld.
msi-maint-repair-button = &Herstellen
msi-maint-repair-tooltip = Herstellen
msi-maint-repair-text = Hiermee worden fouten in de meest recente installatie hersteld - ontbrekende of beschadigde bestanden, snelkoppelingen en registervermeldingen worden gerepareerd.
msi-maint-repair-disabled = Herstellen is momenteel uitgeschakeld.
msi-maint-remove-button = &Verwijderen
msi-maint-remove-tooltip = Verwijderen
msi-maint-remove-text = Hiermee wordt { $app_title } van uw computer verwijderd.
msi-maint-remove-disabled = Verwijderen is momenteel uitgeschakeld.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Weet u zeker dat u de installatie van { $app_title } wilt annuleren?

# MSI Installer UI - Browse Dialog
msi-browse-title = Huidige doelmap wijzigen
msi-browse-description = Naar de doelmap bladeren.
msi-browse-combo-label = &Zoeken in:
msi-browse-path-label = &Mapnaam:
msi-browse-up-tooltip = Eén niveau naar boven
msi-browse-new-folder-tooltip = Een nieuwe map maken

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = De opgegeven doelmap is ongeldig of bevindt zich op een niet-ondersteund type station.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Vereiste schijfruimte
msi-disk-cost-description = De benodigde schijfruimte voor de installatie van de geselecteerde onderdelen.
msi-disk-cost-text = De gemarkeerde volumes beschikken niet over voldoende schijfruimte voor de momenteel geselecteerde onderdelen. U kunt bestanden van de gemarkeerde volumes verwijderen, ervoor kiezen minder onderdelen op het/de lokale station(s) te installeren, of andere doelstations selecteren.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Installatie-informatie

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = De wizard Setup van { $app_title } is voortijdig beëindigd
msi-fatal-description1 = De installatie van { $app_title } is onderbroken. Het systeem is niet gewijzigd. Als u dit programma op een later tijdstip wilt installeren, voert u de installatie opnieuw uit.
msi-fatal-description2 = Klik op de knop Voltooien om de wizard Setup af te sluiten.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = De wizard Setup van { $app_title } is onderbroken
msi-user-exit-description1 = De installatie van { $app_title } is onderbroken. Het systeem is niet gewijzigd. Als u dit programma op een later tijdstip wilt installeren, voert u de installatie opnieuw uit.
msi-user-exit-description2 = Klik op de knop Voltooien om de wizard Setup af te sluiten.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Bestanden in gebruik
msi-files-in-use-description = Bepaalde bestanden die moeten worden bijgewerkt, zijn momenteel in gebruik.
msi-files-in-use-text = De volgende toepassingen gebruiken bestanden die moeten worden bijgewerkt door deze installatie. Sluit deze toepassingen en klik op Opnieuw om de installatie voort te zetten of op Annuleren om de installatie af te sluiten.
msi-files-in-use-exit = &Afsluiten

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Bestanden in gebruik
msi-rm-files-in-use-description = Bepaalde bestanden die moeten worden bijgewerkt, zijn momenteel in gebruik.
msi-rm-files-in-use-text = De volgende toepassingen gebruiken bestanden die door deze installatie moeten worden bijgewerkt. U kunt de wizard Setup deze toepassingen automatisch laten sluiten en proberen ze opnieuw te starten, of u kunt ze handmatig sluiten en op OK klikken om de installatie voort te zetten.
msi-rm-files-in-use-use-rm = De toepassingen automatisch &sluiten en proberen ze opnieuw te starten nadat de installatie is voltooid.
msi-rm-files-in-use-dont-use-rm = &Toepassingen niet sluiten. (Opnieuw opstarten is vereist.)

# MSI Installer UI - Resume Dialog
msi-resume-title = De wizard Setup van { $app_title } hervatten
msi-resume-description = Met de wizard Setup wordt de installatie van { $app_title } op uw computer voltooid. Klik op Installeren om door te gaan of op Annuleren als u de wizard Setup wilt afsluiten.
msi-resume-btn-install = &Installeren

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Bureaubladsnelkoppeling voor { $app_title }
msi-start-menu-shortcut-description = Startmenu-snelkoppeling voor { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Belangrijke informatie
msi-readme-description = Lees de volgende informatie voordat u verdergaat.
