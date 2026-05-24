# Shared titles
title-update = Aggiornamento di { $app_title }
title-setup = Installazione di { $app_title }
title-uninstall = Disinstallazione di { $app_title }
error-title = Errore di { $program_name }

# Shared buttons
btn-cancel = Annulla
btn-install-update = Installa aggiornamento
btn-install = Installa
btn-update = Aggiorna
btn-downgrade = Esegui downgrade
btn-repair = Ripara
btn-open-log = Apri registro
btn-open-install-dir = Apri cartella di installazione

# Elevation (dialogs_common.rs)
elevate-header = Autorizzazione amministratore richiesta
elevate-body = { $app_title } richiede l'autorizzazione di amministratore per installare la versione { $app_version }. Consentire la continuazione dell'aggiornamento?

# Restart required (prerequisite.rs)
restart-header = Riavvio richiesto
restart-body = È necessario riavviare il computer prima che l'installazione possa continuare. Riavviare il computer ed eseguire di nuovo l'installazione.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Componenti aggiuntivi richiesti
missing-deps-body = { $app_title } richiede l'installazione preliminare dei seguenti elementi: { $deps }. Si desidera scaricarli e installarli ora?

# Uninstall with errors (uninstall)
uninstall-errors-header = Disinstallazione completata con problemi
uninstall-errors-body = { $app_title } è stato disinstallato, ma non è stato possibile rimuovere alcuni file o cartelle. È possibile eliminarli manualmente oppure reinstallare l'applicazione e tentare di nuovo la disinstallazione.
uninstall-errors-log = I dettagli sono stati salvati in: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } è già installato
overwrite-repair-body = Questa applicazione è già installata nel computer. Se non funziona correttamente, è possibile tentare di ripararla reinstallandola.
overwrite-older-installed = { $app_title } è già installato
overwrite-update-body = La versione { $old_version } è attualmente installata. Si desidera aggiornare alla versione { $app_version }?
overwrite-newer-installed = Una versione più recente di { $app_title } è già installata
overwrite-downgrade-body = La versione { $old_version } è attualmente installata ed è più recente di questo programma di installazione. Il downgrade non è consigliato e potrebbe causare problemi. Continuare comunque?
overwrite-footer = Installato in: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Disinstallazione completata
uninstall-body = L'applicazione è stata rimossa correttamente dal computer.

# Install hook failed (install.rs)
install-hook-header = Installazione parzialmente riuscita
install-hook-body = L'installazione è stata completata, ma alcuni passaggi potrebbero non essere riusciti. Se l'applicazione non funziona correttamente, è possibile reinstallarla o contattare l'autore dell'applicazione.

# Splash fallback (splash.rs)
splash-header = Installazione di { $app_title } in corso
splash-body = Configurazione di { $app_title } { $app_version } in corso, attendere...

# Dependency download (prerequisite.rs)
deps-download-header = Download del componente richiesto in corso
deps-download-body = Download di { $dep_name } in corso, attendere...

# Apply progress (apply_*_impl.rs)
apply-header = Installazione dell'aggiornamento in corso
apply-body = Aggiornamento alla versione { $app_version }, attendere...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installazione danneggiata
start-corrupt-body = Impossibile avviare l'applicazione perché alcuni dei suoi file sono mancanti o danneggiati. Reinstallare l'applicazione per risolvere il problema.

# Generic error
error-header = Si è verificato un errore

# Setup error (wix msi)
setup-error-header = Impossibile continuare con l'installazione

# MSI Installer UI - Common
msi-dlg-title = Installazione di { $app_title }
msi-btn-back = In&dietro
msi-btn-next = &Avanti
msi-btn-cancel = Annulla
msi-btn-finish = &Fine
msi-btn-ok = OK
msi-btn-yes = &Sì
msi-btn-no = &No
msi-btn-retry = &Riprova
msi-btn-ignore = &Ignora

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Installazione guidata di { $app_title }
msi-welcome-description = L'Installazione guidata installerà { $app_title } nel computer. Fare clic su Avanti per continuare oppure su Annulla per uscire dall'Installazione guidata.
msi-welcome-update-description = L'Installazione guidata aggiornerà { $app_title } nel computer. Fare clic su Avanti per continuare oppure su Annulla per uscire dall'Installazione guidata.

# MSI Installer UI - Exit Dialog
msi-exit-title = Installazione guidata di { $app_title } completata
msi-exit-description = Fare clic sul pulsante Fine per uscire dall'Installazione guidata.
msi-exit-launch-checkbox = Avvia { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Installazione guidata di { $app_title }
msi-prepare-description = Attendere. È in corso la preparazione dell'Installazione guidata.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Contratto di Licenza con l'utente finale
msi-license-description = Leggere attentamente il Contratto di Licenza.
msi-license-checkbox = &Accetto i termini del Contratto di Licenza

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Ambito di installazione
msi-scope-description = Selezionare l'ambito di installazione.
msi-scope-per-user = Installa solo per l'&utente corrente
msi-scope-per-machine = Installa per &tutti gli utenti
msi-scope-per-user-description = Installa solo per l'utente corrente
msi-scope-no-per-user-description = Richiede privilegi di amministratore
msi-scope-per-machine-description = Richiede privilegi di amministratore

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Pronto a installare { $app_title }
msi-ready-install-text = Fare clic su Installa per avviare l'installazione. Fare clic su Indietro per rivedere o modificare le impostazioni di installazione.
msi-ready-change-title = Pronto a modificare { $app_title }
msi-ready-change-text = Fare clic su Cambia per avviare la modifica dell'installazione. Fare clic su Indietro per rivedere o modificare le impostazioni di installazione.
msi-ready-repair-title = Pronto a riparare { $app_title }
msi-ready-repair-text = Fare clic su Ripara per avviare la riparazione. Fare clic su Indietro per rivedere o modificare le impostazioni di installazione.
msi-ready-remove-title = Pronto a rimuovere { $app_title }
msi-ready-remove-text = Fare clic su Rimuovi per rimuovere { $app_title } dal computer. Fare clic su Indietro per rivedere o modificare le impostazioni di installazione.
msi-ready-update-title = Pronto ad aggiornare { $app_title }
msi-ready-update-text = Fare clic su Aggiorna per avviare l'aggiornamento. Fare clic su Indietro per rivedere o modificare le impostazioni di installazione.
msi-ready-btn-install = &Installa
msi-ready-btn-change = &Cambia
msi-ready-btn-repair = Ri&para
msi-ready-btn-remove = &Rimuovi
msi-ready-btn-update = A&ggiorna

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Installazione di { $app_title } in corso
msi-progress-installing-text = Attendere. È in corso l'installazione di { $app_title }.
msi-progress-changing-title = Modifica di { $app_title } in corso
msi-progress-changing-text = Attendere. È in corso la modifica di { $app_title }.
msi-progress-repairing-title = Riparazione di { $app_title } in corso
msi-progress-repairing-text = Attendere. È in corso la riparazione di { $app_title }.
msi-progress-removing-title = Rimozione di { $app_title } in corso
msi-progress-removing-text = Attendere. È in corso la rimozione di { $app_title }.
msi-progress-updating-title = Aggiornamento di { $app_title } in corso
msi-progress-updating-text = Attendere. È in corso l'aggiornamento di { $app_title }.
msi-progress-status = Stato:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Installazione guidata di { $app_title }
msi-maint-welcome-description = L'Installazione guidata consente di riparare o rimuovere { $app_title }. Fare clic su Avanti per continuare oppure su Annulla per uscire dall'Installazione guidata.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Modifica, riparazione o rimozione installazione
msi-maint-type-description = Selezionare l'operazione che si desidera eseguire.
msi-maint-change-button = &Cambia...
msi-maint-change-tooltip = Cambia...
msi-maint-change-text = Consente agli utenti di modificare le funzionalità del programma installate e di modificare singole funzionalità.
msi-maint-change-disabled = Cambia è attualmente disabilitato.
msi-maint-repair-button = Ri&para
msi-maint-repair-tooltip = Ripara
msi-maint-repair-text = Corregge gli errori presenti nell'installazione più recente ripristinando file mancanti o danneggiati, collegamenti e voci del Registro di sistema.
msi-maint-repair-disabled = Ripara è attualmente disabilitato.
msi-maint-remove-button = &Rimuovi
msi-maint-remove-tooltip = Rimuovi
msi-maint-remove-text = Rimuove { $app_title } dal computer.
msi-maint-remove-disabled = Rimuovi è attualmente disabilitato.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Annullare l'installazione di { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Modifica cartella di destinazione corrente
msi-browse-description = Scegliere la cartella di destinazione.
msi-browse-combo-label = &Cerca in:
msi-browse-path-label = &Nome cartella:
msi-browse-up-tooltip = Livello superiore
msi-browse-new-folder-tooltip = Crea una nuova cartella

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = La directory di destinazione specificata non è valida o si trova su un tipo di unità non supportata.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Requisiti di spazio su disco
msi-disk-cost-description = Spazio su disco necessario per l'installazione delle funzionalità selezionate.
msi-disk-cost-text = Lo spazio su disco disponibile nei volumi evidenziati non è sufficiente per installare le funzionalità attualmente selezionate. È possibile rimuovere alcuni file dai volumi evidenziati, scegliere di installare un numero minore di funzionalità nelle unità locali, oppure selezionare unità di destinazione diverse.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informazioni sull'installazione di { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Installazione guidata di { $app_title } terminata prima del completamento
msi-fatal-description1 = L'installazione di { $app_title } è stata interrotta. Il sistema non è stato modificato. Per installare il programma in un secondo momento, eseguire di nuovo l'installazione.
msi-fatal-description2 = Fare clic sul pulsante Fine per uscire dall'Installazione guidata.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Installazione guidata di { $app_title } interrotta
msi-user-exit-description1 = L'installazione di { $app_title } è stata interrotta. Il sistema non è stato modificato. Per installare il programma in un secondo momento, eseguire di nuovo l'installazione.
msi-user-exit-description2 = Fare clic sul pulsante Fine per uscire dall'Installazione guidata.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = File in uso
msi-files-in-use-description = Alcuni file che richiedono l'aggiornamento sono attualmente in uso.
msi-files-in-use-text = Le applicazioni seguenti stanno utilizzando file che devono essere aggiornati da questa installazione. Chiudere le applicazioni, quindi fare clic su Riprova per continuare l'installazione. Per uscire dall'installazione, fare clic su Annulla.
msi-files-in-use-exit = E&sci

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = File in uso
msi-rm-files-in-use-description = Alcuni file che richiedono l'aggiornamento sono attualmente in uso.
msi-rm-files-in-use-text = Le applicazioni seguenti utilizzano file che devono essere aggiornati da questa installazione. È possibile consentire all'Installazione guidata di chiudere e tentare di riavviare automaticamente queste applicazioni oppure chiuderle manualmente e fare clic su OK per continuare l'installazione.
msi-rm-files-in-use-use-rm = &Chiudi automaticamente le applicazioni e tenta di riavviarle al termine dell'installazione.
msi-rm-files-in-use-dont-use-rm = &Non chiudere le applicazioni. (Sarà necessario riavviare il sistema.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Ripresa dell'Installazione guidata di { $app_title }
msi-resume-description = L'Installazione guidata completerà l'installazione di { $app_title } nel computer in uso. Fare clic su Installa per continuare oppure su Annulla per uscire dall'Installazione guidata.
msi-resume-btn-install = &Installa

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Collegamento sul desktop per { $app_title }
msi-start-menu-shortcut-description = Collegamento nel menu Start per { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Informazioni importanti
msi-readme-description = Leggere le seguenti informazioni prima di continuare.
