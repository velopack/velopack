# Buttons
btn-cancel = Отмена
btn-install-update = Установить обновление
btn-open-log = Открыть журнал
btn-open-install-dir = Открыть папку установки

# Generic errors (cli_host.rs)
error-title = Ошибка { $program_name }

# Elevation (dialogs_common.rs)
elevate-title = Обновление { $app }
elevate-body = { $app } хочет обновиться до версии { $version }, но для этого требуются повышенные права. Продолжить?

# Restart required (prerequisite.rs)
restart-title = Установка { $app } { $version }
restart-header = Требуется перезагрузка
restart-body = Для продолжения установки требуется перезагрузка компьютера. Пожалуйста, перезагрузите компьютер и попробуйте снова.

# Update missing dependencies (prerequisite.rs)
update-deps-title = Обновление { $app }
update-deps-header = { $app } хочет обновиться с { $from } до { $to }
update-deps-body = Для { $app } { $to } необходимо установить недостающие зависимости: { $deps }. Продолжить?
update-deps-button = Установить и обновить

# Setup missing dependencies (prerequisite.rs)
setup-deps-title = Установка { $app } { $version }
setup-deps-header = У { $app } отсутствуют системные зависимости.
setup-deps-body = Для работы { $app } необходимо установить следующие пакеты: { $deps }. Продолжить?
setup-deps-button = Установить

# Uninstall with errors (uninstall)
uninstall-errors-title = Удаление { $app }
uninstall-errors-header = Удаление { $app } завершилось с ошибками.
uninstall-errors-body = В системе могли остаться файлы или папки. Вы можете попробовать удалить их вручную или переустановить приложение и повторить попытку.
uninstall-errors-log = Файл журнала: { $path }

# Process locking (locksmith.rs)
locking-title = Обновление { $app } { $version }
locking-header = Обновление { $app }
locking-body = Программы ({ $processes }) препятствуют обновлению { $app }. Нажмите «Продолжить», чтобы средство обновления попыталось закрыть их автоматически, или, если вы закрыли их самостоятельно, нажмите «Повторить» для повторной проверки.
locking-retry = Повторить
locking-continue = Продолжить
locking-cancel = Отмена

# Overwrite/repair dialog (install.rs)
overwrite-title = Установка { $app } { $version }
overwrite-already-installed = { $app } уже установлено.
overwrite-repair-body = Это приложение установлено на вашем компьютере. Если оно работает некорректно, вы можете попробовать восстановить его.
overwrite-repair-button = Восстановить
overwrite-older-installed = Установлена более старая версия { $app }.
overwrite-update-body = Хотите обновить с { $old } до { $version }?
overwrite-update-button = Обновить
overwrite-newer-installed = Установлена более новая версия { $app }.
overwrite-downgrade-body = У вас уже установлена версия { $old }. Хотите понизить версию приложения?
overwrite-downgrade-button = Понизить версию
overwrite-footer-default = Папка установки: %LocalAppData%\{ $id }
overwrite-footer-custom = Папка установки: { $path }

# Uninstall complete (uninstall.rs)
uninstall-title = Удаление { $app }
uninstall-body = Приложение было успешно удалено.

# Install hook failed (install.rs)
install-hook-title = Установка { $app } { $id }
install-hook-body = Установка завершена, но хук установки приложения завершился с ошибкой. Возможно, приложение установлено некорректно.

# Splash fallback (splash.rs)
splash-setup-title = Установка { $app }
splash-setup-body = Установка { $app }...

# Apply progress (apply_*_impl.rs)
apply-title = Обновление { $app }
apply-body = Установка обновления { $version }...

# Start error (start_windows_impl.rs)
start-corrupt-header = Не удалось запустить приложение
start-corrupt-body = Установка этого приложения повреждена и не может быть запущена. Пожалуйста, переустановите приложение.
