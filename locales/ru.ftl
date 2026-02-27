# Shared titles
title-update = Обновление { $app_title }
title-setup = Установка { $app_title }
title-uninstall = Удаление { $app_title }
error-title = Ошибка { $program_name }

# Shared buttons
btn-cancel = Отмена
btn-install-update = Установить обновление
btn-install = Установить
btn-update = Обновить
btn-downgrade = Понизить версию
btn-repair = Восстановить
btn-open-log = Открыть журнал
btn-open-install-dir = Открыть папку установки

# Elevation (dialogs_common.rs)
elevate-header = Требуются повышенные права
elevate-body = { $app_title } хочет обновиться до версии { $app_version }, но для этого требуются повышенные права. Продолжить?

# Restart required (prerequisite.rs)
restart-header = Требуется перезагрузка
restart-body = Для продолжения установки требуется перезагрузка компьютера. Пожалуйста, перезагрузите компьютер и попробуйте снова.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Отсутствуют зависимости
missing-deps-body = Для работы { $app_title } необходимо установить следующие пакеты: { $deps }. Продолжить?

# Uninstall with errors (uninstall)
uninstall-errors-header = Удаление завершено с ошибками
uninstall-errors-body = Удаление { $app_title } завершилось с ошибками. В системе могли остаться файлы или папки. Вы можете попробовать удалить их вручную или переустановить приложение и повторить попытку.
uninstall-errors-log = Файл журнала: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } уже установлено.
overwrite-repair-body = Это приложение установлено на вашем компьютере. Если оно работает некорректно, вы можете попробовать восстановить его.
overwrite-older-installed = Установлена более старая версия { $app_title }.
overwrite-update-body = Хотите обновить с { $old_version } до { $app_version }?
overwrite-newer-installed = Установлена более новая версия { $app_title }.
overwrite-downgrade-body = У вас уже установлена версия { $old_version }. Хотите понизить версию приложения?
overwrite-footer = Папка установки: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Удаление завершено
uninstall-body = Приложение было успешно удалено.

# Install hook failed (install.rs)
install-hook-header = Ошибка хука установки
install-hook-body = Установка завершена, но хук установки приложения завершился с ошибкой. Возможно, приложение установлено некорректно.

# Splash fallback (splash.rs)
splash-header = Установка { $app_title }
splash-body = Установка { $app_title } { $app_version }...

# Dependency download (prerequisite.rs)
deps-download-header = Загрузка зависимости
deps-download-body = { $dep_name }...

# Apply progress (apply_*_impl.rs)
apply-header = Установка обновления
apply-body = Установка обновления { $app_version }...

# Start error (start_windows_impl.rs)
start-corrupt-header = Установка повреждена
start-corrupt-body = Установка этого приложения повреждена и не может быть запущена. Пожалуйста, переустановите приложение.

# Generic error
error-header = Произошла ошибка

# Setup error (wix msi)
setup-error-header = Установка не может быть продолжена
