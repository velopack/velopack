# Shared titles
title-update = Обновление { $app }
title-setup = Установка { $app }
title-uninstall = Удаление { $app }
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
elevate-body = { $app } хочет обновиться до версии { $version }, но для этого требуются повышенные права. Продолжить?

# Restart required (prerequisite.rs)
restart-body = Для продолжения установки требуется перезагрузка компьютера. Пожалуйста, перезагрузите компьютер и попробуйте снова.

# Missing dependencies (prerequisite.rs)
missing-deps-body = Для работы { $app } необходимо установить следующие пакеты: { $deps }. Продолжить?

# Uninstall with errors (uninstall)
uninstall-errors-body = Удаление { $app } завершилось с ошибками. В системе могли остаться файлы или папки. Вы можете попробовать удалить их вручную или переустановить приложение и повторить попытку.
uninstall-errors-log = Файл журнала: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app } уже установлено.
overwrite-repair-body = Это приложение установлено на вашем компьютере. Если оно работает некорректно, вы можете попробовать восстановить его.
overwrite-older-installed = Установлена более старая версия { $app }.
overwrite-update-body = Хотите обновить с { $old } до { $version }?
overwrite-newer-installed = Установлена более новая версия { $app }.
overwrite-downgrade-body = У вас уже установлена версия { $old }. Хотите понизить версию приложения?
overwrite-footer = Папка установки: { $path }

# Uninstall complete (uninstall.rs)
uninstall-body = Приложение было успешно удалено.

# Install hook failed (install.rs)
install-hook-body = Установка завершена, но хук установки приложения завершился с ошибкой. Возможно, приложение установлено некорректно.

# Splash fallback (splash.rs)
splash-body = Установка { $app }...

# Apply progress (apply_*_impl.rs)
apply-body = Установка обновления { $version }...

# Start error (start_windows_impl.rs)
start-corrupt-body = Установка этого приложения повреждена и не может быть запущена. Пожалуйста, переустановите приложение.
