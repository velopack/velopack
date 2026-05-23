# Shared titles
title-update = { $app_title } atjaunināšana
title-setup = { $app_title } uzstādīšana
title-uninstall = { $app_title } atinstalēšana
error-title = { $program_name } kļūda

# Shared buttons
btn-cancel = Atcelt
btn-install-update = Instalēt atjauninājumu
btn-install = Instalēt
btn-update = Atjaunināt
btn-downgrade = Pazemināt versiju
btn-repair = Labot
btn-open-log = Atvērt žurnālu
btn-open-install-dir = Atvērt instalācijas mapi

# Elevation (dialogs_common.rs)
elevate-header = Nepieciešamas administratora atļaujas
elevate-body = Lietojumprogrammai { $app_title } ir nepieciešamas administratora atļaujas, lai instalētu versiju { $app_version }. Vai atļaut šī atjauninājuma turpināšanu?

# Restart required (prerequisite.rs)
restart-header = Nepieciešama restartēšana
restart-body = Datoram ir jāveic restartēšana, pirms uzstādīšana var turpināties. Lūdzu, restartējiet datoru un palaidiet uzstādīšanu vēlreiz.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Nepieciešami papildu komponenti
missing-deps-body = Lietojumprogrammai { $app_title } vispirms ir jāinstalē šie komponenti: { $deps }. Vai vēlaties tos lejupielādēt un instalēt tagad?

# Uninstall with errors (uninstall)
uninstall-errors-header = Atinstalēšana pabeigta ar problēmām
uninstall-errors-body = { $app_title } tika atinstalēts, taču dažus failus vai mapes nevarēja noņemt. Tos var izdzēst manuāli vai pārinstalēt lietojumprogrammu un mēģināt atinstalēt vēlreiz.
uninstall-errors-log = Detalizēta informācija tika saglabāta šeit: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } jau ir instalēts
overwrite-repair-body = Šī lietojumprogramma jau ir instalēta datorā. Ja tā nedarbojas pareizi, varat mēģināt to labot, atkārtoti instalējot.
overwrite-older-installed = { $app_title } jau ir instalēts
overwrite-update-body = Pašlaik ir instalēta versija { $old_version }. Vai vēlaties atjaunināt uz versiju { $app_version }?
overwrite-newer-installed = Jaunāka { $app_title } versija jau ir instalēta
overwrite-downgrade-body = Pašlaik ir instalēta versija { $old_version }, kas ir jaunāka par šo instalētāju. Versijas pazemināšana nav ieteicama un var radīt problēmas. Vai tomēr turpināt?
overwrite-footer = Instalēts vietā: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Atinstalēšana pabeigta
uninstall-body = Lietojumprogramma ir veiksmīgi noņemta no datora.

# Install hook failed (install.rs)
install-hook-header = Instalēšana daļēji veiksmīga
install-hook-body = Instalēšana ir pabeigta, taču dažas darbības, iespējams, neizdevās. Ja lietojumprogramma nedarbojas pareizi, varat mēģināt to atkārtoti instalēt vai sazināties ar lietojumprogrammas izstrādātāju.

# Splash fallback (splash.rs)
splash-header = Notiek { $app_title } instalēšana
splash-body = Notiek { $app_title } { $app_version } iestatīšana, lūdzu, uzgaidiet...

# Dependency download (prerequisite.rs)
deps-download-header = Notiek nepieciešamā komponenta lejupielāde
deps-download-body = Notiek { $dep_name } lejupielāde, lūdzu, uzgaidiet...

# Apply progress (apply_*_impl.rs)
apply-header = Notiek atjauninājuma instalēšana
apply-body = Atjaunināšana uz versiju { $app_version }, lūdzu, uzgaidiet...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalācija bojāta
start-corrupt-body = Šo lietojumprogrammu nevar palaist, jo daži no tās failiem trūkst vai ir bojāti. Lūdzu, pārinstalējiet lietojumprogrammu, lai novērstu šo problēmu.

# Generic error
error-header = Kaut kas nogāja greizi

# Setup error (wix msi)
setup-error-header = Uzstādīšanu nevarēja turpināt

# MSI Installer UI - Common
msi-dlg-title = { $app_title } uzstādīšana
msi-btn-back = &Atpakaļ
msi-btn-next = &Tālāk
msi-btn-cancel = Atcelt
msi-btn-finish = &Pabeigt
msi-btn-ok = Labi
msi-btn-yes = &Jā
msi-btn-no = &Nē
msi-btn-retry = &Mēģināt vēlreiz
msi-btn-ignore = &Ignorēt

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Esiet sveicināts { $app_title } uzstādīšanas vednī!
msi-welcome-description = Izmantojot uzstādīšanas vedni, datorā tiks instalēts { $app_title }. Noklikšķiniet uz Tālāk, lai turpinātu, vai uz Atcelt, lai izietu no uzstādīšanas vedņa.
msi-welcome-update-description = Izmantojot uzstādīšanas vedni, datorā tiks atjaunināts { $app_title }. Noklikšķiniet uz Tālāk, lai turpinātu, vai uz Atcelt, lai izietu no uzstādīšanas vedņa.

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } uzstādīšanas vednis ir pabeigts
msi-exit-description = Noklikšķiniet uz pogas Pabeigt, lai izietu no Uzstādīšanas vedņa.
msi-exit-launch-checkbox = Palaist { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Esiet sveicināts { $app_title } uzstādīšanas vednī!
msi-prepare-description = Uzgaidiet, līdz Uzstādīšanas vednī tiek sagatavoti instalēšanas norādījumi.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Lietotāja licences līgums
msi-license-description = Lūdzu, uzmanīgi izlasiet šo licences līgumu.
msi-license-checkbox = Es &piekrītu licences līguma nosacījumiem

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Instalēšanas tvērums
msi-scope-description = Izvēlieties instalēšanas tvērumu.
msi-scope-per-user = Instalēt tikai &sev
msi-scope-per-machine = Instalēt visiem &lietotājiem
msi-scope-per-user-description = Instalē tikai pašreizējam lietotājam
msi-scope-no-per-user-description = Nepieciešamas administratora atļaujas
msi-scope-per-machine-description = Nepieciešamas administratora atļaujas

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Gatavs { $app_title } instalēšanai
msi-ready-install-text = Noklikšķiniet uz Instalēt, lai sāktu instalēšanu. Noklikšķiniet uz Atpakaļ, lai pārskatītu vai mainītu jebkuru instalēšanas iestatījumu.
msi-ready-change-title = Gatavs { $app_title } mainīšanai
msi-ready-change-text = Noklikšķiniet uz Mainīt, lai sāktu mainīt instalāciju. Noklikšķiniet uz Atpakaļ, lai pārskatītu vai mainītu jebkuru instalēšanas iestatījumu.
msi-ready-repair-title = Gatavs { $app_title } labošanai
msi-ready-repair-text = Noklikšķiniet uz Labot, lai sāktu labošanu. Noklikšķiniet uz Atpakaļ, lai pārskatītu vai mainītu jebkuru instalēšanas iestatījumu.
msi-ready-remove-title = Gatavs { $app_title } noņemšanai
msi-ready-remove-text = Noklikšķiniet uz Noņemt, lai noņemtu { $app_title } no datora. Noklikšķiniet uz Atpakaļ, lai pārskatītu vai mainītu jebkuru instalēšanas iestatījumu.
msi-ready-update-title = Gatavs { $app_title } atjaunināšanai
msi-ready-update-text = Noklikšķiniet uz Atjaunināt, lai sāktu atjaunināšanu. Noklikšķiniet uz Atpakaļ, lai pārskatītu vai mainītu jebkuru instalēšanas iestatījumu.
msi-ready-btn-install = &Instalēt
msi-ready-btn-change = &Mainīt
msi-ready-btn-repair = La&bot
msi-ready-btn-remove = &Noņemt
msi-ready-btn-update = Atja&unināt

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Notiek { $app_title } instalēšana
msi-progress-installing-text = Lūdzu, uzgaidiet, kamēr uzstādīšanas vednis instalē { $app_title }.
msi-progress-changing-title = Notiek { $app_title } maiņa
msi-progress-changing-text = Lūdzu, uzgaidiet, kamēr uzstādīšanas vednis maina { $app_title }.
msi-progress-repairing-title = Notiek { $app_title } labošana
msi-progress-repairing-text = Lūdzu, uzgaidiet, kamēr uzstādīšanas vednis labo { $app_title }.
msi-progress-removing-title = Notiek { $app_title } noņemšana
msi-progress-removing-text = Lūdzu, uzgaidiet, kamēr uzstādīšanas vednis noņem { $app_title }.
msi-progress-updating-title = Notiek { $app_title } atjaunināšana
msi-progress-updating-text = Lūdzu, uzgaidiet, kamēr uzstādīšanas vednis atjaunina { $app_title }.
msi-progress-status = Statuss:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Esiet sveicināts { $app_title } uzstādīšanas vednī!
msi-maint-welcome-description = Uzstādīšanas vednis ļaus jums labot vai noņemt { $app_title }. Noklikšķiniet uz Tālāk, lai turpinātu, vai uz Atcelt, lai izietu no uzstādīšanas vedņa.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Instalācijas maiņa, labošana vai noņemšana
msi-maint-type-description = Atlasiet veicamo darbību.
msi-maint-change-button = &Mainīt
msi-maint-change-tooltip = Mainīt instalāciju
msi-maint-change-text = Ļauj lietotājiem mainīt, kuri programmas līdzekļi ir instalēti, un mainīt atsevišķus līdzekļus.
msi-maint-change-disabled = Mainīšana pašlaik ir atspējota.
msi-maint-repair-button = La&bot
msi-maint-repair-tooltip = Labot instalāciju
msi-maint-repair-text = Jaunākajā instalācijā labo kļūdas, labojot trūkstošos un bojātos failus, saīsnes un reģistra ierakstus.
msi-maint-repair-disabled = Labošana pašlaik ir atspējota.
msi-maint-remove-button = &Noņemt
msi-maint-remove-tooltip = Noņemt instalāciju
msi-maint-remove-text = Noņem { $app_title } no datora.
msi-maint-remove-disabled = Noņemšana pašlaik ir atspējota.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Vai tiešām vēlaties atcelt { $app_title } instalēšanu?

# MSI Installer UI - Browse Dialog
msi-browse-title = Pašreizējās mērķa mapes maiņa
msi-browse-description = Meklēt mērķa mapi.
msi-browse-combo-label = &Skatīt šeit:
msi-browse-path-label = &Mapes nosaukums:
msi-browse-up-tooltip = Vienu līmeni augstāk
msi-browse-new-folder-tooltip = Izveidot jaunu mapi

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Norādītais mērķa direktorijs ir nederīgs vai atrodas neatbalstītā diskdziņa tipā.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Nepieciešamā vieta diskā
msi-disk-cost-description = Atlasīto līdzekļu instalēšanai nepieciešamā vieta diskā.
msi-disk-cost-text = Iezīmētajiem sējumiem nepietiek vietas diskā, lai instalētu atlasītos līdzekļus. Varat noņemt dažus failus no iezīmētajiem sējumiem, instalēt mazāk līdzekļu vietējos diskos vai atlasīt citus mērķa diskdziņus.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } instalētāja informācija

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } uzstādīšanas vednis darbību beidza priekšlaikus
msi-fatal-description1 = { $app_title } uzstādīšana tika pārtraukta. Jūsu sistēma nav modificēta. Lai instalētu šo programmu vēlāk, lūdzu, palaidiet uzstādīšanu vēlreiz.
msi-fatal-description2 = Noklikšķiniet uz pogas Pabeigt, lai izietu no Uzstādīšanas vedņa.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } uzstādīšanas vedņa darbība tika pārtraukta
msi-user-exit-description1 = { $app_title } uzstādīšana tika pārtraukta. Jūsu sistēma nav modificēta. Lai instalētu šo programmu vēlāk, lūdzu, palaidiet uzstādīšanu vēlreiz.
msi-user-exit-description2 = Noklikšķiniet uz pogas Pabeigt, lai izietu no Uzstādīšanas vedņa.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Faili, kas tiek lietoti
msi-files-in-use-description = Daži no failiem, kas jāatjaunina, pašlaik tiek lietoti.
msi-files-in-use-text = Tālāk norādītās lietojumprogrammas izmanto failus, kas ir jāatjaunina šajā uzstādīšanā. Aizveriet šīs lietojumprogrammas un pēc tam noklikšķiniet uz Mēģināt vēlreiz, lai turpinātu instalēšanu, vai uz Atcelt, lai izietu no tās.
msi-files-in-use-exit = I&ziet

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Faili, kas tiek lietoti
msi-rm-files-in-use-description = Daži no failiem, kas jāatjaunina, pašlaik tiek lietoti.
msi-rm-files-in-use-text = Tālāk norādītās lietojumprogrammas izmanto failus, kas ir jāatjaunina šajā uzstādīšanā. Varat ļaut uzstādīšanas vednim automātiski aizvērt un mēģināt restartēt tās, vai arī varat aizvērt tās manuāli un noklikšķināt uz Labi, lai turpinātu instalēšanu.
msi-rm-files-in-use-use-rm = &Automātiski aizvērt lietojumprogrammas un mēģināt tās restartēt pēc uzstādīšanas pabeigšanas.
msi-rm-files-in-use-dont-use-rm = &Neaizveriet lietojumprogrammas. (Būs jāveic atkārtota sāknēšana.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Notiek { $app_title } uzstādīšanas vedņa atsākšana
msi-resume-description = Izmantojot uzstādīšanas vedni, datorā tiks pabeigta { $app_title } instalēšana. Noklikšķiniet uz Instalēt, lai turpinātu, vai uz Atcelt, lai izietu no uzstādīšanas vedņa.
msi-resume-btn-install = &Instalēt

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } darbvirsmas saīsne
msi-start-menu-shortcut-description = { $app_title } izvēlnes Sākt saīsne
