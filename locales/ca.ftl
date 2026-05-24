# Shared titles
title-update = Actualització de { $app_title }
title-setup = Instal·lació de { $app_title }
title-uninstall = Desinstal·lació de { $app_title }
error-title = Error de { $program_name }

# Shared buttons
btn-cancel = Cancel·la
btn-install-update = Instal·la l'actualització
btn-install = Instal·la
btn-update = Actualitza
btn-downgrade = Reverteix la versió
btn-repair = Repara
btn-open-log = Obre el registre
btn-open-install-dir = Obre el directori d'instal·lació

# Elevation (dialogs_common.rs)
elevate-header = Cal permís d'administrador
elevate-body = { $app_title } necessita permís d'administrador per instal·lar la versió { $app_version }. Voleu permetre que continuï aquesta actualització?

# Restart required (prerequisite.rs)
restart-header = Cal reiniciar
restart-body = Cal reiniciar l'ordinador abans que la instal·lació pugui continuar. Reinicieu l'ordinador i executeu la instal·lació de nou.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Cal instal·lar components addicionals
missing-deps-body = { $app_title } necessita que s'instal·lin primer els elements següents: { $deps }. Voleu baixar-los i instal·lar-los ara?

# Uninstall with errors (uninstall)
uninstall-errors-header = La desinstal·lació ha finalitzat amb problemes
uninstall-errors-body = S'ha desinstal·lat { $app_title }, però no s'han pogut suprimir alguns fitxers o carpetes. Podeu suprimir-los manualment o reinstal·lar l'aplicació i provar a desinstal·lar-la de nou.
uninstall-errors-log = Els detalls s'han desat a: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } ja està instal·lat
overwrite-repair-body = Aquesta aplicació ja està instal·lada a l'ordinador. Si no funciona correctament, podeu provar de reparar-la reinstal·lant-la.
overwrite-older-installed = { $app_title } ja està instal·lat
overwrite-update-body = Actualment està instal·lada la versió { $old_version }. Voleu actualitzar a la versió { $app_version }?
overwrite-newer-installed = Ja hi ha instal·lada una versió més nova de { $app_title }
overwrite-downgrade-body = Actualment està instal·lada la versió { $old_version }, que és més nova que aquest instal·lador. No es recomana revertir la versió i pot causar problemes. Voleu continuar de tota manera?
overwrite-footer = Instal·lat a: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Desinstal·lació completada
uninstall-body = L'aplicació s'ha suprimit correctament de l'ordinador.

# Install hook failed (install.rs)
install-hook-header = La instal·lació ha estat parcialment satisfactòria
install-hook-body = La instal·lació s'ha completat, però alguns passos poden haver fallat. Si l'aplicació no funciona correctament, podeu provar de reinstal·lar-la o contactar amb l'autor de l'aplicació.

# Splash fallback (splash.rs)
splash-header = S'està instal·lant { $app_title }
splash-body = S'està configurant { $app_title } { $app_version }, espereu...

# Dependency download (prerequisite.rs)
deps-download-header = S'està baixant el component necessari
deps-download-body = S'està baixant { $dep_name }, espereu...

# Apply progress (apply_*_impl.rs)
apply-header = S'està instal·lant l'actualització
apply-body = S'està actualitzant a la versió { $app_version }, espereu...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instal·lació malmesa
start-corrupt-body = Aquesta aplicació no pot iniciar-se perquè alguns dels seus fitxers no es troben o estan malmesos. Reinstal·leu l'aplicació per solucionar-ho.

# Generic error
error-header = Alguna cosa ha anat malament

# Setup error (wix msi)
setup-error-header = La instal·lació no pot continuar

# MSI Installer UI - Common
msi-dlg-title = Instal·lació de { $app_title }
msi-btn-back = &Endarrere
msi-btn-next = &Endavant
msi-btn-cancel = Cancel·la
msi-btn-finish = &Final
msi-btn-ok = D'acord
msi-btn-yes = &Sí
msi-btn-no = &No
msi-btn-retry = &Torna-ho a provar
msi-btn-ignore = &Ignora-ho

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Benvinguts a l'auxiliar d'instal·lació de { $app_title }
msi-welcome-description = L'auxiliar d'instal·lació instal·larà { $app_title } a l'ordinador. Feu clic a Endavant per continuar o a Cancel·la per sortir de l'auxiliar d'instal·lació.
msi-welcome-update-description = L'auxiliar d'instal·lació actualitzarà { $app_title } a l'ordinador. Feu clic a Endavant per continuar o a Cancel·la per sortir de l'auxiliar d'instal·lació.

# MSI Installer UI - Exit Dialog
msi-exit-title = S'ha completat l'auxiliar d'instal·lació de { $app_title }
msi-exit-description = Feu clic al botó Final per sortir de l'auxiliar d'instal·lació.
msi-exit-launch-checkbox = Inicia { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Benvinguts a l'auxiliar d'instal·lació de { $app_title }
msi-prepare-description = Espereu mentre l'auxiliar d'instal·lació es prepara per guiar-vos pel procés d'instal·lació.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Contracte de llicència de l'usuari final
msi-license-description = Llegiu el contracte de llicència següent atentament.
msi-license-checkbox = &Accepto les condicions del contracte de llicència

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Àmbit d'instal·lació
msi-scope-description = Seleccioneu l'àmbit d'instal·lació.
msi-scope-per-user = Instal·la &només per al vostre usuari
msi-scope-per-machine = Instal·la per a &tots els usuaris
msi-scope-per-user-description = S'instal·la només per a l'usuari actual
msi-scope-no-per-user-description = Requereix privilegis d'administrador
msi-scope-per-machine-description = Requereix privilegis d'administrador

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = A punt per instal·lar { $app_title }
msi-ready-install-text = Feu clic a Instal·la per començar la instal·lació. Feu clic a Endarrere per revisar o canviar les opcions de configuració de la instal·lació.
msi-ready-change-title = A punt per canviar { $app_title }
msi-ready-change-text = Feu clic a Canvia per començar el canvi de la instal·lació. Feu clic a Endarrere per revisar o canviar les opcions de configuració de la instal·lació.
msi-ready-repair-title = A punt per reparar { $app_title }
msi-ready-repair-text = Feu clic a Repara per començar la reparació. Feu clic a Endarrere per revisar o canviar les opcions de configuració de la instal·lació.
msi-ready-remove-title = A punt per suprimir { $app_title }
msi-ready-remove-text = Feu clic a Suprimeix per suprimir { $app_title } de l'ordinador. Feu clic a Endarrere per revisar o canviar les opcions de configuració de la instal·lació.
msi-ready-update-title = A punt per actualitzar { $app_title }
msi-ready-update-text = Feu clic a Actualitza per començar l'actualització. Feu clic a Endarrere per revisar o canviar les opcions de configuració de la instal·lació.
msi-ready-btn-install = &Instal·la
msi-ready-btn-change = &Canvia
msi-ready-btn-repair = Re&para
msi-ready-btn-remove = &Suprimeix
msi-ready-btn-update = &Actualitza

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = S'està instal·lant { $app_title }
msi-progress-installing-text = Espereu mentre l'auxiliar d'instal·lació instal·la { $app_title }.
msi-progress-changing-title = S'està canviant { $app_title }
msi-progress-changing-text = Espereu mentre l'auxiliar d'instal·lació canvia { $app_title }.
msi-progress-repairing-title = S'està reparant { $app_title }
msi-progress-repairing-text = Espereu mentre l'auxiliar d'instal·lació repara { $app_title }.
msi-progress-removing-title = S'està suprimint { $app_title }
msi-progress-removing-text = Espereu mentre l'auxiliar d'instal·lació suprimeix { $app_title }.
msi-progress-updating-title = S'està actualitzant { $app_title }
msi-progress-updating-text = Espereu mentre l'auxiliar d'instal·lació actualitza { $app_title }.
msi-progress-status = Estat:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Benvinguts a l'auxiliar d'instal·lació de { $app_title }
msi-maint-welcome-description = L'auxiliar d'instal·lació us permetrà reparar o suprimir { $app_title }. Feu clic a Endavant per continuar o a Cancel·la per sortir de l'auxiliar d'instal·lació.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Canvi, reparació o supressió de la instal·lació
msi-maint-type-description = Seleccioneu l'operació que voleu realitzar.
msi-maint-change-button = &Canvia...
msi-maint-change-tooltip = Canvia...
msi-maint-change-text = Permet als usuaris canviar quines característiques del programa s'instal·len i canviar característiques individuals.
msi-maint-change-disabled = Canvia està desactivat actualment.
msi-maint-repair-button = Re&para
msi-maint-repair-tooltip = Repara
msi-maint-repair-text = Repara els errors de la instal·lació més recent - corregeix fitxers, dreceres i entrades del registre perduts o malmesos.
msi-maint-repair-disabled = Repara està desactivat actualment.
msi-maint-remove-button = &Suprimeix
msi-maint-remove-tooltip = Suprimeix
msi-maint-remove-text = Suprimeix { $app_title } de l'ordinador.
msi-maint-remove-disabled = Suprimeix està desactivat actualment.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Esteu segur que voleu cancel·lar la instal·lació de { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Canvia la carpeta de destinació actual
msi-browse-description = Navega a la carpeta de destinació.
msi-browse-combo-label = &Mira a:
msi-browse-path-label = &Nom de la carpeta:
msi-browse-up-tooltip = Un nivell amunt
msi-browse-new-folder-tooltip = Crea una carpeta nova

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = El directori de destinació especificat no és vàlid o es troba en un tipus d'unitat no compatible.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Requisits d'espai al disc
msi-disk-cost-description = L'espai de disc necessari per a la instal·lació de les característiques seleccionades.
msi-disk-cost-text = Els volums ressaltats no tenen prou espai disponible al disc per a les característiques actualment seleccionades. Podeu suprimir alguns fitxers dels volums ressaltats, triar instal·lar menys característiques a les unitats locals, o seleccionar altres unitats de destinació.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informació de l'instal·lador de { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = L'auxiliar d'instal·lació de { $app_title } ha finalitzat abans d'hora
msi-fatal-description1 = S'ha interromput la instal·lació de { $app_title }. No s'ha modificat el sistema. Per instal·lar aquest programa en un altre moment, torneu a executar la instal·lació.
msi-fatal-description2 = Feu clic al botó Final per sortir de l'auxiliar d'instal·lació.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = S'ha interromput l'auxiliar d'instal·lació de { $app_title }
msi-user-exit-description1 = S'ha interromput la instal·lació de { $app_title }. No s'ha modificat el sistema. Per instal·lar aquest programa en un altre moment, torneu a executar la instal·lació.
msi-user-exit-description2 = Feu clic al botó Final per sortir de l'auxiliar d'instal·lació.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Fitxers en ús
msi-files-in-use-description = S'estan utilitzant alguns fitxers que cal actualitzar.
msi-files-in-use-text = Les següents aplicacions fan servir fitxers que cal actualitzar en aquesta instal·lació. Tanqueu aquestes aplicacions i feu clic a Torna-ho a provar per continuar amb la instal·lació o bé a Cancel·la per sortir-ne.
msi-files-in-use-exit = &Surt

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Fitxers en ús
msi-rm-files-in-use-description = S'estan utilitzant alguns fitxers que cal actualitzar.
msi-rm-files-in-use-text = Les següents aplicacions fan servir fitxers que cal actualitzar en aquesta instal·lació. Podeu deixar que l'auxiliar d'instal·lació tanqui i intenti reiniciar aquestes aplicacions automàticament, o podeu tancar-les manualment i fer clic a D'acord per continuar amb la instal·lació.
msi-rm-files-in-use-use-rm = &Tanca automàticament les aplicacions i intenta reiniciar-les quan finalitzi la instal·lació.
msi-rm-files-in-use-dont-use-rm = &No tanquis les aplicacions. (Caldrà reiniciar.)

# MSI Installer UI - Resume Dialog
msi-resume-title = S'està reprenent l'auxiliar d'instal·lació de { $app_title }
msi-resume-description = L'auxiliar d'instal·lació completarà la instal·lació de { $app_title } a l'ordinador. Feu clic a Instal·la per continuar o a Cancel·la per sortir de l'auxiliar d'instal·lació.
msi-resume-btn-install = &Instal·la

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Drecera d'escriptori per a { $app_title }
msi-start-menu-shortcut-description = Drecera del menú Inici per a { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Informació important
msi-readme-description = Si us plau, llegiu la informació següent abans de continuar.
