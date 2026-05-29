# Shared titles
title-update = Ažuriranje { $app_title }
title-setup = Instalacija { $app_title }
title-uninstall = Deinstalacija { $app_title }
error-title = Greška { $program_name }

# Shared buttons
btn-cancel = Otkaži
btn-install-update = Instaliraj ažuriranje
btn-install = Instaliraj
btn-update = Ažuriraj
btn-downgrade = Vrati na stariju verziju
btn-repair = Popravi
btn-open-log = Otvori dnevnik
btn-open-install-dir = Otvori instalacioni direktorijum
btn-ok = U redu
btn-hide = Sakrij
# Elevation (dialogs_common.rs)
elevate-header = Potrebne su administratorske dozvole
elevate-body = { $app_title } zahteva administratorske dozvole za instalaciju verzije { $app_version }. Dozvoliti nastavak ažuriranja?

# Restart required (prerequisite.rs)
restart-header = Potrebno je ponovno pokretanje
restart-body = Vaš računar mora biti ponovo pokrenut pre nego što se instalacija može nastaviti. Ponovo pokrenite računar i pokrenite instalaciju ponovo.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Potrebne su dodatne komponente
missing-deps-body = { $app_title } zahteva da prvo budu instalirane sledeće stavke: { $deps }. Želite li da ih preuzmete i instalirate sada?

# Uninstall with errors (uninstall)
uninstall-errors-header = Deinstalacija je završena sa problemima
uninstall-errors-body = { $app_title } je deinstaliran, ali neke datoteke ili fascikle nisu mogle biti uklonjene. Možete ih ručno izbrisati ili ponovo instalirati aplikaciju i pokušati deinstalaciju ponovo.
uninstall-errors-log = Detalji su sačuvani u: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } je već instaliran
overwrite-repair-body = Ova aplikacija je već instalirana na vašem računaru. Ako ne radi ispravno, možete je pokušati popraviti tako što ćete je ponovo instalirati.
overwrite-older-installed = { $app_title } je već instaliran
overwrite-update-body = Trenutno je instalirana verzija { $old_version }. Želite li da ažurirate na verziju { $app_version }?
overwrite-newer-installed = Novija verzija { $app_title } je već instalirana
overwrite-downgrade-body = Trenutno je instalirana verzija { $old_version }, koja je novija od ovog instalatera. Vraćanje na stariju verziju se ne preporučuje i može izazvati probleme. Nastaviti ipak?
overwrite-footer = Instalirano u: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Deinstalacija je dovršena
uninstall-body = Aplikacija je uspešno uklonjena sa vašeg računara.

# Install hook failed (install.rs)
install-hook-header = Instalacija je delimično uspela
install-hook-body = Instalacija je dovršena, ali su neki koraci možda neuspeli. Ako aplikacija ne radi ispravno, možete pokušati ponovnu instalaciju ili se obratiti autoru aplikacije.

# Splash fallback (splash.rs)
splash-header = Instaliranje { $app_title }
splash-body = Podešavanje { $app_title } { $app_version }, sačekajte...

# Dependency download (prerequisite.rs)
deps-download-header = Preuzimanje potrebne komponente
deps-download-body = Preuzimanje { $dep_name }, sačekajte...

# Apply progress (apply_*_impl.rs)
apply-header = Instaliranje ažuriranja
apply-body = Ažuriranje na verziju { $app_version }, sačekajte...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalacija je oštećena
start-corrupt-body = Ova aplikacija ne može da se pokrene jer nedostaju neke od njenih datoteka ili su oštećene. Ponovo instalirajte aplikaciju da biste rešili ovaj problem.

# Generic error
error-header = Nešto je pošlo po zlu

# Setup error (wix msi)
setup-error-header = Instalacija ne može da se nastavi

# MSI Installer UI - Common
msi-dlg-title = Instalacija programa { $app_title }
msi-btn-back = &Nazad
msi-btn-next = &Dalje
msi-btn-cancel = Otkaži
msi-btn-finish = &Završi
msi-btn-ok = U redu
msi-btn-yes = &Da
msi-btn-no = &Ne
msi-btn-retry = &Pokušaj opet
msi-btn-ignore = Zanemar&i

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Dobro došli u čarobnjak za instalaciju proizvoda { $app_title }
msi-welcome-description = Čarobnjak za instalaciju će instalirati { $app_title } na računar. Kliknite na dugme „Dalje“ da biste nastavili ili na dugme „Otkaži“ da biste izašli iz čarobnjaka za instalaciju.
msi-welcome-update-description = Čarobnjak za instalaciju će ažurirati { $app_title } na računaru. Kliknite na dugme „Dalje“ da biste nastavili ili na dugme „Otkaži“ da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - Exit Dialog
msi-exit-title = Dovršen je čarobnjak za instalaciju proizvoda { $app_title }
msi-exit-description = Kliknite na dugme „Završi“ da biste izašli iz čarobnjaka za instalaciju.
msi-exit-launch-checkbox = Pokreni { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Dobro došli u čarobnjak za instalaciju proizvoda { $app_title }
msi-prepare-description = Sačekajte dok se čarobnjak za instalaciju pripremi da vas vodi kroz instalaciju.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Ugovor o licenciranju sa krajnjim korisnikom
msi-license-description = Pažljivo pročitajte sledeći ugovor o licenciranju.
msi-license-checkbox = Prihv&atam uslove navedene u ugovoru o licenciranju

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Opseg instalacije
msi-scope-description = Izaberite opseg instalacije.
msi-scope-per-user = Instaliraj &samo za vas
msi-scope-per-machine = Instaliraj za &sve korisnike
msi-scope-per-user-description = Instalira se samo za trenutnog korisnika
msi-scope-no-per-user-description = Zahteva administratorske privilegije
msi-scope-per-machine-description = Zahteva administratorske privilegije

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Spremno za instalaciju proizvoda { $app_title }
msi-ready-install-text = Kliknite na dugme „Instaliraj“ da biste započeli instalaciju. Kliknite na dugme „Nazad“ da biste pregledali ili promenili bilo koju postavku instalacije.
msi-ready-change-title = Spremno za promenu proizvoda { $app_title }
msi-ready-change-text = Kliknite na dugme „Promeni“ da biste započeli promenu instalacije. Kliknite na dugme „Nazad“ da biste pregledali ili promenili bilo koju postavku instalacije.
msi-ready-repair-title = Spremno za popravku proizvoda { $app_title }
msi-ready-repair-text = Kliknite na dugme „Popravi“ da biste započeli popravku. Kliknite na dugme „Nazad“ da biste pregledali ili promenili bilo koju postavku instalacije.
msi-ready-remove-title = Spremno za uklanjanje proizvoda { $app_title }
msi-ready-remove-text = Kliknite na dugme „Ukloni“ da biste uklonili { $app_title } sa računara. Kliknite na dugme „Nazad“ da biste pregledali ili promenili bilo koju postavku instalacije.
msi-ready-update-title = Spremno za ažuriranje proizvoda { $app_title }
msi-ready-update-text = Kliknite na dugme „Ažuriraj“ da biste započeli ažuriranje. Kliknite na dugme „Nazad“ da biste pregledali ili promenili bilo koju postavku instalacije.
msi-ready-btn-install = &Instaliraj
msi-ready-btn-change = &Promeni
msi-ready-btn-repair = Po&pravi
msi-ready-btn-remove = &Ukloni
msi-ready-btn-update = Až&uriraj

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Instalacija proizvoda { $app_title }
msi-progress-installing-text = Sačekajte dok čarobnjak za instalaciju instalira { $app_title }.
msi-progress-changing-title = Promena proizvoda { $app_title }
msi-progress-changing-text = Sačekajte dok čarobnjak za instalaciju promeni { $app_title }.
msi-progress-repairing-title = Popravka proizvoda { $app_title }
msi-progress-repairing-text = Sačekajte dok čarobnjak za instalaciju popravi { $app_title }.
msi-progress-removing-title = Uklanjanje proizvoda { $app_title }
msi-progress-removing-text = Sačekajte dok čarobnjak za instalaciju ukloni { $app_title }.
msi-progress-updating-title = Ažuriranje proizvoda { $app_title }
msi-progress-updating-text = Sačekajte dok čarobnjak za instalaciju ažurira { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Dobro došli u čarobnjak za instalaciju proizvoda { $app_title }
msi-maint-welcome-description = Čarobnjak za instalaciju će vam omogućiti da popravite ili uklonite { $app_title }. Kliknite na dugme „Dalje“ da biste nastavili ili na dugme „Otkaži“ da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Promena, popravka ili uklanjanje instalacije
msi-maint-type-description = Izaberite operaciju koju želite da izvršite.
msi-maint-change-button = &Promeni...
msi-maint-change-tooltip = Promeni...
msi-maint-change-text = Omogućava korisnicima da promene koje su funkcije programa instalirane i da promene pojedinačne funkcije.
msi-maint-change-disabled = Promena je trenutno onemogućena.
msi-maint-repair-button = Po&pravi
msi-maint-repair-tooltip = Popravi
msi-maint-repair-text = Popravlja greške u najnovijoj instalaciji - popravlja datoteke, prečice i stavke registratora koje nedostaju ili koje su oštećene.
msi-maint-repair-disabled = Popravka je trenutno onemogućena.
msi-maint-remove-button = &Ukloni
msi-maint-remove-tooltip = Ukloni
msi-maint-remove-text = Uklanja { $app_title } sa vašeg računara.
msi-maint-remove-disabled = Uklanjanje je trenutno onemogućeno.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Želite li zaista da otkažete instalaciju proizvoda { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Promena trenutne odredišne fascikle
msi-browse-description = Potražite odredišnu fasciklu.
msi-browse-combo-label = &Pogledaj u:
msi-browse-path-label = Ime &fascikle:
msi-browse-up-tooltip = Nagore za jedan nivo
msi-browse-new-folder-tooltip = Kreirajte novu fasciklu

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Navedeni odredišni direktorijum je nevažeći ili se nalazi na nepodržanom tipu disk jedinice.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Zahtevi za prostor na disku
msi-disk-cost-description = Prostor na disku koji je potreban za instalaciju izabranih funkcija.
msi-disk-cost-text = Na markiranim volumenima nema dovoljno dostupnog prostora za trenutno izabrane funkcije. Možete ukloniti neke datoteke iz markiranih volumena, instalirati manje funkcija na lokalne disk jedinice ili izabrati druge odredišne disk jedinice.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informacije instalatera za { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Čarobnjak za instalaciju proizvoda { $app_title } je pre vremena završio sa radom
msi-fatal-description1 = Instalacija proizvoda { $app_title } je prekinuta. Sistem nije izmenjen. Da biste kasnije instalirali ovaj program, ponovo pokrenite instalaciju.
msi-fatal-description2 = Kliknite na dugme „Završi“ da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Čarobnjak za instalaciju proizvoda { $app_title } je prekinut
msi-user-exit-description1 = Instalacija proizvoda { $app_title } je prekinuta. Sistem nije izmenjen. Da biste ovaj program instalirali kasnije, ponovo pokrenite instalaciju.
msi-user-exit-description2 = Kliknite na dugme „Završi“ da biste izašli iz čarobnjaka za instalaciju.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Datoteke u upotrebi
msi-files-in-use-description = Neke datoteke koje treba ažurirati trenutno se koriste.
msi-files-in-use-text = Sledeće aplikacije koriste datoteke koje ova instalacija treba da ažurira. Zatvorite ove aplikacije, a zatim kliknite na dugme „Pokušaj opet“ da biste nastavili sa instalacijom ili na dugme „Otkaži“ da biste izašli iz nje.
msi-files-in-use-exit = I&zađi

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Datoteke u upotrebi
msi-rm-files-in-use-description = Neke datoteke koje treba ažurirati trenutno se koriste.
msi-rm-files-in-use-text = Sledeće aplikacije koriste datoteke koje ova instalacija treba da ažurira. Možete dozvoliti čarobnjaku za instalaciju da automatski zatvori i pokuša ponovo da ih pokrene ili ih možete ručno zatvoriti i kliknuti na dugme „U redu“ da biste nastavili sa instalacijom.
msi-rm-files-in-use-use-rm = Automatski &zatvori aplikacije i pokušaj ponovo da ih pokreneš nakon završetka instalacije.
msi-rm-files-in-use-dont-use-rm = &Ne zatvaraj aplikacije. (Biće potrebno ponovno pokretanje sistema.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Nastavak rada čarobnjaka za instalaciju proizvoda { $app_title }
msi-resume-description = Čarobnjak za instalaciju će dovršiti instalaciju proizvoda { $app_title } na računaru. Kliknite na dugme „Instaliraj“ da biste nastavili ili na dugme „Otkaži“ da biste izašli iz čarobnjaka za instalaciju.
msi-resume-btn-install = &Instaliraj

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Prečica za { $app_title } na radnoj površini
msi-start-menu-shortcut-description = Prečica za { $app_title } u Start meniju
# MSI Installer UI - Readme Dialog
msi-readme-title = Važne informacije
msi-readme-description = Molimo pročitajte sledeće informacije pre nego što nastavite.
