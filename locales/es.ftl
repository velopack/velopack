# Shared titles
title-update = Actualización de { $app_title }
title-setup = Instalación de { $app_title }
title-uninstall = Desinstalación de { $app_title }
error-title = Error de { $program_name }

# Shared buttons
btn-cancel = Cancelar
btn-install-update = Instalar actualización
btn-install = Instalar
btn-update = Actualizar
btn-downgrade = Cambiar a versión anterior
btn-repair = Reparar
btn-open-log = Abrir registro
btn-open-install-dir = Abrir carpeta de instalación
btn-ok = Aceptar
btn-hide = Ocultar
# Elevation (dialogs_common.rs)
elevate-header = Se requieren permisos de administrador
elevate-body = { $app_title } necesita permisos de administrador para instalar la versión { $app_version }. ¿Permitir que continúe esta actualización?

# Restart required (prerequisite.rs)
restart-header = Reinicio requerido
restart-body = Su equipo debe reiniciarse antes de que la instalación pueda continuar. Reinicie su equipo y vuelva a ejecutar la instalación.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Se requieren componentes adicionales
missing-deps-body = { $app_title } necesita que se instalen los siguientes elementos primero: { $deps }. ¿Desea descargarlos e instalarlos ahora?

# Uninstall with errors (uninstall)
uninstall-errors-header = Desinstalación finalizada con problemas
uninstall-errors-body = { $app_title } se desinstaló, pero no se pudieron quitar algunos archivos o carpetas. Puede eliminarlos manualmente o reinstalar la aplicación y volver a intentar la desinstalación.
uninstall-errors-log = Los detalles se guardaron en: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } ya está instalado
overwrite-repair-body = Esta aplicación ya está instalada en su equipo. Si no funciona correctamente, puede intentar repararla reinstalándola.
overwrite-older-installed = { $app_title } ya está instalado
overwrite-update-body = Actualmente está instalada la versión { $old_version }. ¿Desea actualizar a la versión { $app_version }?
overwrite-newer-installed = Ya está instalada una versión más reciente de { $app_title }
overwrite-downgrade-body = Actualmente está instalada la versión { $old_version }, que es más reciente que este instalador. No se recomienda cambiar a una versión anterior y puede causar problemas. ¿Continuar de todos modos?
overwrite-footer = Instalado en: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Desinstalación completada
uninstall-body = La aplicación se eliminó correctamente de su equipo.

# Install hook failed (install.rs)
install-hook-header = Instalación parcialmente exitosa
install-hook-body = La instalación se ha completado, pero algunos pasos pueden haber fallado. Si la aplicación no funciona correctamente, puede intentar reinstalarla o ponerse en contacto con el autor de la aplicación.

# Splash fallback (splash.rs)
splash-header = Instalando { $app_title }
splash-body = Configurando { $app_title } { $app_version }, por favor espere...

# Dependency download (prerequisite.rs)
deps-download-header = Descargando componente necesario
deps-download-body = Descargando { $dep_name }, por favor espere...

# Apply progress (apply_*_impl.rs)
apply-header = Instalando actualización
apply-body = Actualizando a la versión { $app_version }, por favor espere...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalación dañada
start-corrupt-body = Esta aplicación no puede iniciarse porque faltan o están dañados algunos de sus archivos. Reinstale la aplicación para solucionarlo.

# Generic error
error-header = Algo salió mal

# Setup error (wix msi)
setup-error-header = La instalación no pudo continuar

# MSI Installer UI - Common
msi-dlg-title = Instalación de { $app_title }
msi-btn-back = &Atrás
msi-btn-next = &Siguiente
msi-btn-cancel = Cancelar
msi-btn-finish = &Finalizar
msi-btn-ok = Aceptar
msi-btn-yes = &Sí
msi-btn-no = &No
msi-btn-retry = &Reintentar
msi-btn-ignore = &Ignorar

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Asistente para la instalación de { $app_title }
msi-welcome-description = El Asistente para la instalación instalará { $app_title } en el equipo. Haga clic en Siguiente para continuar o en Cancelar para salir del asistente.
msi-welcome-update-description = El Asistente para la instalación actualizará { $app_title } en el equipo. Haga clic en Siguiente para continuar o en Cancelar para salir del asistente.

# MSI Installer UI - Exit Dialog
msi-exit-title = Ha completado el Asistente para la instalación de { $app_title }
msi-exit-description = Haga clic en el botón Finalizar para salir del Asistente para la instalación.
msi-exit-launch-checkbox = Iniciar { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Asistente para la instalación de { $app_title }
msi-prepare-description = Espere mientras el Asistente para la instalación se prepara para guiarlo durante la instalación.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Contrato de licencia para el usuario final
msi-license-description = Lea detenidamente el siguiente Contrato de licencia.
msi-license-checkbox = A&cepto los términos del Contrato de licencia

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Ámbito de la instalación
msi-scope-description = Seleccione el ámbito de instalación.
msi-scope-per-user = Instalar &solo para usted
msi-scope-per-machine = Instalar para &todos los usuarios
msi-scope-per-user-description = Se instala solo para el usuario actual
msi-scope-no-per-user-description = Requiere privilegios de administrador
msi-scope-per-machine-description = Requiere privilegios de administrador

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Listo para instalar { $app_title }
msi-ready-install-text = Haga clic en Instalar para comenzar la instalación. Haga clic en Atrás para revisar o cambiar la configuración de la instalación.
msi-ready-change-title = Listo para cambiar { $app_title }
msi-ready-change-text = Haga clic en Cambiar para comenzar a cambiar la instalación. Haga clic en Atrás para revisar o cambiar la configuración de la instalación.
msi-ready-repair-title = Listo para reparar { $app_title }
msi-ready-repair-text = Haga clic en Reparar para comenzar la reparación. Haga clic en Atrás para revisar o cambiar la configuración de la instalación.
msi-ready-remove-title = Listo para quitar { $app_title }
msi-ready-remove-text = Haga clic en Quitar para quitar { $app_title } de su equipo. Haga clic en Atrás para revisar o cambiar la configuración de la instalación.
msi-ready-update-title = Listo para actualizar { $app_title }
msi-ready-update-text = Haga clic en Actualizar para comenzar la actualización. Haga clic en Atrás para revisar o cambiar la configuración de la instalación.
msi-ready-btn-install = &Instalar
msi-ready-btn-change = &Cambiar
msi-ready-btn-repair = Re&parar
msi-ready-btn-remove = &Quitar
msi-ready-btn-update = &Actualizar

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Instalando { $app_title }
msi-progress-installing-text = Espere mientras el Asistente para la instalación instala { $app_title }.
msi-progress-changing-title = Cambiando { $app_title }
msi-progress-changing-text = Espere mientras el Asistente para la instalación cambia { $app_title }.
msi-progress-repairing-title = Reparando { $app_title }
msi-progress-repairing-text = Espere mientras el Asistente para la instalación repara { $app_title }.
msi-progress-removing-title = Quitando { $app_title }
msi-progress-removing-text = Espere mientras el Asistente para la instalación quita { $app_title }.
msi-progress-updating-title = Actualizando { $app_title }
msi-progress-updating-text = Espere mientras el Asistente para la instalación actualiza { $app_title }.
msi-progress-status = Estado:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Asistente para la instalación de { $app_title }
msi-maint-welcome-description = El Asistente para la instalación le permitirá reparar o quitar { $app_title }. Haga clic en Siguiente para continuar o en Cancelar para salir del asistente.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Cambiar, reparar o quitar la instalación
msi-maint-type-description = Seleccione la operación que desea realizar.
msi-maint-change-button = &Cambiar...
msi-maint-change-tooltip = Cambiar...
msi-maint-change-text = Permite a los usuarios cambiar qué características del programa se instalan y cambiar características individuales.
msi-maint-change-disabled = Cambiar está deshabilitado actualmente.
msi-maint-repair-button = Re&parar
msi-maint-repair-tooltip = Reparar
msi-maint-repair-text = Repara errores en la instalación más reciente - corrige archivos, accesos directos y entradas de Registro que faltan o están dañados.
msi-maint-repair-disabled = Reparar está deshabilitado actualmente.
msi-maint-remove-button = &Quitar
msi-maint-remove-tooltip = Quitar
msi-maint-remove-text = Quita { $app_title } de su equipo.
msi-maint-remove-disabled = Quitar está deshabilitado actualmente.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = ¿Está seguro de que desea cancelar la instalación de { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Cambiar carpeta de destino actual
msi-browse-description = Busque la carpeta de destino.
msi-browse-combo-label = &Buscar en:
msi-browse-path-label = &Nombre de carpeta:
msi-browse-up-tooltip = Subir un nivel
msi-browse-new-folder-tooltip = Crear una nueva carpeta

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = El directorio de destino especificado no es válido o se encuentra en un tipo de unidad no admitida.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Requisitos de espacio en disco
msi-disk-cost-description = Espacio en disco necesario para la instalación de las características seleccionadas.
msi-disk-cost-text = Los volúmenes resaltados no tienen espacio en disco suficiente para las características seleccionadas actualmente. Puede quitar algunos archivos de los volúmenes resaltados, elegir instalar menos características en la(s) unidad(es) local(es), o seleccionar otras unidades de destino.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Información del instalador de { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = El Asistente para la instalación de { $app_title } finalizó antes de tiempo
msi-fatal-description1 = Se interrumpió la instalación de { $app_title }. El sistema no se ha modificado. Para instalar este programa más tarde, vuelva a ejecutar la instalación.
msi-fatal-description2 = Haga clic en el botón Finalizar para salir del Asistente para la instalación.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Se interrumpió el Asistente para la instalación de { $app_title }
msi-user-exit-description1 = Se interrumpió la instalación de { $app_title }. El sistema no se ha modificado. Para instalar este programa más tarde, vuelva a ejecutar la instalación.
msi-user-exit-description2 = Haga clic en el botón Finalizar para salir del Asistente para la instalación.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Archivos en uso
msi-files-in-use-description = Algunos archivos que es necesario actualizar se están utilizando en este momento.
msi-files-in-use-text = Las siguientes aplicaciones están utilizando archivos que el programa de instalación debe actualizar. Cierre estas aplicaciones y haga clic en Reintentar para continuar con la instalación o en Cancelar para salir de ella.
msi-files-in-use-exit = S&alir

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Archivos en uso
msi-rm-files-in-use-description = Algunos archivos que es necesario actualizar se están utilizando en este momento.
msi-rm-files-in-use-text = Las siguientes aplicaciones están utilizando archivos que el programa de instalación debe actualizar. Puede dejar que el Asistente para la instalación cierre e intente reiniciar estas aplicaciones automáticamente, o puede cerrarlas manualmente y hacer clic en Aceptar para continuar con la instalación.
msi-rm-files-in-use-use-rm = &Cerrar automáticamente las aplicaciones e intentar reiniciarlas después de que finalice la instalación.
msi-rm-files-in-use-dont-use-rm = &No cerrar las aplicaciones. (Será necesario reiniciar.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Reanudando el Asistente para la instalación de { $app_title }
msi-resume-description = El Asistente para la instalación completará la instalación de { $app_title } en su equipo. Haga clic en Instalar para continuar o en Cancelar para salir del Asistente para la instalación.
msi-resume-btn-install = &Instalar

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Acceso directo de escritorio para { $app_title }
msi-start-menu-shortcut-description = Acceso directo del menú Inicio para { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Información importante
msi-readme-description = Lea la siguiente información antes de continuar.
