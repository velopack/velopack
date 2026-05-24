# Shared titles
title-update = { $app_title } frissítése
title-setup = { $app_title } telepítése
title-uninstall = { $app_title } eltávolítása
error-title = { $program_name } hiba

# Shared buttons
btn-cancel = Mégse
btn-install-update = Frissítés telepítése
btn-install = Telepítés
btn-update = Frissítés
btn-downgrade = Visszaminősítés
btn-repair = Javítás
btn-open-log = Napló megnyitása
btn-open-install-dir = Telepítési mappa megnyitása

# Elevation (dialogs_common.rs)
elevate-header = Rendszergazdai engedély szükséges
elevate-body = A(z) { $app_title } alkalmazásnak rendszergazdai engedélyre van szüksége a(z) { $app_version } verzió telepítéséhez. Engedélyezi a frissítés folytatását?

# Restart required (prerequisite.rs)
restart-header = Újraindítás szükséges
restart-body = A telepítés folytatása előtt újra kell indítani a számítógépet. Indítsa újra a számítógépet, majd futtassa újra a telepítőt.

# Missing dependencies (prerequisite.rs)
missing-deps-header = További összetevők szükségesek
missing-deps-body = A(z) { $app_title } használatához először a következőket kell telepíteni: { $deps }. Szeretné most letölteni és telepíteni őket?

# Uninstall with errors (uninstall)
uninstall-errors-header = Az eltávolítás problémákkal fejeződött be
uninstall-errors-body = A(z) { $app_title } el lett távolítva, de néhány fájl vagy mappa eltávolítása nem sikerült. Ezeket eltávolíthatja manuálisan, vagy újratelepítheti az alkalmazást, és újra megpróbálhatja eltávolítani.
uninstall-errors-log = A részletek mentve ide: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = A(z) { $app_title } már telepítve van
overwrite-repair-body = Ez az alkalmazás már telepítve van a számítógépen. Ha nem működik megfelelően, megpróbálhatja kijavítani újratelepítéssel.
overwrite-older-installed = A(z) { $app_title } már telepítve van
overwrite-update-body = Jelenleg a(z) { $old_version } verzió van telepítve. Szeretné frissíteni a(z) { $app_version } verzióra?
overwrite-newer-installed = A(z) { $app_title } újabb verziója már telepítve van
overwrite-downgrade-body = Jelenleg a(z) { $old_version } verzió van telepítve, amely újabb, mint ez a telepítő. A visszaminősítés nem ajánlott, és problémákat okozhat. Mégis folytatja?
overwrite-footer = Telepítve ide: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Az eltávolítás befejeződött
uninstall-body = Az alkalmazás sikeresen el lett távolítva a számítógépről.

# Install hook failed (install.rs)
install-hook-header = A telepítés részben sikerült
install-hook-body = A telepítés befejeződött, de néhány lépés meghiúsulhatott. Ha az alkalmazás nem működik megfelelően, próbálja meg újratelepíteni vagy lépjen kapcsolatba az alkalmazás szerzőjével.

# Splash fallback (splash.rs)
splash-header = A(z) { $app_title } telepítése
splash-body = A(z) { $app_title } { $app_version } beállítása, kérjük, várjon...

# Dependency download (prerequisite.rs)
deps-download-header = Szükséges összetevő letöltése
deps-download-body = A(z) { $dep_name } letöltése folyamatban, kérjük, várjon...

# Apply progress (apply_*_impl.rs)
apply-header = Frissítés telepítése
apply-body = Frissítés a(z) { $app_version } verzióra, kérjük, várjon...

# Start error (start_windows_impl.rs)
start-corrupt-header = A telepítés sérült
start-corrupt-body = Ez az alkalmazás nem indítható el, mert néhány fájlja hiányzik vagy sérült. A hiba elhárításához telepítse újra az alkalmazást.

# Generic error
error-header = Hiba történt

# Setup error (wix msi)
setup-error-header = A telepítés nem folytatható

# MSI Installer UI - Common
msi-dlg-title = A(z) { $app_title } telepítése
msi-btn-back = &Vissza
msi-btn-next = &Tovább
msi-btn-cancel = Mégse
msi-btn-finish = &Befejezés
msi-btn-ok = OK
msi-btn-yes = &Igen
msi-btn-no = &Nem
msi-btn-retry = Újr&a
msi-btn-ignore = &Mellőzés

# MSI Installer UI - Welcome Dialog
msi-welcome-title = A(z) { $app_title } telepítése – üdvözli a varázsló
msi-welcome-description = A telepítővarázsló telepíti a(z) { $app_title } alkalmazást a számítógépre. A folytatáshoz kattintson a Tovább gombra, a telepítővarázslóból való kilépéshez a Mégse gombra.
msi-welcome-update-description = A telepítővarázsló frissíti a(z) { $app_title } terméket a számítógépen. A folytatáshoz kattintson a Tovább gombra, a telepítővarázslóból való kilépéshez a Mégse gombra.

# MSI Installer UI - Exit Dialog
msi-exit-title = A(z) { $app_title } telepítővarázsló futása befejeződött
msi-exit-description = A telepítővarázslóból való kilépéshez kattintson a Befejezés gombra.
msi-exit-launch-checkbox = A(z) { $app_title } indítása

# MSI Installer UI - Prepare Dialog
msi-prepare-title = A(z) { $app_title } telepítése – üdvözli a varázsló
msi-prepare-description = Várjon, amíg a telepítővarázsló felkészül, hogy végigvezesse Önt a telepítés folyamatán.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Végfelhasználói licencszerződés
msi-license-description = Figyelmesen olvassa el az alábbi licencszerződést.
msi-license-checkbox = &Elfogadom a licencszerződés feltételeit

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Telepítési hatókör
msi-scope-description = Válassza ki a telepítési hatókört.
msi-scope-per-user = Telepítés &csak önmaga számára
msi-scope-per-machine = Telepítés &minden felhasználó számára
msi-scope-per-user-description = Csak az aktuális felhasználó számára telepít
msi-scope-no-per-user-description = Rendszergazdai jogok szükségesek
msi-scope-per-machine-description = Rendszergazdai jogok szükségesek

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = A telepítő készen áll a(z) { $app_title } telepítésére
msi-ready-install-text = A telepítés megkezdéséhez kattintson a Telepítés gombra. A Vissza gombra kattintva áttekintheti és módosíthatja a telepítési beállításokat.
msi-ready-change-title = A telepítő készen áll a(z) { $app_title } módosítására
msi-ready-change-text = A módosítás megkezdéséhez kattintson a Módosítás gombra. A Vissza gombra kattintva áttekintheti és módosíthatja a telepítési beállításokat.
msi-ready-repair-title = A telepítő készen áll a(z) { $app_title } kijavítására
msi-ready-repair-text = A javítás megkezdéséhez kattintson a Javítás gombra. A Vissza gombra kattintva áttekintheti és módosíthatja a telepítési beállításokat.
msi-ready-remove-title = A telepítő készen áll a(z) { $app_title } eltávolítására
msi-ready-remove-text = A(z) { $app_title } eltávolításához kattintson az Eltávolítás gombra. A Vissza gombra kattintva áttekintheti és módosíthatja a telepítési beállításokat.
msi-ready-update-title = A telepítő készen áll a(z) { $app_title } frissítésére
msi-ready-update-text = A frissítés megkezdéséhez kattintson a Frissítés gombra. A Vissza gombra kattintva áttekintheti és módosíthatja a telepítési beállításokat.
msi-ready-btn-install = &Telepítés
msi-ready-btn-change = &Módosítás
msi-ready-btn-repair = &Javítás
msi-ready-btn-remove = &Eltávolítás
msi-ready-btn-update = &Frissítés

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = A(z) { $app_title } telepítése
msi-progress-installing-text = Várjon, amíg a telepítővarázsló telepíti a(z) { $app_title } terméket.
msi-progress-changing-title = A(z) { $app_title } módosítása
msi-progress-changing-text = Várjon, amíg a telepítővarázsló módosítja a(z) { $app_title } terméket.
msi-progress-repairing-title = A(z) { $app_title } kijavítása
msi-progress-repairing-text = Várjon, amíg a telepítővarázsló kijavítja a(z) { $app_title } terméket.
msi-progress-removing-title = A(z) { $app_title } eltávolítása
msi-progress-removing-text = Várjon, amíg a telepítővarázsló eltávolítja a(z) { $app_title } terméket.
msi-progress-updating-title = A(z) { $app_title } frissítése
msi-progress-updating-text = Kis türelmet, a telepítővarázsló a(z) { $app_title } frissítését végzi.
msi-progress-status = Állapot:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = A(z) { $app_title } telepítése – üdvözli a varázsló
msi-maint-welcome-description = A telepítővarázslóval kijavíthatja vagy eltávolíthatja a(z) { $app_title } terméket. A folytatáshoz kattintson a Tovább gombra, a telepítővarázslóból való kilépéshez a Mégse gombra.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = A telepítés módosítása, kijavítása vagy eltávolítása
msi-maint-type-description = Válasszon a rendelkezésre álló lehetőségek közül.
msi-maint-change-button = &Módosítás...
msi-maint-change-tooltip = Módosítás...
msi-maint-change-text = Lehetővé teszi a felhasználók számára a telepített programszolgáltatások és az egyes szolgáltatások módosítását.
msi-maint-change-disabled = A módosítás jelenleg le van tiltva.
msi-maint-repair-button = &Javítás
msi-maint-repair-tooltip = Javítás
msi-maint-repair-text = Kijavítja a legutóbbi telepítés hibáit úgy, hogy helyreállítja a hiányzó és sérült fájlokat, parancsikonokat és beállításjegyzékbeli bejegyzéseket.
msi-maint-repair-disabled = A javítás jelenleg le van tiltva.
msi-maint-remove-button = &Eltávolítás
msi-maint-remove-tooltip = Eltávolítás
msi-maint-remove-text = A(z) { $app_title } eltávolítása a számítógépről.
msi-maint-remove-disabled = Az eltávolítás jelenleg le van tiltva.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Biztosan megszakítja a(z) { $app_title } telepítését?

# MSI Installer UI - Browse Dialog
msi-browse-title = Aktuális célmappa módosítása
msi-browse-description = Tallózással keresse meg a célmappát.
msi-browse-combo-label = &Hely:
msi-browse-path-label = &Mappa neve:
msi-browse-up-tooltip = Egy szinttel feljebb
msi-browse-new-folder-tooltip = Új mappa létrehozása

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = A megadott célkönyvtár érvénytelen, vagy nem támogatott típusú meghajtón található.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Lemezterület-szükséglet
msi-disk-cost-description = A kijelölt szolgáltatások telepítéséhez szükséges lemezterület.
msi-disk-cost-text = A kijelölt köteteken nincs elég szabad lemezterület a jelenleg kiválasztott szolgáltatások telepítéséhez. A probléma megoldásához törölhet néhány fájlt a kijelölt kötetekről, telepíthet kevesebb szolgáltatást a helyi meghajtó(k)ra, vagy más célmeghajtót választhat.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = A(z) { $app_title } telepítőjének információi

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = A(z) { $app_title } telepítővarázsló futása idő előtt véget ért
msi-fatal-description1 = A(z) { $app_title } telepítése megszakadt. A rendszer nem módosult. A programot később a telepítés futtatásával telepítheti.
msi-fatal-description2 = A telepítővarázslóból való kilépéshez kattintson a Befejezés gombra.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = A(z) { $app_title } telepítővarázsló futása megszakadt
msi-user-exit-description1 = A(z) { $app_title } telepítése megszakadt. A rendszer nem módosult. A programot később a telepítés futtatásával telepítheti.
msi-user-exit-description2 = A telepítővarázslóból való kilépéshez kattintson a Befejezés gombra.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Használatban lévő fájlok
msi-files-in-use-description = Néhány frissítendő fájl jelenleg használatban van.
msi-files-in-use-text = A következő alkalmazások jelenleg használják a telepítő által frissítendő fájlokat. Zárja be ezeket az alkalmazásokat, majd az Újra gombra kattintva folytassa a telepítést, vagy a Mégse gombra kattintva lépjen ki a telepítőből.
msi-files-in-use-exit = &Kilépés

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Használatban lévő fájlok
msi-rm-files-in-use-description = Néhány frissítendő fájl jelenleg használatban van.
msi-rm-files-in-use-text = A következő alkalmazások jelenleg használják a telepítő által frissítendő fájlokat. Engedélyezheti, hogy a telepítővarázsló automatikusan bezárja és a telepítés befejezése után megpróbálja újraindítani őket, vagy bezárhatja őket manuálisan és az OK gombra kattintva folytathatja a telepítést.
msi-rm-files-in-use-use-rm = Az alkalmazások automatikus &bezárása és újraindítása a telepítés befejezése után.
msi-rm-files-in-use-dont-use-rm = Az alkalmazások bezárásának &mellőzése. (Újraindítás szükséges.)

# MSI Installer UI - Resume Dialog
msi-resume-title = A(z) { $app_title } telepítővarázsló folytatása
msi-resume-description = A telepítővarázsló befejezi a(z) { $app_title } telepítését a számítógépre. A folytatáshoz kattintson a Telepítés gombra, a varázslóból való kilépéshez a Mégse gombra.
msi-resume-btn-install = &Telepítés

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Asztali parancsikon a(z) { $app_title } alkalmazáshoz
msi-start-menu-shortcut-description = Start menü parancsikon a(z) { $app_title } alkalmazáshoz
# MSI Installer UI - Readme Dialog
msi-readme-title = Fontos információk
msi-readme-description = Kérjük, olvassa el az alábbi információkat a folytatás előtt.
