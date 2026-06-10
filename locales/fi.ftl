# Shared titles
title-update = { $app_title } -päivitys
title-setup = { $app_title } -asennus
title-uninstall = { $app_title } -poisto
error-title = { $program_name } -virhe

# Shared buttons
btn-cancel = Peruuta
btn-install-update = Asenna päivitys
btn-install = Asenna
btn-update = Päivitä
btn-downgrade = Alenna versiota
btn-repair = Korjaa
btn-open-log = Avaa loki
btn-open-install-dir = Avaa asennuskansio
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Järjestelmänvalvojan oikeudet vaaditaan
elevate-body = { $app_title } tarvitsee järjestelmänvalvojan oikeudet versiota { $app_version } asennettaessa. Salli päivityksen jatkua?

# Restart required (prerequisite.rs)
restart-header = Uudelleenkäynnistys vaaditaan
restart-body = Tietokone on käynnistettävä uudelleen, ennen kuin asennusta voidaan jatkaa. Käynnistä tietokone uudelleen ja suorita asennus uudelleen.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Lisäkomponentteja vaaditaan
missing-deps-body = { $app_title } edellyttää, että seuraavat asennetaan ensin: { $deps }. Haluatko ladata ja asentaa ne nyt?

# Uninstall with errors (uninstall)
uninstall-errors-header = Poisto valmistui ongelmin
uninstall-errors-body = { $app_title } poistettiin, mutta joitakin tiedostoja tai kansioita ei voitu poistaa. Voit poistaa ne käsin tai asentaa sovelluksen uudelleen ja yrittää poistoa uudelleen.
uninstall-errors-log = Tiedot tallennettiin: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } on jo asennettu
overwrite-repair-body = Tämä sovellus on jo asennettu tietokoneeseesi. Jos se ei toimi oikein, voit yrittää korjata sen asentamalla uudelleen.
overwrite-older-installed = { $app_title } on jo asennettu
overwrite-update-body = Versio { $old_version } on tällä hetkellä asennettuna. Haluatko päivittää versioon { $app_version }?
overwrite-newer-installed = Tuotteesta { $app_title } on jo asennettu uudempi versio
overwrite-downgrade-body = Versio { $old_version } on tällä hetkellä asennettuna, ja se on uudempi kuin tämä asennusohjelma. Aiemman version asentamista ei suositella ja se voi aiheuttaa ongelmia. Jatketaanko silti?
overwrite-footer = Asennettu sijaintiin: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Poisto valmis
uninstall-body = Sovellus on poistettu tietokoneestasi.

# Install hook failed (install.rs)
install-hook-header = Asennus onnistui osittain
install-hook-body = Asennus on valmis, mutta jotkin vaiheet saattoivat epäonnistua. Jos sovellus ei toimi oikein, voit yrittää asentaa sen uudelleen tai ottaa yhteyttä sovelluksen tekijään.

# Splash fallback (splash.rs)
splash-header = Asennetaan { $app_title }
splash-body = Määritetään { $app_title } { $app_version }, odota...

# Dependency download (prerequisite.rs)
deps-download-header = Ladataan tarvittavaa komponenttia
deps-download-body = Ladataan { $dep_name }, odota...

# Apply progress (apply_*_impl.rs)
apply-header = Asennetaan päivitystä
apply-body = Päivitetään versioon { $app_version }, odota...

# Start error (start_windows_impl.rs)
start-corrupt-header = Asennus on vaurioitunut
start-corrupt-body = Tätä sovellusta ei voi käynnistää, koska osa sen tiedostoista puuttuu tai on vaurioitunut. Asenna sovellus uudelleen ongelman korjaamiseksi.

# Generic error
error-header = Jokin meni vikaan

# Setup error (wix msi)
setup-error-header = Asennusta ei voitu jatkaa
setup-disk-space-insufficient = { $app_title } requires at least { $required_space } disk space to be installed. There is only { $available_space } available.

# MSI Installer UI - Common
msi-dlg-title = { $app_title } -asennus
msi-btn-back = &Edellinen
msi-btn-next = &Seuraava
msi-btn-cancel = Peruuta
msi-btn-finish = &Valmis
msi-btn-ok = OK
msi-btn-yes = &Kyllä
msi-btn-no = &Ei
msi-btn-retry = &Yritä uudelleen
msi-btn-ignore = &Ohita

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Tervetuloa tuotteen { $app_title } ohjattuun asennukseen
msi-welcome-description = Ohjattu asennus asentaa tuotteen { $app_title } tietokoneeseen. Voit jatkaa valitsemalla Seuraava tai lopettaa ohjatun asennuksen valitsemalla Peruuta.
msi-welcome-update-description = Ohjattu asennus päivittää tuotteen { $app_title } tietokoneeseen. Voit jatkaa valitsemalla Seuraava tai lopettaa ohjatun asennuksen valitsemalla Peruuta.

# MSI Installer UI - Exit Dialog
msi-exit-title = Tuotteen { $app_title } ohjattu asennus on suoritettu
msi-exit-description = Lopeta ohjattu asennus valitsemalla Valmis.
msi-exit-launch-checkbox = Käynnistä { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Tervetuloa tuotteen { $app_title } ohjattuun asennukseen
msi-prepare-description = Odota. Ohjattu asennus valmistautuu asennukseen.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Käyttöoikeussopimus
msi-license-description = Lue seuraava käyttöoikeussopimus huolellisesti.
msi-license-checkbox = &Hyväksyn käyttöoikeussopimuksen ehdot

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Asennuksen laajuus
msi-scope-description = Valitse asennuksen laajuus.
msi-scope-per-user = Asenna vain &sinulle
msi-scope-per-machine = Asenna &kaikille käyttäjille
msi-scope-per-user-description = Asentaa vain nykyiselle käyttäjälle
msi-scope-no-per-user-description = Vaatii järjestelmänvalvojan oikeudet
msi-scope-per-machine-description = Vaatii järjestelmänvalvojan oikeudet

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Valmiina asentamaan tuotteen { $app_title }
msi-ready-install-text = Voit aloittaa asennuksen valitsemalla Asenna. Voit tarkastella tai muuttaa asennuksen asetuksia valitsemalla Edellinen.
msi-ready-change-title = Valmiina muuttamaan tuotetta { $app_title }
msi-ready-change-text = Voit aloittaa asennuksen muuttamisen valitsemalla Muuta. Voit tarkastella tai muuttaa asennuksen asetuksia valitsemalla Edellinen.
msi-ready-repair-title = Valmiina korjaamaan tuotteen { $app_title }
msi-ready-repair-text = Voit aloittaa korjauksen valitsemalla Korjaa. Voit tarkastella tai muuttaa asennuksen asetuksia valitsemalla Edellinen.
msi-ready-remove-title = Valmiina poistamaan tuotteen { $app_title }
msi-ready-remove-text = Voit poistaa tuotteen { $app_title } tietokoneestasi valitsemalla Poista. Voit tarkastella tai muuttaa asennuksen asetuksia valitsemalla Edellinen.
msi-ready-update-title = Valmiina päivittämään tuotteen { $app_title }
msi-ready-update-text = Voit aloittaa päivityksen valitsemalla Päivitä. Voit tarkastella tai muuttaa asennuksen asetuksia valitsemalla Edellinen.
msi-ready-btn-install = &Asenna
msi-ready-btn-change = &Muuta
msi-ready-btn-repair = &Korjaa
msi-ready-btn-remove = &Poista
msi-ready-btn-update = &Päivitä

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Asennetaan tuotetta { $app_title }
msi-progress-installing-text = Odota. Ohjattu asennus asentaa tuotteen { $app_title }.
msi-progress-changing-title = Muutetaan tuotetta { $app_title }
msi-progress-changing-text = Odota. Ohjattu asennus muuttaa tuotteen { $app_title } asennusta.
msi-progress-repairing-title = Korjataan tuotetta { $app_title }
msi-progress-repairing-text = Odota. Ohjattu asennus korjaa tuotteen { $app_title }.
msi-progress-removing-title = Poistetaan tuotetta { $app_title }
msi-progress-removing-text = Odota. Ohjattu asennus poistaa tuotteen { $app_title }.
msi-progress-updating-title = Päivitetään tuotetta { $app_title }
msi-progress-updating-text = Odota. Ohjattu asennus päivittää tuotteen { $app_title }.
msi-progress-status = Tila:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Tervetuloa tuotteen { $app_title } ohjattuun asennukseen
msi-maint-welcome-description = Ohjattu asennus mahdollistaa tuotteen { $app_title } korjaamisen tai poistamisen. Voit jatkaa valitsemalla Seuraava tai lopettaa ohjatun asennuksen valitsemalla Peruuta.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Korjaa tai poista asennus tai muuta sitä
msi-maint-type-description = Valitse suoritettava toiminto.
msi-maint-change-button = &Muuta
msi-maint-change-tooltip = Muuta asennusta
msi-maint-change-text = Antaa käyttäjien muuttaa, mitkä ohjelmaominaisuudet asennetaan, ja muuttaa yksittäisiä ominaisuuksia.
msi-maint-change-disabled = Muuta ei ole tällä hetkellä käytettävissä.
msi-maint-repair-button = &Korjaa
msi-maint-repair-tooltip = Korjaa asennus
msi-maint-repair-text = Korjaa uusimman asennuksen virheitä korjaamalla puuttuvia tai vioittuneita tiedostoja, pikakuvakkeita ja rekisterimerkintöjä.
msi-maint-repair-disabled = Korjaa ei ole tällä hetkellä käytettävissä.
msi-maint-remove-button = &Poista
msi-maint-remove-tooltip = Poista asennus
msi-maint-remove-text = Poistaa tuotteen { $app_title } tietokoneesta.
msi-maint-remove-disabled = Poista ei ole tällä hetkellä käytettävissä.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Haluatko varmasti peruuttaa tuotteen { $app_title } asennuksen?

# MSI Installer UI - Browse Dialog
msi-browse-title = Vaihda nykyistä kohdekansiota
msi-browse-description = Selaa kohdekansioon.
msi-browse-combo-label = &Kohde:
msi-browse-path-label = &Kansion nimi:
msi-browse-up-tooltip = Yksi taso ylöspäin
msi-browse-new-folder-tooltip = Luo uusi kansio

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Määritetty kohdehakemisto on joko virheellinen tai sellaisessa asematyypissä, jota ei tueta.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Levytilavaatimukset
msi-disk-cost-description = Valittujen ominaisuuksien asentamiseen vaadittava levytila.
msi-disk-cost-text = Korostetuissa asemissa ei ole tarpeeksi vapaata levytilaa valituille ominaisuuksille. Voit poistaa tiedostoja korostetuista asemista, asentaa vähemmän ominaisuuksia paikallisille asemille tai valita eri kohdeasemat.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } -asennusohjelman tiedot

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Tuotteen { $app_title } ohjattu asennus päättyi ennenaikaisesti
msi-fatal-description1 = Tuotteen { $app_title } asennus keskeytettiin. Järjestelmääsi ei ole muutettu. Voit asentaa tämän ohjelman myöhemmin suorittamalla asennuksen uudelleen.
msi-fatal-description2 = Lopeta ohjattu asennus valitsemalla Valmis.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Tuotteen { $app_title } ohjattu asennus keskeytyi
msi-user-exit-description1 = Tuotteen { $app_title } asennus keskeytettiin. Järjestelmääsi ei ole muutettu. Voit asentaa tämän ohjelman myöhemmin suorittamalla asennuksen uudelleen.
msi-user-exit-description2 = Lopeta ohjattu asennus valitsemalla Valmis.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Tiedostoja on käytössä
msi-files-in-use-description = Jotkin päivitettävät tiedostot ovat tällä hetkellä käytössä.
msi-files-in-use-text = Seuraavat sovellukset käyttävät tiedostoja, jotka tämän asennuksen on päivitettävä. Sulje sovellukset ja jatka sitten asennusta valitsemalla Yritä uudelleen tai lopeta valitsemalla Peruuta.
msi-files-in-use-exit = &Lopeta

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Tiedostoja on käytössä
msi-rm-files-in-use-description = Jotkin päivitettävät tiedostot ovat tällä hetkellä käytössä.
msi-rm-files-in-use-text = Seuraavat sovellukset käyttävät tiedostoja, jotka tämän asennuksen on päivitettävä. Voit antaa ohjatun asennuksen automaattisesti sulkea ja yrittää käynnistää ne uudelleen, tai voit sulkea ne käsin ja jatkaa asennusta valitsemalla OK.
msi-rm-files-in-use-use-rm = &Sulje sovellukset automaattisesti ja yritä käynnistää ne uudelleen asennuksen jälkeen.
msi-rm-files-in-use-dont-use-rm = &Älä sulje sovelluksia. (Uudelleenkäynnistys vaaditaan.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Jatketaan tuotteen { $app_title } ohjattua asennusta
msi-resume-description = Ohjattu asennus viimeistelee tuotteen { $app_title } asennuksen tietokoneeseesi. Voit jatkaa valitsemalla Asenna tai lopettaa ohjatun asennuksen valitsemalla Peruuta.
msi-resume-btn-install = &Asenna

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Tuotteen { $app_title } työpöytäpikakuvake
msi-start-menu-shortcut-description = Tuotteen { $app_title } Käynnistä-valikon pikakuvake
# MSI Installer UI - Readme Dialog
msi-readme-title = Tärkeitä tietoja
msi-readme-description = Lue seuraavat tiedot ennen jatkamista.
