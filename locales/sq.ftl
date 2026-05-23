# Shared titles
title-update = Përditësim i { $app_title }
title-setup = Rregullim i { $app_title }
title-uninstall = Çinstalim i { $app_title }
error-title = Gabim te { $program_name }

# Shared buttons
btn-cancel = Anuloje
btn-install-update = Instalo përditësimin
btn-install = Instaloje
btn-update = Përditësoje
btn-downgrade = Zhgradoje
btn-repair = Riparoje
btn-open-log = Hap regjistrin
btn-open-install-dir = Hap dosjen e instalimit

# Elevation (dialogs_common.rs)
elevate-header = Lypsen leje administratori
elevate-body = Që të instalohet versioni { $app_version }, { $app_title } i lyp leje administratori. Lejoni vazhdimin e këtij përditësimi?

# Restart required (prerequisite.rs)
restart-header = Lypset rinisje
restart-body = Para se rregullimi të mund të vazhdohet, kompjuteri juaj duhet rinisur. Ju lutemi, rinisni kompjuterin tuaj dhe xhironi sërish rregullimin.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Lypsen përbërës shtesë
missing-deps-body = { $app_title } lyp së pari të instalohen sa vijon: { $deps }. Doni të shkarkohen dhe instalohen tani?

# Uninstall with errors (uninstall)
uninstall-errors-header = Çinstalimi përfundoi me probleme
uninstall-errors-body = { $app_title } u çinstalua, por disa skedarë apo dosje s’u hoqën dot. Mund t’i fshini dorazi, ose riinstaloni aplikacionin dhe provoni sërish çinstalimin.
uninstall-errors-log = Hollësitë u ruajtën te: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } është tashmë i instaluar
overwrite-repair-body = Ky aplikacion është tashmë i instaluar në kompjuterin tuaj. Nëse nuk funksionon si duhet, mund të provoni ta riparoni duke e riinstaluar.
overwrite-older-installed = { $app_title } është tashmë i instaluar
overwrite-update-body = Aktualisht është i instaluar versioni { $old_version }. Doni të përditësohet në versionin { $app_version }?
overwrite-newer-installed = Një version më i ri i { $app_title } është tashmë i instaluar
overwrite-downgrade-body = Aktualisht është i instaluar versioni { $old_version }, i cili është më i ri se ky instalues. Zhgradimi nuk këshillohet dhe mund të shkaktojë probleme. Të vazhdohet sidoqoftë?
overwrite-footer = I instaluar te: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Çinstalimi u plotësua
uninstall-body = Aplikacioni u hoq me sukses prej kompjuterit tuaj.

# Install hook failed (install.rs)
install-hook-header = Instalimi pati pjesërisht sukses
install-hook-body = Instalimi u plotësua, por disa hapa mund të kenë dështuar. Nëse aplikacioni nuk funksionon si duhet, mund të provoni ta riinstaloni ose të lidheni me autorin e aplikacionit.

# Splash fallback (splash.rs)
splash-header = Po instalohet { $app_title }
splash-body = Po rregullohet { $app_title } { $app_version }, ju lutemi, pritni…

# Dependency download (prerequisite.rs)
deps-download-header = Po shkarkohet përbërësi i domosdoshëm
deps-download-body = Po shkarkohet { $dep_name }, ju lutemi, pritni…

# Apply progress (apply_*_impl.rs)
apply-header = Po instalohet përditësimi
apply-body = Po përditësohet në versionin { $app_version }, ju lutemi, pritni…

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalim i dëmtuar
start-corrupt-body = Ky aplikacion nuk niset dot, ngaqë disa nga skedarët e tij mungojnë ose janë të dëmtuar. Ju lutemi, riinstaloni aplikacionin për ta ndrequr këtë.

# Generic error
error-header = Diç shkoi ters

# Setup error (wix msi)
setup-error-header = Rregullimi s’u vazhdua dot

# MSI Installer UI - Common
msi-dlg-title = Rregullim i { $app_title }
msi-btn-back = &Mbrapsht
msi-btn-next = P&asuesi
msi-btn-cancel = Anuloje
msi-btn-finish = Për&fundoje
msi-btn-ok = OK
msi-btn-yes = &Po
msi-btn-no = &Jo
msi-btn-retry = &Riprovoni
msi-btn-ignore = &Shpërfille

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Mirë se vini te Ndihmësi i Rregullimit të { $app_title }
msi-welcome-description = Ndihmësi i Rregullimit do të instalojë { $app_title } në kompjuterin tuaj. Klikoni mbi Pasuesi që të vazhdohet, ose mbi Anuloje që të dilet nga Ndihmësi i Rregullimit.
msi-welcome-update-description = Ndihmësi i Rregullimit do të përditësojë { $app_title } në kompjuterin tuaj. Klikoni mbi Pasuesi që të vazhdohet, ose mbi Anuloje që të dilet nga Ndihmësi i Rregullimit.

# MSI Installer UI - Exit Dialog
msi-exit-title = U plotësua Ndihmësi i Rregullimit të { $app_title }
msi-exit-description = Klikoni mbi butonin Përfundoje që të dilet nga Ndihmësi i Rregullimit.
msi-exit-launch-checkbox = Nise { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Mirë se vini te Ndihmësi i Rregullimit të { $app_title }
msi-prepare-description = Ju lutemi, pritni teksa Ndihmësi i Rregullimit përgatitet t’ju udhëheqë përmes instalimit.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Marrëveshje Licence Përdoruesi të Fundmë
msi-license-description = Ju lutemi, lexojeni me kujdes marrëveshjen vijuese për licencën.
msi-license-checkbox = I &pranoj kushtet e Marrëveshjes së Licencës

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Shtrirje instalimi
msi-scope-description = Përzgjidhni shtrirjen e instalimit.
msi-scope-per-user = Instalojeni &vetëm për ju
msi-scope-per-machine = Instaloje për krejt përdoruesit e kësaj &makine
msi-scope-per-user-description = Instalohet vetëm për përdoruesin e tanishëm
msi-scope-no-per-user-description = Lypsen privilegje administratori
msi-scope-per-machine-description = Lypsen privilegje administratori

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Gati për të instaluar { $app_title }
msi-ready-install-text = Klikoni mbi Instaloje që të fillojë instalimi. Klikoni mbi Mbrapsht që të rishihni ose ndryshoni çfarëdo rregullimi tuajin për instalimin.
msi-ready-change-title = Gati për të ndryshuar { $app_title }
msi-ready-change-text = Klikoni mbi Ndryshoje që të fillojë ndryshimi i instalimit. Klikoni mbi Mbrapsht që të rishihni ose ndryshoni çfarëdo rregullimi tuajin për instalimin.
msi-ready-repair-title = Gati për të riparuar { $app_title }
msi-ready-repair-text = Klikoni mbi Riparoje që të fillojë riparimi. Klikoni mbi Mbrapsht që të rishihni ose ndryshoni çfarëdo rregullimi tuajin për instalimin.
msi-ready-remove-title = Gati për të hequr { $app_title }
msi-ready-remove-text = Klikoni mbi Hiqe që të hiqet { $app_title } prej kompjuterit tuaj. Klikoni mbi Mbrapsht që të rishihni ose ndryshoni çfarëdo rregullimi tuajin për instalimin.
msi-ready-update-title = Gati për të përditësuar { $app_title }
msi-ready-update-text = Klikoni mbi Përditësoje që të fillojë përditësimi. Klikoni mbi Mbrapsht që të rishihni ose ndryshoni çfarëdo rregullimi tuajin për instalimin.
msi-ready-btn-install = &Instaloje
msi-ready-btn-change = &Ndryshoje
msi-ready-btn-repair = Ri&paroje
msi-ready-btn-remove = &Hiqe
msi-ready-btn-update = &Përditësoje

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Po instalohet { $app_title }
msi-progress-installing-text = Ju lutemi, pritni teksa Ndihmësi i Rregullimit instalon { $app_title }.
msi-progress-changing-title = Po ndryshohet { $app_title }
msi-progress-changing-text = Ju lutemi, pritni teksa Ndihmësi i Rregullimit ndryshon { $app_title }.
msi-progress-repairing-title = Po riparohet { $app_title }
msi-progress-repairing-text = Ju lutemi, pritni teksa Ndihmësi i Rregullimit riparon { $app_title }.
msi-progress-removing-title = Po hiqet { $app_title }
msi-progress-removing-text = Ju lutemi, pritni teksa Ndihmësi i Rregullimit heq { $app_title }.
msi-progress-updating-title = Po përditësohet { $app_title }
msi-progress-updating-text = Ju lutemi, pritni teksa Ndihmësi i Rregullimit përditëson { $app_title }.
msi-progress-status = Gjendje:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Mirë se vini te Ndihmësi i Rregullimit të { $app_title }
msi-maint-welcome-description = Ndihmësi i Rregullimit do t’ju lejojë të riparoni ose hiqni { $app_title }. Klikoni mbi Pasuesi që të vazhdohet, ose mbi Anuloje që të dilet nga Ndihmësi i Rregullimit.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Ndryshoni, riparoni, ose hiqni një instalim
msi-maint-type-description = Përzgjidhni veprimin që dëshironi të kryhet.
msi-maint-change-button = &Ndryshoje...
msi-maint-change-tooltip = Ndryshoje...
msi-maint-change-text = Lejon përdoruesit të ndryshojnë cilat veçori programi instalohen dhe të ndryshojnë veçori individuale.
msi-maint-change-disabled = Ndryshimi është aktualisht i çaktivizuar.
msi-maint-repair-button = Ri&paroje
msi-maint-repair-tooltip = Riparoje
msi-maint-repair-text = Ndreq gabimet në instalimin më të freskët - rregullon skedarë, shkurtore dhe zëra regjistri që mungojnë ose janë të dëmtuar.
msi-maint-repair-disabled = Riparimi është aktualisht i çaktivizuar.
msi-maint-remove-button = &Hiqe
msi-maint-remove-tooltip = Hiqe
msi-maint-remove-text = E heq { $app_title } nga kompjuteri juaj.
msi-maint-remove-disabled = Heqja është aktualisht e çaktivizuar.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Jeni i sigurt se doni të anulohet instalimi i { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Ndryshoni dosjen e tanishme destinacion
msi-browse-description = Kaloni te dosja destinacion.
msi-browse-combo-label = &Shih te:
msi-browse-path-label = Emër &dosjeje:
msi-browse-up-tooltip = Një nivel më sipër
msi-browse-new-folder-tooltip = Krijo një dosje të re

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Drejtoria e specifikuar e destinacionit ose është e pavlefshme, ose është në një lloj disku të pambuluar.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Domosdoshmëri hapësire disku
msi-disk-cost-description = Hapësira e domosdoshme në disk për instalimin e veçorive të përzgjedhura.
msi-disk-cost-text = Vëllimet e theksuar nuk kanë në disk hapësirë të mjaftueshme për veçoritë e përzgjedhura në këtë çast. Ose mund të hiqni skedarë prej vëllimeve të theksuar, ose të zgjidhni të instaloni më pak veçori në disqe lokalë, ose të përzgjidhni disqe të tjerë destinacion.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Të dhëna instaluesi për { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Ndihmësi i Rregullimit të { $app_title } përfundoi para kohe
msi-fatal-description1 = Rregullimi i { $app_title } u ndërpre. Sistemi juaj nuk u modifikua. Që të instaloni këtë program në një kohë tjetër, ju lutemi, xhironi sërish rregullimin.
msi-fatal-description2 = Klikoni butonin Përfundoje që të dilni nga Ndihmësi i Rregullimit.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Puna e Ndihmësit të Rregullimit të { $app_title } u ndërpre
msi-user-exit-description1 = Rregullimi i { $app_title } u ndërpre. Sistemi juaj nuk u modifikua. Që të instaloni këtë program në një kohë tjetër, ju lutemi, xhironi sërish rregullimin.
msi-user-exit-description2 = Klikoni mbi butonin Përfundoje që të dilet nga Ndihmësi i Rregullimit.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Skedarë në përdorim
msi-files-in-use-description = Disa skedarë që lypset të përditësohen janë hëpërhë në përdorim e sipër.
msi-files-in-use-text = Aplikacionet vijuese po përdorin skedarë që lypset të përditësohen nga ky rregullim. Mbyllini këto aplikacione dhe mandej klikoni mbi Riprovoni që të vazhdohet instalimi ose mbi Anuloje që të dilet.
msi-files-in-use-exit = &Dilni

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Skedarë në përdorim
msi-rm-files-in-use-description = Disa skedarë që lypset të përditësohen janë hëpërhë në përdorim e sipër.
msi-rm-files-in-use-text = Aplikacionet vijuese po përdorin skedarë që lypset të përditësohen nga ky rregullim. Mund të lini Ndihmësin e Rregullimit t’i mbyllë automatikisht dhe të provojë t’i rinisë ato, ose t’i mbyllni vetë dorazi dhe të klikoni mbi OK që të vazhdohet instalimi.
msi-rm-files-in-use-use-rm = Mbylli automatikisht aplikacionet dhe &provo t’i rinisësh pas plotësimit të rregullimit.
msi-rm-files-in-use-dont-use-rm = M&os i mbyll aplikacionet. (Do të kërkohet një rinisje.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Po rimerret Ndihmësi i Rregullimit të { $app_title }
msi-resume-description = Ndihmësi i Rregullimit do të plotësojë instalimin e { $app_title } në kompjuterin tuaj. Klikoni mbi Instaloje që të vazhdohet ose mbi Anuloje që të dilet nga Ndihmësi i Rregullimit.
msi-resume-btn-install = &Instaloje

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Shkurtore për { $app_title } në desktop
msi-start-menu-shortcut-description = Shkurtore për { $app_title } në menynë Fillim
