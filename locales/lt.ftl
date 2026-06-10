# Shared titles
title-update = { $app_title } naujinimas
title-setup = { $app_title } sąranka
title-uninstall = { $app_title } šalinimas
error-title = { $program_name } klaida

# Shared buttons
btn-cancel = Atšaukti
btn-install-update = Diegti naujinį
btn-install = Diegti
btn-update = Naujinti
btn-downgrade = Senesnė versija
btn-repair = Taisyti
btn-open-log = Atidaryti žurnalą
btn-open-install-dir = Atidaryti diegimo aplanką
btn-ok = Gerai
# Elevation (dialogs_common.rs)
elevate-header = Reikalingos administratoriaus teisės
elevate-body = Programai { $app_title } reikia administratoriaus teisių, kad būtų galima įdiegti versiją { $app_version }. Ar leisti šiam naujinimui tęsti?

# Restart required (prerequisite.rs)
restart-header = Reikia paleisti iš naujo
restart-body = Prieš tęsiant sąranką, reikia paleisti kompiuterį iš naujo. Iš naujo paleiskite kompiuterį ir vėl paleiskite sąranką.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Reikia papildomų komponentų
missing-deps-body = Programai { $app_title } pirmiausia reikia įdiegti: { $deps }. Ar norite juos atsisiųsti ir įdiegti dabar?

# Uninstall with errors (uninstall)
uninstall-errors-header = Šalinimas baigtas su problemomis
uninstall-errors-body = Programa { $app_title } pašalinta, bet kai kurių failų ar aplankų nepavyko pašalinti. Galite juos rankiniu būdu ištrinti arba iš naujo įdiegti programą ir bandyti šalinti dar kartą.
uninstall-errors-log = Išsami informacija įrašyta į: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } jau įdiegta
overwrite-repair-body = Ši programa jau įdiegta kompiuteryje. Jei ji veikia netinkamai, galite pabandyti ją pataisyti diegdami iš naujo.
overwrite-older-installed = { $app_title } jau įdiegta
overwrite-update-body = Šiuo metu įdiegta versija { $old_version }. Ar norite naujinti į versiją { $app_version }?
overwrite-newer-installed = Naujesnė programos { $app_title } versija jau įdiegta
overwrite-downgrade-body = Šiuo metu įdiegta versija { $old_version }, kuri yra naujesnė nei ši diegimo programa. Senesnės versijos diegti nerekomenduojama ir tai gali sukelti problemų. Vis tiek tęsti?
overwrite-footer = Įdiegta vietoje: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Šalinimas baigtas
uninstall-body = Programa sėkmingai pašalinta iš kompiuterio.

# Install hook failed (install.rs)
install-hook-header = Diegimas iš dalies sėkmingas
install-hook-body = Diegimas baigtas, bet kai kurie veiksmai galėjo nepavykti. Jei programa veikia netinkamai, pabandykite ją iš naujo įdiegti arba kreipkitės į programos kūrėją.

# Splash fallback (splash.rs)
splash-header = Diegiama { $app_title }
splash-body = Nustatoma { $app_title } { $app_version }, palaukite...

# Dependency download (prerequisite.rs)
deps-download-header = Atsisiunčiamas reikiamas komponentas
deps-download-body = Atsisiunčiama { $dep_name }, palaukite...

# Apply progress (apply_*_impl.rs)
apply-header = Diegiamas naujinys
apply-body = Naujinama į versiją { $app_version }, palaukite...

# Start error (start_windows_impl.rs)
start-corrupt-header = Diegimas pažeistas
start-corrupt-body = Ši programa negali būti paleista, nes trūksta kai kurių jos failų arba jie pažeisti. Norėdami tai išspręsti, iš naujo įdiekite programą.

# Generic error
error-header = Įvyko klaida

# Setup error (wix msi)
setup-error-header = Sąrankos tęsti nepavyko
setup-disk-space-insufficient = { $app_title } requires at least { $required_space } disk space to be installed. There is only { $available_space } available.

# MSI Installer UI - Common
msi-dlg-title = { $app_title } sąranka
msi-btn-back = &Atgal
msi-btn-next = &Pirmyn
msi-btn-cancel = Atšaukti
msi-btn-finish = &Baigti
msi-btn-ok = Gerai
msi-btn-yes = &Taip
msi-btn-no = &Ne
msi-btn-retry = &Kartoti
msi-btn-ignore = &Nepaisyti

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Sveiki! Čia { $app_title } sąrankos vedlys
msi-welcome-description = Sąrankos vedlys įdiegs { $app_title } jūsų kompiuteryje. Spustelėkite Pirmyn, norėdami tęsti, arba spustelėkite Atšaukti, norėdami uždaryti sąrankos vedlį.
msi-welcome-update-description = Sąrankos vedlys atnaujins { $app_title } jūsų kompiuteryje. Spustelėkite Pirmyn, norėdami tęsti, arba spustelėkite Atšaukti, norėdami uždaryti sąrankos vedlį.

# MSI Installer UI - Exit Dialog
msi-exit-title = Atlikti { $app_title } sąrankos vedlio nurodymai
msi-exit-description = Norėdami uždaryti sąrankos vedlį, spustelėkite mygtuką Baigti.
msi-exit-launch-checkbox = Paleisti { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Sveiki! Čia { $app_title } sąrankos vedlys
msi-prepare-description = Palaukite, kol sąrankos vedlys pasiruoš vadovauti diegimo procesui.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Galutinio vartotojo licencijos sutartis
msi-license-description = Atidžiai perskaitykite šią licencijos sutartį.
msi-license-checkbox = &Sutinku su licencijos sutarties sąlygomis

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Diegimo aprėptis
msi-scope-description = Pasirinkite diegimo aprėptį.
msi-scope-per-user = Diegti tik &sau
msi-scope-per-machine = Diegti visiems &vartotojams
msi-scope-per-user-description = Diegia tik dabartiniam vartotojui
msi-scope-no-per-user-description = Reikalingos administratoriaus teisės
msi-scope-per-machine-description = Reikalingos administratoriaus teisės

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Parengta diegti { $app_title }
msi-ready-install-text = Norėdami pradėti diegti, spustelėkite mygtuką Diegti. Norėdami peržiūrėti ar pakeisti bet kurį diegimo parametrą, spustelėkite mygtuką Atgal.
msi-ready-change-title = Parengta keisti { $app_title }
msi-ready-change-text = Norėdami pradėti keisti diegimą, spustelėkite mygtuką Keisti. Norėdami peržiūrėti ar pakeisti bet kurį diegimo parametrą, spustelėkite mygtuką Atgal.
msi-ready-repair-title = Parengta taisyti { $app_title }
msi-ready-repair-text = Norėdami pradėti taisymą, spustelėkite mygtuką Taisyti. Norėdami peržiūrėti ar pakeisti bet kurį diegimo parametrą, spustelėkite mygtuką Atgal.
msi-ready-remove-title = Parengta pašalinti { $app_title }
msi-ready-remove-text = Norėdami pašalinti { $app_title } iš savo kompiuterio, spustelėkite mygtuką Šalinti. Norėdami peržiūrėti ar pakeisti bet kurį diegimo parametrą, spustelėkite mygtuką Atgal.
msi-ready-update-title = Parengta naujinti { $app_title }
msi-ready-update-text = Norėdami pradėti naujinimą, spustelėkite mygtuką Naujinti. Norėdami peržiūrėti ar pakeisti bet kurį diegimo parametrą, spustelėkite mygtuką Atgal.
msi-ready-btn-install = &Diegti
msi-ready-btn-change = &Keisti
msi-ready-btn-repair = Ta&isyti
msi-ready-btn-remove = &Šalinti
msi-ready-btn-update = &Naujinti

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } diegimas
msi-progress-installing-text = Palaukite, kol sąrankos vedlys įdiegs { $app_title }.
msi-progress-changing-title = { $app_title } keitimas
msi-progress-changing-text = Palaukite, kol sąrankos vedlys pakeis { $app_title }.
msi-progress-repairing-title = { $app_title } taisymas
msi-progress-repairing-text = Palaukite, kol sąrankos vedlys pataisys { $app_title }.
msi-progress-removing-title = { $app_title } šalinimas
msi-progress-removing-text = Palaukite, kol sąrankos vedlys pašalins { $app_title }.
msi-progress-updating-title = { $app_title } naujinimas
msi-progress-updating-text = Palaukite, kol sąrankos vedlys atnaujins { $app_title }.
msi-progress-status = Būsena:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Sveiki! Čia { $app_title } sąrankos vedlys
msi-maint-welcome-description = Sąrankos vedlys leis taisyti arba šalinti { $app_title }. Spustelėkite Pirmyn, norėdami tęsti, arba spustelėkite Atšaukti, norėdami uždaryti sąrankos vedlį.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Įdiegties keitimas, taisymas arba šalinimas
msi-maint-type-description = Pažymėkite norimą atlikti operaciją.
msi-maint-change-button = &Keisti
msi-maint-change-tooltip = Keisti įdiegtį
msi-maint-change-text = Leidžia vartotojams keisti, kurios programos priemonės įdiegtos, ir keisti atskiras priemones.
msi-maint-change-disabled = Šiuo metu keisti negalima.
msi-maint-repair-button = Ta&isyti
msi-maint-repair-tooltip = Taisyti įdiegtį
msi-maint-repair-text = Ištaiso naujausios įdiegties klaidas pataisydama trūkstamus ar sugadintus failus, sparčiąsias nuorodas ir registro įrašus.
msi-maint-repair-disabled = Šiuo metu taisyti negalima.
msi-maint-remove-button = &Šalinti
msi-maint-remove-tooltip = Šalinti įdiegtį
msi-maint-remove-text = Pašalina { $app_title } iš kompiuterio.
msi-maint-remove-disabled = Šiuo metu šalinti negalima.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Ar tikrai norite atšaukti { $app_title } diegimą?

# MSI Installer UI - Browse Dialog
msi-browse-title = Keisti dabartinį paskirties aplanką
msi-browse-description = Eiti į paskirties aplanką.
msi-browse-combo-label = &Kur ieškoti:
msi-browse-path-label = &Aplanko pavadinimas:
msi-browse-up-tooltip = Vienu lygiu aukščiau
msi-browse-new-folder-tooltip = Kurti naują aplanką

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Nurodytas paskirties katalogas yra netinkamas arba diske, kuris nepalaikomas.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Diske reikalinga vieta
msi-disk-cost-description = Reikalinga vieta diske pažymėtoms priemonėms diegti.
msi-disk-cost-text = Paryškintuose tomuose nepakanka vietos pasirinktoms priemonėms. Galite pašalinti kai kuriuos failus iš paryškintų tomų, įdiegti mažiau priemonių į vietinius diskus arba pasirinkti kitus paskirties diskus.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } diegimo programos informacija

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } sąrankos vedlys nutrūko per anksti
msi-fatal-description1 = { $app_title } sąranka buvo nutraukta. Jūsų sistema nepakeista. Norėdami įdiegti šią programą vėliau, paleiskite sąranką dar kartą.
msi-fatal-description2 = Norėdami uždaryti sąrankos vedlį, spustelėkite mygtuką Baigti.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } sąrankos vedlio darbas nutrūko
msi-user-exit-description1 = { $app_title } sąranka buvo nutraukta. Jūsų sistema nepakeista. Norėdami įdiegti šią programą vėliau, paleiskite sąranką dar kartą.
msi-user-exit-description2 = Norėdami uždaryti sąrankos vedlį, spustelėkite mygtuką Baigti.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Naudojami failai
msi-files-in-use-description = Kai kurie failai, kuriuos reikia atnaujinti, šiuo metu naudojami.
msi-files-in-use-text = Šios programos naudoja sąrankos metu būtinus atnaujinti failus. Uždarykite šias programas, tada spustelėkite mygtuką Kartoti, norėdami tęsti diegimą, arba mygtuką Atšaukti, norėdami iš jo išeiti.
msi-files-in-use-exit = I&šeiti

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Naudojami failai
msi-rm-files-in-use-description = Kai kurie failai, kuriuos reikia atnaujinti, šiuo metu naudojami.
msi-rm-files-in-use-text = Šios programos naudoja sąrankos metu būtinus atnaujinti failus. Galite leisti sąrankos vedliui automatiškai uždaryti ir pabandyti iš naujo paleisti šias programas arba galite uždaryti jas rankiniu būdu ir spustelėti Gerai, norėdami tęsti diegimą.
msi-rm-files-in-use-use-rm = &Automatiškai uždaryti programas ir pabandyti jas paleisti iš naujo baigus diegti.
msi-rm-files-in-use-dont-use-rm = &Neuždarykite programų. (Reikės iš naujo paleisti kompiuterį.)

# MSI Installer UI - Resume Dialog
msi-resume-title = { $app_title } sąrankos vedlys tęsia darbą
msi-resume-description = Sąrankos vedlys baigs diegti { $app_title } jūsų kompiuteryje. Norėdami tęsti, spustelėkite mygtuką Diegti, o norėdami išeiti iš sąrankos vedlio, spustelėkite mygtuką Atšaukti.
msi-resume-btn-install = &Diegti

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } darbalaukio sparčioji nuoroda
msi-start-menu-shortcut-description = { $app_title } meniu Pradžia sparčioji nuoroda
# MSI Installer UI - Readme Dialog
msi-readme-title = Svarbi informacija
msi-readme-description = Prieš tęsdami perskaitykite šią informaciją.
