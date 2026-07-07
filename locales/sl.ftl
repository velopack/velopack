# Shared titles
title-update = Posodobitev { $app_title }
title-setup = Namestitev { $app_title }
title-uninstall = Odstranitev { $app_title }
error-title = Napaka { $program_name }

# Shared buttons
btn-cancel = Prekliči
btn-install-update = Namesti posodobitev
btn-install = Namesti
btn-update = Posodobi
btn-downgrade = Znižaj različico
btn-repair = Popravi
btn-open-log = Odpri dnevnik
btn-open-install-dir = Odpri namestitveno mapo
btn-ok = V redu
# Elevation (dialogs_common.rs)
elevate-header = Zahtevana so skrbniška dovoljenja
elevate-body = { $app_title } potrebuje skrbniška dovoljenja za namestitev različice { $app_version }. Ali dovolite, da se ta posodobitev nadaljuje?

# Restart required (prerequisite.rs)
restart-header = Zahtevan je vnovični zagon
restart-body = Računalnik je treba znova zagnati, preden lahko namestitev nadaljuje. Znova zaženite računalnik in ponovno zaženite namestitev.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Zahtevane so dodatne komponente
missing-deps-body = { $app_title } zahteva, da najprej namestite: { $deps }. Jih želite zdaj prenesti in namestiti?

# Uninstall with errors (uninstall)
uninstall-errors-header = Odstranjevanje je končano s težavami
uninstall-errors-body = { $app_title } je bil odstranjen, vendar nekaterih datotek ali map ni bilo mogoče odstraniti. Lahko jih izbrišete ročno ali znova namestite aplikacijo in poskusite znova odstraniti.
uninstall-errors-log = Podrobnosti so bile shranjene v: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } je že nameščen
overwrite-repair-body = Ta aplikacija je že nameščena v računalniku. Če ne deluje pravilno, jo lahko poskusite popraviti tako, da jo znova namestite.
overwrite-older-installed = { $app_title } je že nameščen
overwrite-update-body = Trenutno je nameščena različica { $old_version }. Ali želite posodobiti na različico { $app_version }?
overwrite-newer-installed = Novejša različica programa { $app_title } je že nameščena
overwrite-downgrade-body = Trenutno je nameščena različica { $old_version }, ki je novejša od tega namestitvenega programa. Znižanje različice ni priporočljivo in lahko povzroči težave. Vseeno nadaljujem?
overwrite-footer = Nameščeno na: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Odstranjevanje je končano
uninstall-body = Aplikacija je bila uspešno odstranjena iz računalnika.

# Install hook failed (install.rs)
install-hook-header = Namestitev je delno uspela
install-hook-body = Namestitev je končana, vendar nekateri koraki morda niso uspeli. Če aplikacija ne deluje pravilno, jo lahko poskusite znova namestiti ali se obrnete na avtorja aplikacije.

# Splash fallback (splash.rs)
splash-header = Nameščanje { $app_title }
splash-body = Nastavitev { $app_title } { $app_version }, počakajte...

# Dependency download (prerequisite.rs)
deps-download-header = Prenašanje zahtevane komponente
deps-download-body = Prenašanje { $dep_name }, počakajte...

# Apply progress (apply_*_impl.rs)
apply-header = Nameščanje posodobitve
apply-body = Posodabljanje na različico { $app_version }, počakajte...

# Start error (start_windows_impl.rs)
start-corrupt-header = Namestitev je poškodovana
start-corrupt-body = Te aplikacije ni mogoče zagnati, ker nekatere njene datoteke manjkajo ali so poškodovane. Znova namestite aplikacijo, da odpravite to težavo.

# Generic error
error-header = Nekaj je šlo narobe

# Setup error (wix msi)
setup-error-header = Namestitve ni mogoče nadaljevati
setup-disk-space-insufficient = { $app_title } za namestitev potrebuje vsaj { $required_space } prostora na disku. Na voljo je samo { $available_space }.
setup-windows-version-unsupported = Ta namestitveni program zahteva Windows 7 SP1 ali novejši in se ne more zagnati.
setup-embedded-zip-missing = Vgrajene datoteke zip ni bilo mogoče najti. Obrnite se na avtorja aplikacije.
setup-os-version-required = Ta aplikacija zahteva Windows { $os_version } ali novejši.
setup-cpu-arch-unsupported = Ta aplikacija ({ $machine_arch }) ne podpira arhitekture vašega procesorja.
setup-stop-app-failed = Aplikacije ni bilo mogoče zaustaviti ({ $error }). Zaprite aplikacijo in poskusite znova zagnati namestitveni program.
setup-remove-dir-failed = Obstoječe mape aplikacije ni bilo mogoče odstraniti. Zaprite aplikacijo in poskusite znova zagnati namestitveni program. Če težava ne izgine, poskusite aplikacijo najprej odstraniti prek možnosti Programi in funkcije ali znova zaženite računalnik.
setup-update-exe-missing = Temu namestitvenemu programu manjka ključna binarna datoteka (Update.exe). Obrnite se na avtorja aplikacije.
setup-main-exe-missing = Glavne izvedljive datoteke v paketu ni bilo mogoče najti. Obrnite se na avtorja aplikacije.

# MSI Installer UI - Common
msi-dlg-title = Namestitev programa { $app_title }
msi-btn-back = &Nazaj
msi-btn-next = &Naprej
msi-btn-cancel = Prekliči
msi-btn-finish = &Dokončaj
msi-btn-ok = V redu
msi-btn-yes = &Da
msi-btn-no = &Ne
msi-btn-retry = &Poskusi znova
msi-btn-ignore = &Prezri

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Dobrodošli v čarovniku za namestitev programa { $app_title }
msi-welcome-description = Čarovnik za namestitev bo v računalnik namestil { $app_title }. Kliknite »Naprej« za nadaljevanje ali »Prekliči« za izhod iz čarovnika za namestitev.
msi-welcome-update-description = Čarovnik za namestitev bo posodobil { $app_title } v računalniku. Kliknite »Naprej«, če želite nadaljevati ali »Prekliči«, če želite zapreti čarovnika za namestitev.

# MSI Installer UI - Exit Dialog
msi-exit-title = Čarovnik za namestitev programa { $app_title } je dokončan
msi-exit-description = Če želite zapreti čarovnika za namestitev, kliknite gumb »Dokončaj«.
msi-exit-launch-checkbox = Zaženi { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Dobrodošli v čarovniku za namestitev programa { $app_title }
msi-prepare-description = Počakajte, da se čarovnik za namestitev pripravi za vodenje po namestitvi.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Licenčna pogodba za končnega uporabnika
msi-license-description = Pozorno preberite to licenčno pogodbo.
msi-license-checkbox = Sprejmem &pogoje licenčne pogodbe

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Obseg namestitve
msi-scope-description = Izberite obseg namestitve.
msi-scope-per-user = Namesti &le zame
msi-scope-per-machine = Namesti za &vse uporabnike
msi-scope-per-user-description = Namesti samo za trenutnega uporabnika
msi-scope-no-per-user-description = Zahteva skrbniške pravice
msi-scope-per-machine-description = Zahteva skrbniške pravice

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Pripravljen na namestitev programa { $app_title }
msi-ready-install-text = Kliknite »Namesti«, če želite začeti namestitev. Kliknite »Nazaj«, če želite pregledati ali spremeniti nastavitve namestitve.
msi-ready-change-title = Pripravljen na spreminjanje programa { $app_title }
msi-ready-change-text = Kliknite »Spremeni«, če želite začeti spreminjati namestitev. Kliknite »Nazaj«, če želite pregledati ali spremeniti nastavitve namestitve.
msi-ready-repair-title = Pripravljen na popravljanje programa { $app_title }
msi-ready-repair-text = Kliknite »Popravi«, če želite začeti popravilo. Kliknite »Nazaj«, če želite pregledati ali spremeniti nastavitve namestitve.
msi-ready-remove-title = Pripravljen na odstranjevanje programa { $app_title }
msi-ready-remove-text = Kliknite »Odstrani«, če želite odstraniti program { $app_title } iz računalnika. Kliknite »Nazaj«, če želite pregledati ali spremeniti nastavitve namestitve.
msi-ready-update-title = Pripravljen na posodobitev programa { $app_title }
msi-ready-update-text = Kliknite »Posodobi«, če želite začeti posodobitev. Kliknite »Nazaj«, če želite pregledati ali spremeniti nastavitve namestitve.
msi-ready-btn-install = &Namesti
msi-ready-btn-change = &Spremeni
msi-ready-btn-repair = Po&pravi
msi-ready-btn-remove = &Odstrani
msi-ready-btn-update = &Posodobi

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Namestitev programa { $app_title }
msi-progress-installing-text = Počakajte, da čarovnik za namestitev namesti program { $app_title }.
msi-progress-changing-title = Spreminjanje programa { $app_title }
msi-progress-changing-text = Počakajte, da čarovnik za namestitev spremeni program { $app_title }.
msi-progress-repairing-title = Popravljanje programa { $app_title }
msi-progress-repairing-text = Počakajte, da čarovnik za namestitev popravi program { $app_title }.
msi-progress-removing-title = Odstranjevanje programa { $app_title }
msi-progress-removing-text = Počakajte, da čarovnik za namestitev odstrani program { $app_title }.
msi-progress-updating-title = Posodabljanje programa { $app_title }
msi-progress-updating-text = Počakajte, da čarovnik za namestitev posodobi program { $app_title }.
msi-progress-status = Stanje:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Dobrodošli v čarovniku za namestitev programa { $app_title }
msi-maint-welcome-description = Čarovnik za namestitev omogoča popravljanje ali odstranjevanje programa { $app_title }. Če želite nadaljevati, kliknite »Naprej« ali »Prekliči«, če želite zapreti čarovnika za namestitev.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Spreminjanje, popravljanje ali odstranjevanje namestitve
msi-maint-type-description = Izberite postopek, ki ga želite izvesti.
msi-maint-change-button = &Spremeni...
msi-maint-change-tooltip = Spremeni...
msi-maint-change-text = Uporabnikom omogoča spreminjanje, katere programske funkcije so nameščene, in spreminjanje posameznih funkcij.
msi-maint-change-disabled = Spreminjanje je trenutno onemogočeno.
msi-maint-repair-button = Po&pravi
msi-maint-repair-tooltip = Popravi
msi-maint-repair-text = Odpravi napake pri nedavni namestitvi, tako da popravi manjkajoče in poškodovane datoteke, bližnjice in vnose v register.
msi-maint-repair-disabled = Popravilo je trenutno onemogočeno.
msi-maint-remove-button = &Odstrani
msi-maint-remove-tooltip = Odstrani
msi-maint-remove-text = Odstrani program { $app_title } iz računalnika.
msi-maint-remove-disabled = Odstranjevanje je trenutno onemogočeno.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Ali ste prepričani, da želite preklicati namestitev programa { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Spremenite trenutno ciljno mapo
msi-browse-description = Prebrskajte do ciljne mape.
msi-browse-combo-label = &Išči v:
msi-browse-path-label = &Ime mape:
msi-browse-up-tooltip = V nadrejeno mapo
msi-browse-new-folder-tooltip = Ustvari novo mapo

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Navedeni ciljni imenik je neveljaven ali pa je na nepodprti vrsti pogona.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Potreben prostor na disku
msi-disk-cost-description = Prostor na disku, ki ga potrebujete za namestitev izbranih funkcij.
msi-disk-cost-text = Na označenih nosilcih ni na voljo dovolj prostora za trenutno izbrane funkcije. Z označenih nosilcev lahko odstranite nekatere datoteke, namestite manj funkcij na lokalne pogone ali izberete drugi ciljni pogon.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informacije namestitvenega programa { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Čarovnik za namestitev programa { $app_title } se je predčasno končal
msi-fatal-description1 = Namestitev programa { $app_title } je bila prekinjena. Sistem ni bil spremenjen. Če želite ta program namestiti pozneje, znova zaženite namestitev.
msi-fatal-description2 = Če želite zapreti čarovnika za namestitev, kliknite gumb »Dokončaj«.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Čarovnik za namestitev programa { $app_title } je bil prekinjen
msi-user-exit-description1 = Namestitev programa { $app_title } je bila prekinjena. Sistem ni bil spremenjen. Če želite ta program namestiti pozneje, znova zaženite namestitev.
msi-user-exit-description2 = Če želite zapreti čarovnika za namestitev, kliknite gumb »Dokončaj«.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Datoteke v uporabi
msi-files-in-use-description = Nekatere datoteke, ki jih je treba posodobiti, so trenutno v uporabi.
msi-files-in-use-text = Ti programi uporabljajo datoteke, ki jih je treba posodobiti pri tej namestitvi. Zaprite te programe in nato kliknite »Poskusi znova«, da nadaljujete z namestitvijo, ali »Prekliči«, če jo želite zapreti.
msi-files-in-use-exit = I&zhod

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Datoteke v uporabi
msi-rm-files-in-use-description = Nekatere datoteke, ki jih je treba posodobiti, so trenutno v uporabi.
msi-rm-files-in-use-text = Navedeni programi uporabljajo datoteke, ki jih je med to namestitvijo treba posodobiti. Lahko dovolite, da jih čarovnik za namestitev samodejno zapre in jih po končani namestitvi poskusi znova zagnati, ali pa jih zaprete ročno in kliknete V redu za nadaljevanje namestitve.
msi-rm-files-in-use-use-rm = Samodejno &zapri programe in jih po končani namestitvi poskusi znova zagnati.
msi-rm-files-in-use-dont-use-rm = &Ne zapri programov. (Računalnik bo treba zagnati znova.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Nadaljevanje izvajanja čarovnika za namestitev programa { $app_title }
msi-resume-description = Čarovnik za namestitev bo dokončal namestitev programa { $app_title } v računalnik. Če želite nadaljevati, kliknite »Namesti« ali »Prekliči«, če želite zapreti čarovnika za namestitev.
msi-resume-btn-install = &Namesti

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Namizna bližnjica za { $app_title }
msi-start-menu-shortcut-description = Bližnjica v meniju Start za { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Pomembne informacije
msi-readme-description = Pred nadaljevanjem preberite naslednje informacije.
