# Shared titles
title-update = { $app_title } värskendamine
title-setup = { $app_title } installimine
title-uninstall = { $app_title } desinstallimine
error-title = { $program_name } tõrge

# Shared buttons
btn-cancel = Loobu
btn-install-update = Installi värskendus
btn-install = Installi
btn-update = Värskenda
btn-downgrade = Versiooni alandamine
btn-repair = Paranda
btn-open-log = Ava logi
btn-open-install-dir = Ava installikaust
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Administraatori õigused on vajalikud
elevate-body = { $app_title } vajab administraatori õigusi, et installida versioon { $app_version }. Kas lubada selle värskenduse jätkamine?

# Restart required (prerequisite.rs)
restart-header = Vajalik on taaskäivitamine
restart-body = Enne installi jätkamist tuleb arvuti taaskäivitada. Taaskäivitage arvuti ja käivitage install uuesti.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Vajalikud on täiendavad komponendid
missing-deps-body = { $app_title } vajab kõigepealt järgmiste installimist: { $deps }. Kas soovite need nüüd alla laadida ja installida?

# Uninstall with errors (uninstall)
uninstall-errors-header = Desinstallimine lõpetatud probleemidega
uninstall-errors-body = { $app_title } desinstalliti, kuid mõnda faili või kausta ei õnnestunud eemaldada. Saate need käsitsi kustutada või rakenduse uuesti installida ja desinstallimist uuesti proovida.
uninstall-errors-log = Üksikasjad salvestati asukohta: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } on juba installitud
overwrite-repair-body = See rakendus on juba teie arvutisse installitud. Kui see ei tööta õigesti, võite proovida seda uuesti installides parandada.
overwrite-older-installed = { $app_title } on juba installitud
overwrite-update-body = Praegu on installitud versioon { $old_version }. Kas soovite värskendada versioonile { $app_version }?
overwrite-newer-installed = Uuem versioon { $app_title } on juba installitud
overwrite-downgrade-body = Praegu on installitud versioon { $old_version }, mis on uuem kui see installer. Versiooni alandamine pole soovitatav ja võib põhjustada probleeme. Kas jätkata sellegipoolest?
overwrite-footer = Installitud asukohas: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Desinstallimine lõpetatud
uninstall-body = Rakendus on teie arvutist edukalt eemaldatud.

# Install hook failed (install.rs)
install-hook-header = Install õnnestus osaliselt
install-hook-body = Installimine on lõpetatud, kuid mõned sammud võisid ebaõnnestuda. Kui rakendus ei tööta õigesti, võite proovida seda uuesti installida või võtta ühendust rakenduse autoriga.

# Splash fallback (splash.rs)
splash-header = Installitakse { $app_title }
splash-body = Toote { $app_title } { $app_version } seadistamine, palun oodake...

# Dependency download (prerequisite.rs)
deps-download-header = Vajaliku komponendi allalaadimine
deps-download-body = Laadin alla { $dep_name }, palun oodake...

# Apply progress (apply_*_impl.rs)
apply-header = Värskenduse installimine
apply-body = Värskendamine versioonile { $app_version }, palun oodake...

# Start error (start_windows_impl.rs)
start-corrupt-header = Install on rikutud
start-corrupt-body = Seda rakendust ei saa käivitada, kuna mõned selle failid puuduvad või on rikutud. Probleemi lahendamiseks installige rakendus uuesti.

# Generic error
error-header = Midagi läks valesti

# Setup error (wix msi)
setup-error-header = Installi ei saanud jätkata

# MSI Installer UI - Common
msi-dlg-title = { $app_title } installimine
msi-btn-back = &Tagasi
msi-btn-next = &Edasi
msi-btn-cancel = Loobu
msi-btn-finish = &Lõpeta
msi-btn-ok = OK
msi-btn-yes = &Jah
msi-btn-no = &Ei
msi-btn-retry = &Proovi uuesti
msi-btn-ignore = &Ignoreeri

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Tere tulemast toote { $app_title } installiviisardisse!
msi-welcome-description = Installiviisard installib toote { $app_title } teie arvutisse. Jätkamiseks klõpsake käsul Edasi või installiviisardist väljumiseks käsul Loobu.
msi-welcome-update-description = Installiviisard värskendab toodet { $app_title } teie arvutis. Jätkamiseks klõpsake käsul Edasi või installiviisardist väljumiseks käsul Loobu.

# MSI Installer UI - Exit Dialog
msi-exit-title = Toote { $app_title } installiviisard on lõpetanud
msi-exit-description = Klõpsake installiviisardist väljumiseks nupul Lõpeta.
msi-exit-launch-checkbox = Käivita { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Tere tulemast toote { $app_title } installiviisardisse!
msi-prepare-description = Palun oodake, kuni installiviisard valmistub teid installimisel juhendama.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Lõppkasutaja litsentsileping
msi-license-description = Palun lugege alltoodud litsentsileping hoolikalt läbi.
msi-license-checkbox = &Nõustun litsentsilepingu tingimustega

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Installimise ulatus
msi-scope-description = Valige installimise ulatus.
msi-scope-per-user = Installi ainult &teie jaoks
msi-scope-per-machine = Installi kõigile &kasutajatele
msi-scope-per-user-description = Installib ainult praegusele kasutajale
msi-scope-no-per-user-description = Nõuab administraatori õigusi
msi-scope-per-machine-description = Nõuab administraatori õigusi

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Toote { $app_title } installimiseks valmis
msi-ready-install-text = Installimise alustamiseks klõpsake käsul Installi. Installisätete läbivaatamiseks või muutmiseks klõpsake käsul Tagasi.
msi-ready-change-title = Toote { $app_title } muutmiseks valmis
msi-ready-change-text = Installi muutmiseks klõpsake käsul Muuda. Installisätete läbivaatamiseks või muutmiseks klõpsake käsul Tagasi.
msi-ready-repair-title = Toote { $app_title } parandamiseks valmis
msi-ready-repair-text = Parandamise alustamiseks klõpsake käsul Paranda. Installisätete läbivaatamiseks või muutmiseks klõpsake käsul Tagasi.
msi-ready-remove-title = Toote { $app_title } eemaldamiseks valmis
msi-ready-remove-text = Toote { $app_title } arvutist eemaldamiseks klõpsake käsul Eemalda. Installisätete läbivaatamiseks või muutmiseks klõpsake käsul Tagasi.
msi-ready-update-title = Toote { $app_title } värskendamiseks valmis
msi-ready-update-text = Värskendamise alustamiseks klõpsake käsul Värskenda. Installisätete läbivaatamiseks või muutmiseks klõpsake käsul Tagasi.
msi-ready-btn-install = &Installi
msi-ready-btn-change = &Muuda
msi-ready-btn-repair = &Paranda
msi-ready-btn-remove = &Eemalda
msi-ready-btn-update = &Värskenda

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Toote { $app_title } installimine
msi-progress-installing-text = Palun oodake, kuni installiviisard installib toodet { $app_title }.
msi-progress-changing-title = Toote { $app_title } muutmine
msi-progress-changing-text = Palun oodake, kuni installiviisard muudab toodet { $app_title }.
msi-progress-repairing-title = Toote { $app_title } parandamine
msi-progress-repairing-text = Palun oodake, kuni installiviisard parandab toodet { $app_title }.
msi-progress-removing-title = Toote { $app_title } eemaldamine
msi-progress-removing-text = Palun oodake, kuni installiviisard eemaldab toodet { $app_title }.
msi-progress-updating-title = Toote { $app_title } värskendamine
msi-progress-updating-text = Palun oodake, kuni installiviisard värskendab toodet { $app_title }.
msi-progress-status = Olek:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Tere tulemast toote { $app_title } installiviisardisse!
msi-maint-welcome-description = Installiviisard võimaldab teil parandada või eemaldada toote { $app_title }. Jätkamiseks klõpsake käsul Edasi või installiviisardist väljumiseks käsul Loobu.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Installi muutmine, parandamine või eemaldamine
msi-maint-type-description = Valige toiming, mida soovite teha.
msi-maint-change-button = &Muuda
msi-maint-change-tooltip = Muuda installi
msi-maint-change-text = Võimaldab kasutajatel muuta, millised programmifunktsioonid on installitud, ja muuta üksikuid funktsioone.
msi-maint-change-disabled = Muutmine on hetkel keelatud.
msi-maint-repair-button = &Paranda
msi-maint-repair-tooltip = Paranda install
msi-maint-repair-text = Parandab viimase installimise tõrked, lahendades probleemid puuduvate või rikutud failide ja andmete, otseteede ja registrikirjete osas.
msi-maint-repair-disabled = Parandamine on hetkel keelatud.
msi-maint-remove-button = &Eemalda
msi-maint-remove-tooltip = Eemalda install
msi-maint-remove-text = Eemaldab toote { $app_title } teie arvutist.
msi-maint-remove-disabled = Eemaldamine on hetkel keelatud.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Kas soovite kindlasti rakenduse { $app_title } installist loobuda?

# MSI Installer UI - Browse Dialog
msi-browse-title = Praeguse sihtkausta muutmine
msi-browse-description = Liikuge sirvides soovitud sihtkausta juurde.
msi-browse-combo-label = &Vaata:
msi-browse-path-label = &Kausta nimi:
msi-browse-up-tooltip = Taseme võrra üles
msi-browse-new-folder-tooltip = Loo uus kaust

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Määratud sihtkataloog on kas vigane või asub draivitüübil, mida ei toetata.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Kettaruumi nõuded
msi-disk-cost-description = Valitud funktsioonide installimiseks vajalik kettaruum.
msi-disk-cost-text = Esiletõstetud draividel pole valitud funktsioonide jaoks piisavalt vaba kettaruumi. Võite mõne faili esiletõstetud draividelt eemaldada, installida vähem funktsioone kohalikule draivile või valida mõne muu sihtdraivi.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } installeri teave

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Toote { $app_title } installiviisard peatus enneaegselt
msi-fatal-description1 = Toote { $app_title } install katkestati. Teie süsteemi pole muudetud. Kui soovite selle programmi hiljem installida, käivitage install uuesti.
msi-fatal-description2 = Klõpsake installiviisardist väljumiseks nupul Lõpeta.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Toote { $app_title } installiviisardi töö on katkestatud
msi-user-exit-description1 = Toote { $app_title } installimine katkestati. Teie süsteemi pole muudetud. Kui soovite selle programmi hiljem installida, käivitage install uuesti.
msi-user-exit-description2 = Klõpsake installiviisardist väljumiseks nupul Lõpeta.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Kasutuses olevad failid
msi-files-in-use-description = Mõni värskendamist vajav fail on praegu kasutusel.
msi-files-in-use-text = Järgmised rakendused kasutavad faile, mida see install peab värskendama. Sulgege need rakendused ja klõpsake siis installimise jätkamiseks käsul Proovi uuesti või väljumiseks käsul Loobu.
msi-files-in-use-exit = V&älju

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Kasutuses olevad failid
msi-rm-files-in-use-description = Mõni värskendamist vajav fail on praegu kasutusel.
msi-rm-files-in-use-text = Järgmised rakendused kasutavad faile, mida see install peab värskendama. Võite lasta installiviisardil need rakendused automaatselt sulgeda ja proovida need uuesti käivitada või sulgeda need ise ja klõpsata installimise jätkamiseks nupul OK.
msi-rm-files-in-use-use-rm = &Sulgege rakendused automaatselt ja proovige need pärast installi lõpetamist uuesti käivitada.
msi-rm-files-in-use-dont-use-rm = &Ärge sulgege rakendusi. (Arvuti tuleb taaskäivitada.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Toote { $app_title } installiviisardi jätkamine
msi-resume-description = Installiviisard viib toote { $app_title } teie arvutisse installimise lõpule. Jätkamiseks klõpsake käsul Installi, installiviisardist väljumiseks käsul Loobu.
msi-resume-btn-install = &Installi

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Toote { $app_title } töölaua otsetee
msi-start-menu-shortcut-description = Toote { $app_title } menüü Start otsetee
# MSI Installer UI - Readme Dialog
msi-readme-title = Oluline teave
msi-readme-description = Palun lugege enne jätkamist järgmist teavet.
