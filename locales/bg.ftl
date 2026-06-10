# Shared titles
title-update = Актуализация на { $app_title }
title-setup = Инсталиране на { $app_title }
title-uninstall = Деинсталиране на { $app_title }
error-title = Грешка в { $program_name }

# Shared buttons
btn-cancel = Отказ
btn-install-update = Инсталиране на актуализацията
btn-install = Инсталирай
btn-update = Актуализирай
btn-downgrade = Понижи версията
btn-repair = Поправи
btn-open-log = Отвори дневника
btn-open-install-dir = Отвори инсталационната директория
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Изискват се администраторски разрешения
elevate-body = { $app_title } се нуждае от администраторски разрешения, за да инсталира версия { $app_version }. Разрешавате ли тази актуализация да продължи?

# Restart required (prerequisite.rs)
restart-header = Изисква се рестартиране
restart-body = Компютърът трябва да се рестартира, преди инсталирането да може да продължи. Рестартирайте компютъра и стартирайте инсталирането отново.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Изискват се допълнителни компоненти
missing-deps-body = За { $app_title } трябва първо да се инсталират следните: { $deps }. Желаете ли да ги изтеглите и инсталирате сега?

# Uninstall with errors (uninstall)
uninstall-errors-header = Деинсталирането завърши с проблеми
uninstall-errors-body = { $app_title } беше деинсталирано, но някои файлове или папки не можаха да бъдат премахнати. Можете да ги изтриете ръчно или да преинсталирате приложението и да опитате да го деинсталирате отново.
uninstall-errors-log = Подробностите бяха записани в: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } вече е инсталирано
overwrite-repair-body = Това приложение вече е инсталирано на вашия компютър. Ако то не работи правилно, можете да опитате да го поправите чрез преинсталиране.
overwrite-older-installed = { $app_title } вече е инсталирано
overwrite-update-body = В момента е инсталирана версия { $old_version }. Желаете ли да актуализирате до версия { $app_version }?
overwrite-newer-installed = По-нова версия на { $app_title } вече е инсталирана
overwrite-downgrade-body = В момента е инсталирана версия { $old_version }, която е по-нова от тази инсталационна програма. Понижаването на версията не се препоръчва и може да причини проблеми. Желаете ли да продължите въпреки това?
overwrite-footer = Инсталирано в: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Деинсталирането завърши
uninstall-body = Приложението беше успешно премахнато от вашия компютър.

# Install hook failed (install.rs)
install-hook-header = Инсталирането завърши частично
install-hook-body = Инсталирането завърши, но някои стъпки може да не са успели. Ако приложението не работи правилно, можете да опитате да го инсталирате отново или да се свържете с автора на приложението.

# Splash fallback (splash.rs)
splash-header = Инсталиране на { $app_title }
splash-body = Настройване на { $app_title } { $app_version }, моля, изчакайте...

# Dependency download (prerequisite.rs)
deps-download-header = Изтегляне на необходимия компонент
deps-download-body = Изтегляне на { $dep_name }, моля, изчакайте...

# Apply progress (apply_*_impl.rs)
apply-header = Инсталиране на актуализация
apply-body = Актуализиране до версия { $app_version }, моля, изчакайте...

# Start error (start_windows_impl.rs)
start-corrupt-header = Инсталацията е повредена
start-corrupt-body = Това приложение не може да се стартира, защото някои от файловете му липсват или са повредени. Преинсталирайте приложението, за да отстраните този проблем.

# Generic error
error-header = Нещо се обърка

# Setup error (wix msi)
setup-error-header = Инсталирането не може да продължи
setup-disk-space-insufficient = { $app_title } requires at least { $required_space } disk space to be installed. There is only { $available_space } available.

# MSI Installer UI - Common
msi-dlg-title = Инсталиране на { $app_title }
msi-btn-back = &Назад
msi-btn-next = Н&апред
msi-btn-cancel = Отказ
msi-btn-finish = &Готово
msi-btn-ok = OK
msi-btn-yes = &Да
msi-btn-no = &Не
msi-btn-retry = &Опитай пак
msi-btn-ignore = &Игнорирай

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Добре дошли в съветника за инсталиране на { $app_title }
msi-welcome-description = Съветникът за инсталиране ще инсталира { $app_title } на компютъра. Щракнете върху "Напред", за да продължите, или върху "Отказ", за да излезете от съветника за инсталиране.
msi-welcome-update-description = Съветникът за инсталиране ще актуализира { $app_title } на компютъра. Щракнете върху "Напред", за да продължите, или върху "Отказ", за да излезете от съветника за инсталиране.

# MSI Installer UI - Exit Dialog
msi-exit-title = Съветникът за инсталиране на { $app_title } завърши
msi-exit-description = Щракнете върху бутона "Готово", за да излезете от съветника за инсталиране.
msi-exit-launch-checkbox = Стартирай { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Добре дошли в съветника за инсталиране на { $app_title }
msi-prepare-description = Моля, изчакайте, докато съветникът за инсталиране се подготви да ви направлява в инсталирането.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Лицензионно споразумение с краен потребител
msi-license-description = Прочетете внимателно следното лицензионно споразумение.
msi-license-checkbox = &Приемам условията в лицензионното споразумение

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Обхват на инсталиране
msi-scope-description = Изберете обхвата на инсталиране.
msi-scope-per-user = Инсталиране &само за вас
msi-scope-per-machine = Инсталиране за &всички потребители
msi-scope-per-user-description = Инсталира само за текущия потребител
msi-scope-no-per-user-description = Изисква привилегии на администратор
msi-scope-per-machine-description = Изисква привилегии на администратор

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Всичко е готово за инсталиране на { $app_title }
msi-ready-install-text = Щракнете върху "Инсталирай", за да започнете инсталирането. Щракнете върху "Назад", за да прегледате или промените настройките за инсталиране.
msi-ready-change-title = Всичко е готово за промяна на { $app_title }
msi-ready-change-text = Щракнете върху "Промени", за да започнете промяната на инсталацията. Щракнете върху "Назад", за да прегледате или промените настройките за инсталиране.
msi-ready-repair-title = Всичко е готово за поправяне на { $app_title }
msi-ready-repair-text = Щракнете върху "Поправи", за да започнете поправянето. Щракнете върху "Назад", за да прегледате или промените настройките за инсталиране.
msi-ready-remove-title = Всичко е готово за премахване на { $app_title }
msi-ready-remove-text = Щракнете върху "Премахни", за да премахнете { $app_title } от компютъра си. Щракнете върху "Назад", за да прегледате или промените настройките за инсталиране.
msi-ready-update-title = Всичко е готово за актуализиране на { $app_title }
msi-ready-update-text = Щракнете върху "Актуализирай", за да започнете актуализирането. Щракнете върху "Назад", за да прегледате или промените настройките за инсталиране.
msi-ready-btn-install = &Инсталирай
msi-ready-btn-change = &Промени
msi-ready-btn-repair = Поп&рави
msi-ready-btn-remove = &Премахни
msi-ready-btn-update = &Актуализирай

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Инсталиране на { $app_title }
msi-progress-installing-text = Моля, изчакайте, докато съветникът за инсталиране инсталира { $app_title }.
msi-progress-changing-title = Промяна на { $app_title }
msi-progress-changing-text = Моля, изчакайте, докато съветникът за инсталиране промени { $app_title }.
msi-progress-repairing-title = Поправяне на { $app_title }
msi-progress-repairing-text = Моля, изчакайте, докато съветникът за инсталиране поправи { $app_title }.
msi-progress-removing-title = Премахване на { $app_title }
msi-progress-removing-text = Моля, изчакайте, докато съветникът за инсталиране премахне { $app_title }.
msi-progress-updating-title = Актуализиране на { $app_title }
msi-progress-updating-text = Моля, изчакайте, докато съветникът за инсталиране актуализира { $app_title }.
msi-progress-status = Състояние:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Добре дошли в съветника за инсталиране на { $app_title }
msi-maint-welcome-description = Съветникът за инсталиране ще ви позволи да поправите или премахнете { $app_title }. Щракнете върху "Напред", за да продължите, или върху "Отказ", за да излезете от съветника за инсталиране.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Промяна, поправяне или премахване на инсталация
msi-maint-type-description = Изберете операцията, която желаете да изпълните.
msi-maint-change-button = &Промени...
msi-maint-change-tooltip = Промени...
msi-maint-change-text = Позволява на потребителите да променят кои функции на програмата са инсталирани и да променят отделни функции.
msi-maint-change-disabled = Промяната в момента е забранена.
msi-maint-repair-button = Поп&рави
msi-maint-repair-tooltip = Поправи
msi-maint-repair-text = Поправя грешки в последната инсталация, като коригира липсващи и повредени файлове, преки пътища и записи в системния регистър.
msi-maint-repair-disabled = Поправянето в момента е забранено.
msi-maint-remove-button = &Премахни
msi-maint-remove-tooltip = Премахни
msi-maint-remove-text = Премахва { $app_title } от компютъра.
msi-maint-remove-disabled = Премахването в момента е забранено.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Наистина ли искате да отмените инсталирането на { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Промяна на текущата папка местоназначение
msi-browse-description = Отидете до папката местоназначение.
msi-browse-combo-label = &Търси в:
msi-browse-path-label = &Име на папката:
msi-browse-up-tooltip = Едно ниво нагоре
msi-browse-new-folder-tooltip = Създаване на нова папка

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Зададената директория местоназначение е невалидна или се намира на неподдържан тип устройство.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Изисквания за дисковото пространство
msi-disk-cost-description = Дисковото пространство, необходимо за инсталиране на избраните компоненти.
msi-disk-cost-text = Маркираните томове нямат достатъчно свободно място на диска за текущо избраните компоненти. Можете да премахнете някои файлове от маркираните томове, да инсталирате по-малко компоненти на локалния(те) диск(ове) или да изберете друго устройство за местоназначение.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Информация на инсталиращата програма { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Съветникът за инсталиране на { $app_title } завърши преждевременно
msi-fatal-description1 = Инсталирането на { $app_title } беше прекъснато. Системата ви не е променена. За да инсталирате тази програма по-късно, изпълнете отново инсталирането.
msi-fatal-description2 = Щракнете върху бутона "Готово", за да излезете от съветника за инсталиране.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Съветникът за инсталиране на { $app_title } беше прекъснат
msi-user-exit-description1 = Инсталирането на { $app_title } е прекъснато. Системата ви не е променена. За да инсталирате тази програма по-късно, изпълнете отново инсталирането.
msi-user-exit-description2 = Щракнете върху бутона "Готово", за да излезете от съветника за инсталиране.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Използвани в момента файлове
msi-files-in-use-description = Някои от файловете, които трябва да се актуализират, в момента се използват.
msi-files-in-use-text = Приложенията по-долу използват файлове, които трябва да се актуализират при това инсталиране. Затворете тези приложения, след което щракнете върху "Опитай пак", за да продължите инсталирането, или щракнете върху "Отказ", за да излезете от него.
msi-files-in-use-exit = И&зход

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Използвани в момента файлове
msi-rm-files-in-use-description = Някои от файловете, които трябва да се актуализират, в момента се използват.
msi-rm-files-in-use-text = Приложенията по-долу използват файлове, които трябва да се актуализират при това инсталиране. Можете да позволите на съветника за инсталиране да ги затвори автоматично и да опита да ги стартира отново след приключване на инсталирането или можете да ги затворите ръчно и да щракнете върху OK, за да продължите инсталирането.
msi-rm-files-in-use-use-rm = Автоматично &затвори приложенията и направи опит за рестартирането им след приключване на инсталирането.
msi-rm-files-in-use-dont-use-rm = &Не затваряй приложенията. (Ще се изисква рестартиране.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Възобновяване на съветника за инсталиране на { $app_title }
msi-resume-description = Съветникът за инсталиране ще завърши инсталирането на { $app_title } на компютъра. Щракнете върху "Инсталирай", за да продължите, или върху "Отказ", за изход от съветника за инсталиране.
msi-resume-btn-install = &Инсталирай

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Пряк път на работния плот за { $app_title }
msi-start-menu-shortcut-description = Пряк път в менюто "Старт" за { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Информация за прочит
msi-readme-description = Моля, прочетете следната информация, преди да продължите.
