# Shared titles
title-update = Ažuriranje { $app_title }
title-setup = Instalacija { $app_title }
title-uninstall = Deinstalacija { $app_title }
error-title = Pogreška { $program_name }

# Shared buttons
btn-cancel = Odustani
btn-install-update = Instaliraj ažuriranje
btn-install = Instaliraj
btn-update = Ažuriraj
btn-downgrade = Vrati na stariju verziju
btn-repair = Popravi
btn-open-log = Otvori zapisnik
btn-open-install-dir = Otvori instalacijski direktorij
btn-ok = U redu
# Elevation (dialogs_common.rs)
elevate-header = Potrebne su administratorske ovlasti
elevate-body = { $app_title } zahtijeva administratorske ovlasti za instalaciju verzije { $app_version }. Dopustite li nastavak ovog ažuriranja?

# Restart required (prerequisite.rs)
restart-header = Potrebno je ponovno pokretanje
restart-body = Računalo se mora ponovno pokrenuti prije nego što se instalacija može nastaviti. Ponovno pokrenite računalo i ponovno pokrenite instalaciju.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Potrebne su dodatne komponente
missing-deps-body = Za { $app_title } najprije se moraju instalirati sljedeće komponente: { $deps }. Želite li ih sada preuzeti i instalirati?

# Uninstall with errors (uninstall)
uninstall-errors-header = Deinstalacija je dovršena s problemima
uninstall-errors-body = Aplikacija { $app_title } je deinstalirana, ali neke datoteke ili mape nije bilo moguće ukloniti. Možete ih izbrisati ručno ili ponovno instalirati aplikaciju i pokušati je deinstalirati ponovno.
uninstall-errors-log = Pojedinosti su spremljene u: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } je već instaliran
overwrite-repair-body = Ova aplikacija već je instalirana na vašem računalu. Ako ne radi ispravno, možete je pokušati popraviti ponovnom instalacijom.
overwrite-older-installed = { $app_title } je već instaliran
overwrite-update-body = Trenutno je instalirana verzija { $old_version }. Želite li ažurirati na verziju { $app_version }?
overwrite-newer-installed = Novija verzija aplikacije { $app_title } već je instalirana
overwrite-downgrade-body = Trenutno je instalirana verzija { $old_version }, koja je novija od ove instalacije. Vraćanje na stariju verziju se ne preporučuje i može uzrokovati probleme. Želite li ipak nastaviti?
overwrite-footer = Instalirano na: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Deinstalacija je dovršena
uninstall-body = Aplikacija je uspješno uklonjena s vašeg računala.

# Install hook failed (install.rs)
install-hook-header = Instalacija je djelomično uspjela
install-hook-body = Instalacija je dovršena, ali neki koraci možda nisu uspjeli. Ako aplikacija ne radi ispravno, možete je pokušati ponovno instalirati ili se obratiti autoru aplikacije.

# Splash fallback (splash.rs)
splash-header = Instaliranje { $app_title }
splash-body = Postavljanje { $app_title } { $app_version }, pričekajte...

# Dependency download (prerequisite.rs)
deps-download-header = Preuzimanje potrebne komponente
deps-download-body = Preuzimanje { $dep_name }, pričekajte...

# Apply progress (apply_*_impl.rs)
apply-header = Instaliranje ažuriranja
apply-body = Ažuriranje na verziju { $app_version }, pričekajte...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalacija je oštećena
start-corrupt-body = Ovu aplikaciju nije moguće pokrenuti jer neke njezine datoteke nedostaju ili su oštećene. Ponovno instalirajte aplikaciju da biste otklonili ovaj problem.

# Generic error
error-header = Nešto je pošlo po krivu

# Setup error (wix msi)
setup-error-header = Instalaciju nije moguće nastaviti
setup-disk-space-insufficient = { $app_title } zahtijeva najmanje { $required_space } prostora na disku za instalaciju. Dostupno je samo { $available_space }.
setup-windows-version-unsupported = Ova instalacija zahtijeva Windows 7 SP1 ili noviji i ne može se pokrenuti.
setup-embedded-zip-missing = Ugrađena zip datoteka nije pronađena. Obratite se autoru aplikacije.
setup-os-version-required = Ova aplikacija zahtijeva Windows { $os_version } ili noviji.
setup-cpu-arch-unsupported = Ova aplikacija ({ $machine_arch }) ne podržava arhitekturu vašeg procesora.
setup-stop-app-failed = Zaustavljanje aplikacije nije uspjelo ({ $error }), zatvorite aplikaciju i pokušajte ponovno pokrenuti instalaciju.
setup-remove-dir-failed = Uklanjanje postojeće mape aplikacije nije uspjelo, zatvorite aplikaciju i pokušajte ponovno pokrenuti instalaciju. Ako se problem nastavi, pokušajte prvo deinstalirati putem stavke Programi i značajke ili ponovno pokrenite računalo.
setup-update-exe-missing = Ovoj instalaciji nedostaje ključna binarna datoteka (Update.exe). Obratite se autoru aplikacije.
setup-main-exe-missing = Glavna izvršna datoteka nije pronađena u paketu. Obratite se autoru aplikacije.

# MSI Installer UI - Common
msi-dlg-title = Instalacija programa { $app_title }
msi-btn-back = &Natrag
msi-btn-next = &Dalje
msi-btn-cancel = Odustani
msi-btn-finish = &Dovrši
msi-btn-ok = U redu
msi-btn-yes = &Da
msi-btn-no = &Ne
msi-btn-retry = &Pokušaj ponovo
msi-btn-ignore = &Zanemari

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Dobro došli u čarobnjak za instalaciju programa { $app_title }
msi-welcome-description = Čarobnjak za instalaciju instalirat će { $app_title } na računalo. Kliknite "Dalje" da biste nastavili ili "Odustani" da biste izašli iz čarobnjaka za instalaciju.
msi-welcome-update-description = Čarobnjak za instalaciju ažurirat će { $app_title } na računalu. Kliknite "Dalje" da biste nastavili ili "Odustani" da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - Exit Dialog
msi-exit-title = Dovršen je čarobnjak za instalaciju programa { $app_title }
msi-exit-description = Kliknite gumb "Dovrši" da biste izašli iz čarobnjaka za instalaciju.
msi-exit-launch-checkbox = Pokreni { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Dobro došli u čarobnjak za instalaciju programa { $app_title }
msi-prepare-description = Pričekajte da se čarobnjak za instalaciju pripremi da bi vas vodio kroz instalaciju.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Licencni ugovor za krajnjeg korisnika
msi-license-description = Pozorno pročitajte sljedeći licencni ugovor.
msi-license-checkbox = &Prihvaćam uvjete licencnog ugovora

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Opseg instalacije
msi-scope-description = Odaberite opseg instalacije.
msi-scope-per-user = Instaliraj &samo za vas
msi-scope-per-machine = Instaliraj za &sve korisnike
msi-scope-per-user-description = Instalira samo za trenutnog korisnika
msi-scope-no-per-user-description = Zahtijeva administratorske ovlasti
msi-scope-per-machine-description = Zahtijeva administratorske ovlasti

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Spreman instalirati { $app_title }
msi-ready-install-text = Kliknite "Instaliraj" da biste započeli instalaciju. Kliknite "Natrag" da biste pregledali postavke instalacije ili ih promijenili.
msi-ready-change-title = Spreman promijeniti { $app_title }
msi-ready-change-text = Kliknite "Promijeni" da biste započeli promjenu instalacije. Kliknite "Natrag" da biste pregledali postavke instalacije ili ih promijenili.
msi-ready-repair-title = Spreman popraviti { $app_title }
msi-ready-repair-text = Kliknite "Popravi" da biste započeli popravak. Kliknite "Natrag" da biste pregledali postavke instalacije ili ih promijenili.
msi-ready-remove-title = Spreman ukloniti { $app_title }
msi-ready-remove-text = Kliknite "Ukloni" da biste uklonili { $app_title } s računala. Kliknite "Natrag" da biste pregledali postavke instalacije ili ih promijenili.
msi-ready-update-title = Spreman ažurirati { $app_title }
msi-ready-update-text = Kliknite "Ažuriraj" da biste započeli ažuriranje. Kliknite "Natrag" da biste pregledali postavke instalacije ili ih promijenili.
msi-ready-btn-install = &Instaliraj
msi-ready-btn-change = &Promijeni
msi-ready-btn-repair = Po&pravi
msi-ready-btn-remove = &Ukloni
msi-ready-btn-update = &Ažuriraj

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Instalacija programa { $app_title }
msi-progress-installing-text = Pričekajte da čarobnjak za instalaciju instalira { $app_title }.
msi-progress-changing-title = Promjena programa { $app_title }
msi-progress-changing-text = Pričekajte da čarobnjak za instalaciju promijeni { $app_title }.
msi-progress-repairing-title = Popravak programa { $app_title }
msi-progress-repairing-text = Pričekajte da čarobnjak za instalaciju popravi { $app_title }.
msi-progress-removing-title = Uklanjanje programa { $app_title }
msi-progress-removing-text = Pričekajte da čarobnjak za instalaciju ukloni { $app_title }.
msi-progress-updating-title = Ažuriranje programa { $app_title }
msi-progress-updating-text = Pričekajte da čarobnjak za instalaciju ažurira { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Dobro došli u čarobnjak za instalaciju programa { $app_title }
msi-maint-welcome-description = Čarobnjak za instalaciju omogućuje popravak ili uklanjanje programa { $app_title }. Kliknite "Dalje" da biste nastavili ili "Odustani" da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Promjena, popravak i uklanjanje instalacije
msi-maint-type-description = Odaberite operaciju koju želite izvesti.
msi-maint-change-button = &Promijeni...
msi-maint-change-tooltip = Promijeni...
msi-maint-change-text = Omogućuje korisnicima promjenu instaliranih značajki programa i promjenu pojedinačnih značajki.
msi-maint-change-disabled = Promjena je trenutno onemogućena.
msi-maint-repair-button = Po&pravi
msi-maint-repair-tooltip = Popravi
msi-maint-repair-text = Otklanja pogreške najnovije instalacije popravljanjem oštećenih datoteka, prečaca i unosa u registar te dodavanjem onih koji nedostaju.
msi-maint-repair-disabled = Popravak je trenutno onemogućen.
msi-maint-remove-button = &Ukloni
msi-maint-remove-tooltip = Ukloni
msi-maint-remove-text = Uklanja { $app_title } s računala.
msi-maint-remove-disabled = Uklanjanje je trenutno onemogućeno.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Jeste li sigurni da želite otkazati instalaciju programa { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Promjena trenutne odredišne mape
msi-browse-description = Pregledavanje odredišne mape.
msi-browse-combo-label = &Traži u:
msi-browse-path-label = &Naziv mape:
msi-browse-up-tooltip = Jedna razina gore
msi-browse-new-folder-tooltip = Stvaranje nove mape

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Navedeni odredišni direktorij nije važeći ili se nalazi na nepodržanoj vrsti pogona.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Potreban prostor na disku
msi-disk-cost-description = Potreban slobodan prostor na disku za instalaciju odabranih značajki.
msi-disk-cost-text = Na označenim jedinicama nema dovoljno slobodnog prostora na disku za trenutno odabrane značajke. Možete ukloniti neke datoteke s označenih jedinica, instalirati manje značajki na lokalne pogone ili odabrati neki drugi odredišni disk.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } – informacije programa za instalaciju

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Čarobnjak za instalaciju programa { $app_title } završio je prerano
msi-fatal-description1 = Instalacija programa { $app_title } prekinuta je. Sustav nije izmijenjen. Da biste naknadno instalirali program, ponovno pokrenite instalaciju.
msi-fatal-description2 = Kliknite gumb "Dovrši" da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Instalacija programa { $app_title } je prekinuta
msi-user-exit-description1 = Instalacija programa { $app_title } prekinuta je. Sustav nije izmijenjen. Da biste naknadno instalirali program, ponovno pokrenite instalaciju.
msi-user-exit-description2 = Kliknite gumb "Dovrši" da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Datoteke se koriste
msi-files-in-use-description = Neke datoteke koje je potrebno ažurirati trenutno se koriste.
msi-files-in-use-text = Sljedeće aplikacije koriste datoteke koje instalacija mora ažurirati. Zatvorite te aplikacije, a zatim kliknite "Pokušaj ponovno" da biste nastavili instalaciju ili "Odustani" da biste izašli.
msi-files-in-use-exit = I&zlaz

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Datoteke se koriste
msi-rm-files-in-use-description = Neke datoteke koje je potrebno ažurirati trenutno se koriste.
msi-rm-files-in-use-text = Sljedeće aplikacije koriste datoteke koje instalacija mora ažurirati. Možete dopustiti čarobnjaku za instalaciju da ih automatski zatvori i pokuša ih ponovno pokrenuti nakon dovršetka instalacije ili ih možete zatvoriti ručno i kliknuti U redu da biste nastavili instalaciju.
msi-rm-files-in-use-use-rm = Automatski &zatvori aplikacije i pokušaj ih ponovno pokrenuti nakon dovršetka instalacije.
msi-rm-files-in-use-dont-use-rm = &Ne zatvaraj aplikacije. (Potrebno će biti ponovno pokrenuti računalo.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Nastavljanje čarobnjaka za instalaciju programa { $app_title }
msi-resume-description = Čarobnjak za instalaciju dovršit će instalaciju programa { $app_title } na računalo. Kliknite "Instaliraj" da biste nastavili ili "Odustani" da biste izašli iz čarobnjaka za instalaciju.
msi-resume-btn-install = &Instaliraj

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Prečac na radnoj površini za { $app_title }
msi-start-menu-shortcut-description = Prečac u izborniku Start za { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Važne informacije
msi-readme-description = Molimo pročitajte sljedeće informacije prije nastavka.
