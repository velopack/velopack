# Shared titles
title-update = Actualizare { $app_title }
title-setup = Instalare { $app_title }
title-uninstall = Dezinstalare { $app_title }
error-title = Eroare { $program_name }

# Shared buttons
btn-cancel = Revocare
btn-install-update = Instalează actualizarea
btn-install = Instalare
btn-update = Actualizare
btn-downgrade = Retrogradare
btn-repair = Reparare
btn-open-log = Deschidere jurnal
btn-open-install-dir = Deschide directorul de instalare

# Elevation (dialogs_common.rs)
elevate-header = Sunt necesare permisiuni de administrator
elevate-body = { $app_title } necesită permisiuni de administrator pentru a instala versiunea { $app_version }. Permiteți continuarea acestei actualizări?

# Restart required (prerequisite.rs)
restart-header = Repornire necesară
restart-body = Computerul trebuie repornit înainte ca instalarea să poată continua. Reporniți computerul și executați din nou instalarea.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Sunt necesare componente suplimentare
missing-deps-body = { $app_title } necesită ca următoarele să fie instalate mai întâi: { $deps }. Doriți să le descărcați și să le instalați acum?

# Uninstall with errors (uninstall)
uninstall-errors-header = Dezinstalarea s-a încheiat cu probleme
uninstall-errors-body = { $app_title } a fost dezinstalat, dar unele fișiere sau foldere nu au putut fi eliminate. Le puteți șterge manual sau puteți reinstala aplicația și încerca din nou dezinstalarea.
uninstall-errors-log = Detaliile au fost salvate la: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } este deja instalat
overwrite-repair-body = Această aplicație este deja instalată pe computer. Dacă nu funcționează corect, puteți încerca să o reparați prin reinstalare.
overwrite-older-installed = { $app_title } este deja instalat
overwrite-update-body = Versiunea { $old_version } este instalată în prezent. Doriți să actualizați la versiunea { $app_version }?
overwrite-newer-installed = O versiune mai nouă a { $app_title } este deja instalată
overwrite-downgrade-body = Versiunea { $old_version } este instalată în prezent, care este mai nouă decât acest program de instalare. Retrogradarea nu este recomandată și poate cauza probleme. Continuați oricum?
overwrite-footer = Instalat la: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Dezinstalare finalizată
uninstall-body = Aplicația a fost eliminată cu succes de pe computer.

# Install hook failed (install.rs)
install-hook-header = Instalarea a reușit parțial
install-hook-body = Instalarea s-a încheiat, dar este posibil ca unele etape să fi eșuat. Dacă aplicația nu funcționează corect, puteți încerca să o reinstalați sau să contactați autorul aplicației.

# Splash fallback (splash.rs)
splash-header = Se instalează { $app_title }
splash-body = Se configurează { $app_title } { $app_version }, vă rugăm așteptați...

# Dependency download (prerequisite.rs)
deps-download-header = Se descarcă componenta necesară
deps-download-body = Se descarcă { $dep_name }, vă rugăm așteptați...

# Apply progress (apply_*_impl.rs)
apply-header = Se instalează actualizarea
apply-body = Se actualizează la versiunea { $app_version }, vă rugăm așteptați...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalare deteriorată
start-corrupt-body = Această aplicație nu poate porni deoarece unele dintre fișierele sale lipsesc sau sunt deteriorate. Reinstalați aplicația pentru a remedia această problemă.

# Generic error
error-header = Ceva nu a mers bine

# Setup error (wix msi)
setup-error-header = Instalarea nu poate continua

# MSI Installer UI - Common
msi-dlg-title = Programul de instalare { $app_title }
msi-btn-back = Î&napoi
msi-btn-next = &Următorul
msi-btn-cancel = Revocare
msi-btn-finish = &Terminare
msi-btn-ok = OK
msi-btn-yes = &Da
msi-btn-no = &Nu
msi-btn-retry = &Reîncercare
msi-btn-ignore = &Ignorare

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Bun venit la Expertul de instalare { $app_title }
msi-welcome-description = Expertul de instalare va instala { $app_title } pe computer. Faceți clic pe Următorul pentru a continua sau pe Revocare pentru a ieși din Expertul de instalare.
msi-welcome-update-description = Expertul de instalare va actualiza { $app_title } pe computer. Faceți clic pe Următorul pentru a continua sau pe Revocare pentru a ieși din Expertul de instalare.

# MSI Installer UI - Exit Dialog
msi-exit-title = Expertul de instalare { $app_title } s-a încheiat
msi-exit-description = Faceți clic pe Terminare pentru a ieși din Expertul de instalare.
msi-exit-launch-checkbox = Lansare { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Bun venit la Expertul de instalare { $app_title }
msi-prepare-description = Așteptați. Expertul de instalare se pregătește să vă ghideze pe parcursul instalării.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Acord de licență pentru utilizatorul final
msi-license-description = Citiți cu atenție următorul acord de licență.
msi-license-checkbox = &Accept termenii din Acordul de licență

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Aria de instalare
msi-scope-description = Selectați aria de instalare.
msi-scope-per-user = Instalare &numai pentru dvs.
msi-scope-per-machine = Instalare pentru &toți utilizatorii
msi-scope-per-user-description = Instalează numai pentru utilizatorul curent
msi-scope-no-per-user-description = Necesită privilegii de administrator
msi-scope-per-machine-description = Necesită privilegii de administrator

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Pregătit pentru instalarea { $app_title }
msi-ready-install-text = Faceți clic pe Instalare pentru a începe instalarea. Faceți clic pe Înapoi pentru a examina sau a modifica oricare dintre setările de instalare.
msi-ready-change-title = Pregătit pentru modificarea { $app_title }
msi-ready-change-text = Faceți clic pe Modificare pentru a începe modificarea instalării. Faceți clic pe Înapoi pentru a examina sau a modifica oricare dintre setările de instalare.
msi-ready-repair-title = Pregătit pentru repararea { $app_title }
msi-ready-repair-text = Faceți clic pe Reparare pentru a începe repararea. Faceți clic pe Înapoi pentru a examina sau a modifica oricare dintre setările de instalare.
msi-ready-remove-title = Pregătit pentru eliminarea { $app_title }
msi-ready-remove-text = Faceți clic pe Eliminare pentru a elimina { $app_title } de pe computer. Faceți clic pe Înapoi pentru a examina sau a modifica oricare dintre setările de instalare.
msi-ready-update-title = Pregătit pentru actualizarea { $app_title }
msi-ready-update-text = Faceți clic pe Actualizare pentru a începe actualizarea. Faceți clic pe Înapoi pentru a examina sau a modifica oricare dintre setările de instalare.
msi-ready-btn-install = &Instalare
msi-ready-btn-change = &Modificare
msi-ready-btn-repair = Re&parare
msi-ready-btn-remove = &Eliminare
msi-ready-btn-update = Act&ualizare

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Se instalează { $app_title }
msi-progress-installing-text = Așteptați, Expertul de instalare instalează { $app_title }.
msi-progress-changing-title = Se modifică { $app_title }
msi-progress-changing-text = Așteptați, Expertul de instalare modifică { $app_title }.
msi-progress-repairing-title = Se repară { $app_title }
msi-progress-repairing-text = Așteptați, Expertul de instalare repară { $app_title }.
msi-progress-removing-title = Se elimină { $app_title }
msi-progress-removing-text = Așteptați, Expertul de instalare elimină { $app_title }.
msi-progress-updating-title = Se actualizează { $app_title }
msi-progress-updating-text = Așteptați, Expertul de instalare actualizează { $app_title }.
msi-progress-status = Stare:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Bun venit la Expertul de instalare { $app_title }
msi-maint-welcome-description = Expertul de instalare vă permite să reparați sau să eliminați { $app_title }. Faceți clic pe Următorul pentru a continua sau pe Revocare pentru a ieși din Expertul de instalare.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Modificați, reparați sau eliminați instalarea
msi-maint-type-description = Selectați operația pe care doriți să o efectuați.
msi-maint-change-button = &Modificare...
msi-maint-change-tooltip = Modificare...
msi-maint-change-text = Permite utilizatorilor să modifice caracteristicile de program instalate și să modifice caracteristici individuale.
msi-maint-change-disabled = Modificarea este momentan dezactivată.
msi-maint-repair-button = Re&parare
msi-maint-repair-tooltip = Reparare
msi-maint-repair-text = Repară erorile celei mai recente instalări, prin remedierea fișierelor, comenzilor rapide și intrărilor de registry care lipsesc sau sunt deteriorate.
msi-maint-repair-disabled = Repararea este momentan dezactivată.
msi-maint-remove-button = &Eliminare
msi-maint-remove-tooltip = Eliminare
msi-maint-remove-text = Elimină { $app_title } de pe computer.
msi-maint-remove-disabled = Eliminarea este momentan dezactivată.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Sigur revocați instalarea { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Modificați folderul curent de destinație
msi-browse-description = Răsfoiți la folderul destinație.
msi-browse-combo-label = &Privire în:
msi-browse-path-label = &Nume folder:
msi-browse-up-tooltip = Mai sus cu un nivel
msi-browse-new-folder-tooltip = Creare folder nou

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Directorul destinație specificat este nevalid sau se află pe un tip de unitate neacceptat.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Cerințe de spațiu-disc
msi-disk-cost-description = Spațiul-disc necesar instalării caracteristicilor selectate.
msi-disk-cost-text = Volumele evidențiate nu au suficient spațiu-disc disponibil pentru caracteristicile selectate momentan. Aveți posibilitatea să eliminați unele fișiere din volumele evidențiate, să instalați mai puține caracteristici pe unitatea/unitățile locale sau să selectați altă unitate de destinație.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Informații despre program de instalare

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Expertul de instalare { $app_title } s-a încheiat prematur
msi-fatal-description1 = Instalarea { $app_title } a fost întreruptă. Sistemul nu a fost modificat. Pentru a instala ulterior acest program, executați din nou instalarea.
msi-fatal-description2 = Faceți clic pe Terminare pentru a ieși din Expertul de instalare.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Expertul de instalare { $app_title } a fost întrerupt
msi-user-exit-description1 = Instalarea { $app_title } a fost întreruptă. Sistemul nu a fost modificat. Pentru a instala ulterior acest program, executați din nou instalarea.
msi-user-exit-description2 = Faceți clic pe Terminare pentru a ieși din Expertul de instalare.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Fișiere în uz
msi-files-in-use-description = Unele fișiere care trebuie actualizate sunt momentan în uz.
msi-files-in-use-text = Aplicațiile următoare utilizează fișiere care trebuie actualizate de această instalare. Închideți aceste aplicații și faceți clic pe Reîncercare pentru a continua instalarea sau pe Revocare pentru a ieși.
msi-files-in-use-exit = I&eșire

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Fișiere în uz
msi-rm-files-in-use-description = Unele fișiere care trebuie actualizate sunt momentan în uz.
msi-rm-files-in-use-text = Aplicațiile următoare utilizează fișiere care trebuie actualizate de această instalare. Puteți lăsa Expertul de instalare să le închidă automat și să încerce repornirea după finalizarea instalării sau le puteți închide manual și faceți clic pe OK pentru a continua instalarea.
msi-rm-files-in-use-use-rm = &Se închid automat aplicațiile și se încearcă repornirea după ce instalarea s-a încheiat.
msi-rm-files-in-use-dont-use-rm = &Nu se închid aplicațiile. (Va fi necesară o repornire.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Se reia Expertul de instalare { $app_title }
msi-resume-description = Expertul de instalare va finaliza instalarea { $app_title } pe computer. Faceți clic pe Instalare pentru a continua sau pe Revocare pentru a ieși din Expertul de instalare.
msi-resume-btn-install = &Instalare

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Comandă rapidă pe desktop pentru { $app_title }
msi-start-menu-shortcut-description = Comandă rapidă în meniul Start pentru { $app_title }
