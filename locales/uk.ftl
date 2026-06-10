# Shared titles
title-update = Оновлення { $app_title }
title-setup = Інсталяція { $app_title }
title-uninstall = Видалення { $app_title }
error-title = Помилка { $program_name }

# Shared buttons
btn-cancel = Скасувати
btn-install-update = Інсталювати оновлення
btn-install = Інсталювати
btn-update = Оновити
btn-downgrade = Понизити версію
btn-repair = Відновити
btn-open-log = Відкрити журнал
btn-open-install-dir = Відкрити папку інсталяції
btn-ok = ОК
# Elevation (dialogs_common.rs)
elevate-header = Потрібен дозвіл адміністратора
elevate-body = Для інсталяції версії { $app_version } програмі { $app_title } потрібен дозвіл адміністратора. Дозволити продовжити це оновлення?

# Restart required (prerequisite.rs)
restart-header = Потрібно перезавантажити
restart-body = Перш ніж продовжити інсталяцію, необхідно перезавантажити комп’ютер. Перезавантажте комп’ютер і запустіть інсталятор знову.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Потрібні додаткові компоненти
missing-deps-body = Для { $app_title } спочатку потрібно інсталювати: { $deps }. Завантажити й інсталювати їх зараз?

# Uninstall with errors (uninstall)
uninstall-errors-header = Видалення завершено з проблемами
uninstall-errors-body = Програму { $app_title } видалено, але деякі файли або папки не вдалося видалити. Ви можете видалити їх вручну або перевстановити застосунок і повторити спробу видалення.
uninstall-errors-log = Подробиці збережено в: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } уже інстальовано
overwrite-repair-body = Цей застосунок уже інстальовано на вашому комп’ютері. Якщо він працює неправильно, спробуйте відновити його, перевстановивши.
overwrite-older-installed = { $app_title } уже інстальовано
overwrite-update-body = Зараз інстальовано версію { $old_version }. Оновити до версії { $app_version }?
overwrite-newer-installed = Уже інстальовано новішу версію { $app_title }
overwrite-downgrade-body = Зараз інстальовано версію { $old_version }, яка новіша за цей інсталятор. Зниження версії не рекомендовано та може спричинити проблеми. Продовжити попри це?
overwrite-footer = Інстальовано у: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Видалення завершено
uninstall-body = Застосунок успішно видалено з вашого комп’ютера.

# Install hook failed (install.rs)
install-hook-header = Інсталяцію частково завершено
install-hook-body = Інсталяцію завершено, але деякі кроки могли не виконатися. Якщо застосунок працює неправильно, спробуйте перевстановити його або зверніться до автора застосунку.

# Splash fallback (splash.rs)
splash-header = Інсталяція { $app_title }
splash-body = Налаштування { $app_title } { $app_version }, зачекайте…

# Dependency download (prerequisite.rs)
deps-download-header = Завантаження необхідного компонента
deps-download-body = Завантаження { $dep_name }, зачекайте…

# Apply progress (apply_*_impl.rs)
apply-header = Інсталяція оновлення
apply-body = Оновлення до версії { $app_version }, зачекайте…

# Start error (start_windows_impl.rs)
start-corrupt-header = Інсталяцію пошкоджено
start-corrupt-body = Цей застосунок не може запуститися, оскільки деякі з його файлів відсутні або пошкоджені. Перевстановіть застосунок, щоб виправити це.

# Generic error
error-header = Щось пішло не так

# Setup error (wix msi)
setup-error-header = Не вдалося продовжити інсталяцію
setup-disk-space-insufficient = { $app_title } requires at least { $required_space } disk space to be installed. There is only { $available_space } available.

# MSI Installer UI - Common
msi-dlg-title = Інсталяція { $app_title }
msi-btn-back = &Назад
msi-btn-next = &Далі
msi-btn-cancel = Скасувати
msi-btn-finish = &Готово
msi-btn-ok = ОК
msi-btn-yes = &Так
msi-btn-no = &Ні
msi-btn-retry = &Повторити
msi-btn-ignore = &Пропустити

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Вас вітає майстер інсталяції { $app_title }
msi-welcome-description = Майстер інсталяції інсталює { $app_title } на ваш комп’ютер. Натисніть «Далі», щоб продовжити, або «Скасувати», щоб закрити майстер інсталяції.
msi-welcome-update-description = Майстер інсталяції оновить { $app_title } на вашому комп’ютері. Натисніть «Далі», щоб продовжити, або «Скасувати», щоб закрити майстер інсталяції.

# MSI Installer UI - Exit Dialog
msi-exit-title = Роботу майстра інсталяції { $app_title } завершено
msi-exit-description = Натисніть кнопку «Готово», щоб закрити майстер інсталяції.
msi-exit-launch-checkbox = Запустити { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Вас вітає майстер інсталяції { $app_title }
msi-prepare-description = Зачекайте: майстер готується до інсталяції.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Ліцензійна угода
msi-license-description = Уважно прочитайте наведену нижче ліцензійну угоду.
msi-license-checkbox = Я &приймаю умови ліцензійної угоди

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Тип інсталяції
msi-scope-description = Виберіть тип інсталяції.
msi-scope-per-user = Інсталювати &лише для вас
msi-scope-per-machine = Інсталювати для &всіх користувачів
msi-scope-per-user-description = Інсталяція лише для поточного користувача
msi-scope-no-per-user-description = Потрібні права адміністратора
msi-scope-per-machine-description = Потрібні права адміністратора

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Усе готово до інсталяції { $app_title }
msi-ready-install-text = Натисніть «Інсталювати», щоб розпочати інсталяцію. Натисніть «Назад», щоб переглянути або змінити параметри інсталяції.
msi-ready-change-title = Усе готово до змінення { $app_title }
msi-ready-change-text = Натисніть «Змінити», щоб розпочати змінення інсталяції. Натисніть «Назад», щоб переглянути або змінити параметри інсталяції.
msi-ready-repair-title = Усе готово до відновлення { $app_title }
msi-ready-repair-text = Натисніть «Відновити», щоб розпочати відновлення. Натисніть «Назад», щоб переглянути або змінити параметри інсталяції.
msi-ready-remove-title = Усе готово до видалення { $app_title }
msi-ready-remove-text = Натисніть «Видалити», щоб видалити { $app_title } з комп’ютера. Натисніть «Назад», щоб переглянути або змінити параметри інсталяції.
msi-ready-update-title = Усе готово до оновлення { $app_title }
msi-ready-update-text = Натисніть «Оновити», щоб розпочати оновлення. Натисніть «Назад», щоб переглянути або змінити параметри інсталяції.
msi-ready-btn-install = &Інсталювати
msi-ready-btn-change = &Змінити
msi-ready-btn-repair = Відно&вити
msi-ready-btn-remove = &Видалити
msi-ready-btn-update = &Оновити

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Триває інсталяція { $app_title }
msi-progress-installing-text = Зачекайте: майстер виконує інсталяцію { $app_title }.
msi-progress-changing-title = Змінення { $app_title }
msi-progress-changing-text = Зачекайте: майстер інсталяції змінює { $app_title }.
msi-progress-repairing-title = Відновлення { $app_title }
msi-progress-repairing-text = Зачекайте: майстер інсталяції відновлює { $app_title }.
msi-progress-removing-title = Видалення { $app_title }
msi-progress-removing-text = Зачекайте: майстер інсталяції видаляє { $app_title }.
msi-progress-updating-title = Оновлення { $app_title }
msi-progress-updating-text = Зачекайте: майстер інсталяції оновлює { $app_title }.
msi-progress-status = Стан:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Вас вітає майстер інсталяції { $app_title }
msi-maint-welcome-description = Майстер інсталяції дає змогу відновити або видалити { $app_title }. Натисніть «Далі», щоб продовжити, або «Скасувати», щоб закрити майстер інсталяції.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Змінити, відновити або видалити інсталяцію
msi-maint-type-description = Виберіть операцію, яку потрібно виконати.
msi-maint-change-button = &Змінити...
msi-maint-change-tooltip = Змінити...
msi-maint-change-text = Дає змогу користувачам змінити, які компоненти програми інстальовані, та змінювати окремі компоненти.
msi-maint-change-disabled = Змінення наразі вимкнено.
msi-maint-repair-button = Відно&вити
msi-maint-repair-tooltip = Відновити
msi-maint-repair-text = Виправляє помилки останньої інсталяції — відновлює втрачені або пошкоджені файли, ярлики й записи реєстру.
msi-maint-repair-disabled = Відновлення наразі вимкнено.
msi-maint-remove-button = &Видалити
msi-maint-remove-tooltip = Видалити
msi-maint-remove-text = Видаляє { $app_title } з вашого комп’ютера.
msi-maint-remove-disabled = Видалення наразі вимкнено.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Справді скасувати інсталяцію { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Змінення поточної папки призначення
msi-browse-description = Перехід до папки призначення.
msi-browse-combo-label = &Область пошуку:
msi-browse-path-label = &Ім’я папки:
msi-browse-up-tooltip = Перейти на рівень вгору
msi-browse-new-folder-tooltip = Створення нової папки

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Указаний каталог призначення недійсний або розташований на непідтримуваному типі диска.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Вимоги до дискового простору
msi-disk-cost-description = Дисковий простір, необхідний для інсталяції вибраних компонентів.
msi-disk-cost-text = У виділених томах бракує дискового простору для поточних вибраних компонентів. Можна видалити з виділених томів кілька файлів, інсталювати менше компонентів на локальні диски або вибрати інші диски призначення.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Інформація інсталятора { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Роботу майстра інсталяції { $app_title } завершено передчасно
msi-fatal-description1 = Інсталяцію { $app_title } було перервано. Систему не змінено. Щоб інсталювати цю програму пізніше, знову запустіть інсталятор.
msi-fatal-description2 = Натисніть кнопку «Готово», щоб закрити майстер інсталяції.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Роботу майстра інсталяції { $app_title } було перервано
msi-user-exit-description1 = Інсталяцію { $app_title } було перервано. Систему не змінено. Щоб інсталювати цю програму пізніше, знову запустіть інсталятор.
msi-user-exit-description2 = Натисніть кнопку «Готово», щоб закрити майстер інсталяції.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Файли, які використовуються
msi-files-in-use-description = Деякі файли, що підлягають оновленню, зараз використовуються.
msi-files-in-use-text = Наведені нижче застосунки використовують файли, які підлягають оновленню під час цієї інсталяції. Закривши ці застосунки, натисніть «Повторити», щоб продовжити інсталяцію, або «Скасувати», щоб припинити її.
msi-files-in-use-exit = Ви&хід

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Файли, які використовуються
msi-rm-files-in-use-description = Деякі файли, що підлягають оновленню, зараз використовуються.
msi-rm-files-in-use-text = Наведені нижче застосунки використовують файли, які підлягають оновленню під час цієї інсталяції. Можна дозволити майстру інсталяції автоматично закрити та спробувати перезапустити їх, або закрити їх вручну та натиснути «ОК», щоб продовжити інсталяцію.
msi-rm-files-in-use-use-rm = Автоматично &закрити застосунки та спробувати перезапустити їх після завершення інсталяції.
msi-rm-files-in-use-dont-use-rm = &Не закривати застосунки. (Потрібно буде перезавантажити комп’ютер.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Відновлення роботи майстра інсталяції { $app_title }
msi-resume-description = Майстер інсталяції завершить інсталяцію { $app_title } на вашому комп’ютері. Натисніть «Інсталювати», щоб продовжити, або «Скасувати», щоб закрити майстер інсталяції.
msi-resume-btn-install = &Інсталювати

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Ярлик { $app_title } на робочому столі
msi-start-menu-shortcut-description = Ярлик { $app_title } у меню «Пуск»
# MSI Installer UI - Readme Dialog
msi-readme-title = Важлива інформація
msi-readme-description = Будь ласка, прочитайте наступну інформацію перед продовженням.
