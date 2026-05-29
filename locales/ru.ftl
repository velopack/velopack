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
btn-ok = ОК
btn-hide = Скрыть
# Elevation (dialogs_common.rs)
elevate-header = Требуются права администратора
elevate-body = Для установки версии { $app_version } приложению { $app_title } требуются права администратора. Разрешить продолжить обновление?

# Restart required (prerequisite.rs)
restart-header = Требуется перезагрузка
restart-body = Для продолжения установки необходимо перезагрузить компьютер. Перезагрузите компьютер и запустите установку снова.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Требуются дополнительные компоненты
missing-deps-body = Для работы { $app_title } сначала нужно установить: { $deps }. Загрузить и установить их сейчас?

# Uninstall with errors (uninstall)
uninstall-errors-header = Удаление завершено с предупреждениями
uninstall-errors-body = Приложение { $app_title } удалено, но некоторые файлы или папки удалить не удалось. Вы можете удалить их вручную или переустановить приложение и попробовать удалить его снова.
uninstall-errors-log = Подробности сохранены в: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } уже установлено
overwrite-repair-body = Это приложение уже установлено на вашем компьютере. Если оно работает неправильно, его можно попробовать восстановить, выполнив переустановку.
overwrite-older-installed = { $app_title } уже установлено
overwrite-update-body = Сейчас установлена версия { $old_version }. Обновить до версии { $app_version }?
overwrite-newer-installed = Установлена более новая версия { $app_title }
overwrite-downgrade-body = Сейчас установлена версия { $old_version }, которая новее, чем эта. Понижать версию не рекомендуется, это может вызвать проблемы. Всё равно продолжить?
overwrite-footer = Папка установки: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Удаление завершено
uninstall-body = Приложение успешно удалено с вашего компьютера.

# Install hook failed (install.rs)
install-hook-header = Установка завершена частично
install-hook-body = Установка завершена, но некоторые шаги могли быть выполнены с ошибками. Если приложение работает неправильно, попробуйте переустановить его или обратитесь к разработчику.

# Splash fallback (splash.rs)
splash-header = Установка { $app_title }
splash-body = Установка { $app_title } { $app_version }, пожалуйста, подождите…

# Dependency download (prerequisite.rs)
deps-download-header = Загрузка необходимого компонента
deps-download-body = Загрузка { $dep_name }, пожалуйста, подождите…

# Apply progress (apply_*_impl.rs)
apply-header = Установка обновления
apply-body = Обновление до версии { $app_version }, пожалуйста, подождите…
progress-cancelling = Отмена...

# Start error (start_windows_impl.rs)
start-corrupt-header = Установка повреждена
start-corrupt-body = Приложение не может быть запущено, так как некоторые из его файлов отсутствуют или повреждены. Переустановите приложение, чтобы решить эту проблему.

# Generic error
error-header = Что-то пошло не так

# Setup error (wix msi)
setup-error-header = Не удалось продолжить установку

# MSI Installer UI - Common
msi-dlg-title = Установка { $app_title }
msi-btn-back = &Назад
msi-btn-next = &Далее
msi-btn-cancel = Отмена
msi-btn-finish = &Готово
msi-btn-ok = ОК
msi-btn-yes = &Да
msi-btn-no = &Нет
msi-btn-retry = &Повторить
msi-btn-ignore = &Пропустить

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Добро пожаловать в мастер установки { $app_title }
msi-welcome-description = Мастер установки установит { $app_title } на ваш компьютер. Нажмите «Далее» для продолжения или «Отмена» для выхода из мастера установки.
msi-welcome-update-description = Мастер установки обновит { $app_title } на вашем компьютере. Нажмите «Далее» для продолжения или «Отмена» для выхода из мастера установки.

# MSI Installer UI - Exit Dialog
msi-exit-title = Мастер установки { $app_title } завершён
msi-exit-description = Нажмите кнопку «Готово» для выхода из мастера установки.
msi-exit-launch-checkbox = Запустить { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Добро пожаловать в мастер установки { $app_title }
msi-prepare-description = Пожалуйста, подождите, пока мастер установки подготовится к процессу установки.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Лицензионное соглашение
msi-license-description = Пожалуйста, внимательно прочитайте следующее лицензионное соглашение.
msi-license-checkbox = Я &принимаю условия лицензионного соглашения

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Область установки
msi-scope-description = Выберите область установки.
msi-scope-per-user = Установить только для &меня
msi-scope-per-machine = Установить для &всех пользователей
msi-scope-per-user-description = Установка только для текущего пользователя
msi-scope-no-per-user-description = Требуются права администратора
msi-scope-per-machine-description = Требуются права администратора

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Всё готово для установки { $app_title }
msi-ready-install-text = Нажмите «Установить» для начала установки. Нажмите «Назад» для просмотра или изменения параметров установки.
msi-ready-change-title = Всё готово для изменения { $app_title }
msi-ready-change-text = Нажмите «Изменить» для начала изменения установки. Нажмите «Назад» для просмотра или изменения параметров.
msi-ready-repair-title = Всё готово для восстановления { $app_title }
msi-ready-repair-text = Нажмите «Восстановить» для начала восстановления. Нажмите «Назад» для просмотра или изменения параметров.
msi-ready-remove-title = Всё готово для удаления { $app_title }
msi-ready-remove-text = Нажмите «Удалить» для удаления { $app_title } с вашего компьютера. Нажмите «Назад» для просмотра или изменения параметров.
msi-ready-update-title = Всё готово для обновления { $app_title }
msi-ready-update-text = Нажмите «Обновить» для начала обновления. Нажмите «Назад» для просмотра или изменения параметров.
msi-ready-btn-install = &Установить
msi-ready-btn-change = &Изменить
msi-ready-btn-repair = &Восстановить
msi-ready-btn-remove = &Удалить
msi-ready-btn-update = &Обновить

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Установка { $app_title }
msi-progress-installing-text = Пожалуйста, подождите, пока мастер установки устанавливает { $app_title }.
msi-progress-changing-title = Изменение { $app_title }
msi-progress-changing-text = Пожалуйста, подождите, пока мастер установки изменяет { $app_title }.
msi-progress-repairing-title = Восстановление { $app_title }
msi-progress-repairing-text = Пожалуйста, подождите, пока мастер установки восстанавливает { $app_title }.
msi-progress-removing-title = Удаление { $app_title }
msi-progress-removing-text = Пожалуйста, подождите, пока мастер установки удаляет { $app_title }.
msi-progress-updating-title = Обновление { $app_title }
msi-progress-updating-text = Пожалуйста, подождите, пока мастер установки обновляет { $app_title }.
msi-progress-status = Состояние:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Добро пожаловать в мастер установки { $app_title }
msi-maint-welcome-description = Мастер установки позволит вам восстановить или удалить { $app_title }. Нажмите «Далее» для продолжения или «Отмена» для выхода из мастера установки.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Изменение, восстановление или удаление
msi-maint-type-description = Выберите нужную операцию.
msi-maint-change-button = &Изменить...
msi-maint-change-tooltip = Изменить...
msi-maint-change-text = Позволяет изменить установленные компоненты программы.
msi-maint-change-disabled = Изменение в настоящее время недоступно.
msi-maint-repair-button = &Восстановить
msi-maint-repair-tooltip = Восстановить
msi-maint-repair-text = Исправляет ошибки установки — восстанавливает отсутствующие или повреждённые файлы, ярлыки и записи реестра.
msi-maint-repair-disabled = Восстановление в настоящее время недоступно.
msi-maint-remove-button = У&далить
msi-maint-remove-tooltip = Удалить
msi-maint-remove-text = Удаляет { $app_title } с вашего компьютера.
msi-maint-remove-disabled = Удаление в настоящее время недоступно.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Вы уверены, что хотите отменить установку { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Изменение папки назначения
msi-browse-description = Выберите папку назначения.
msi-browse-combo-label = &Искать в:
msi-browse-path-label = &Имя папки:
msi-browse-up-tooltip = На уровень выше
msi-browse-new-folder-tooltip = Создать новую папку

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Указанная папка назначения недействительна или находится на неподдерживаемом типе диска.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Требования к дисковому пространству
msi-disk-cost-description = Дисковое пространство, необходимое для установки выбранных компонентов.
msi-disk-cost-text = На выделенных томах недостаточно свободного дискового пространства для выбранных компонентов. Вы можете удалить файлы с выделенных томов, выбрать меньше компонентов для установки или выбрать другой диск назначения.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } — Информация установщика

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Мастер установки { $app_title } завершился преждевременно
msi-fatal-description1 = Установка { $app_title } была прервана. Ваша система не была изменена. Для установки программы позже запустите установку повторно.
msi-fatal-description2 = Нажмите кнопку «Готово» для выхода из мастера установки.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Мастер установки { $app_title } был прерван
msi-user-exit-description1 = Установка { $app_title } была прервана. Ваша система не была изменена. Для установки программы позже запустите установку повторно.
msi-user-exit-description2 = Нажмите кнопку «Готово» для выхода из мастера установки.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Файлы используются
msi-files-in-use-description = Некоторые файлы, которые необходимо обновить, в настоящее время используются.
msi-files-in-use-text = Следующие приложения используют файлы, которые необходимо обновить. Закройте эти приложения и нажмите «Повторить» для продолжения установки или «Отмена» для выхода.
msi-files-in-use-exit = &Выход

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Файлы используются
msi-rm-files-in-use-description = Некоторые файлы, которые необходимо обновить, в настоящее время используются.
msi-rm-files-in-use-text = Следующие приложения используют файлы, которые необходимо обновить. Мастер установки может автоматически закрыть эти приложения и попытаться перезапустить их, или вы можете закрыть их вручную и нажать «ОК» для продолжения.
msi-rm-files-in-use-use-rm = Автоматически &закрыть приложения и попытаться перезапустить их после завершения установки.
msi-rm-files-in-use-dont-use-rm = &Не закрывать приложения. (Потребуется перезагрузка.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Возобновление мастера установки { $app_title }
msi-resume-description = Мастер установки завершит установку { $app_title } на ваш компьютер. Нажмите «Установить» для продолжения или «Отмена» для выхода из мастера установки.
msi-resume-btn-install = &Установить

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Ярлык { $app_title } на рабочем столе
msi-start-menu-shortcut-description = Ярлык { $app_title } в меню «Пуск»
# MSI Installer UI - Readme Dialog
msi-readme-title = Важная информация
msi-readme-description = Пожалуйста, прочитайте следующую информацию перед продолжением.
