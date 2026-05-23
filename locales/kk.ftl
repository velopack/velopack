# Shared titles
title-update = { $app_title } жаңартуы
title-setup = { $app_title } орнатуы
title-uninstall = { $app_title } жою
error-title = { $program_name } қатесі

# Shared buttons
btn-cancel = Болдырмау
btn-install-update = Жаңартуды орнату
btn-install = Орнату
btn-update = Жаңарту
btn-downgrade = Төмен нұсқаға өзгерту
btn-repair = Қалпына келтіру
btn-open-log = Журналды ашу
btn-open-install-dir = Орнату қалтасын ашу

# Elevation (dialogs_common.rs)
elevate-header = Әкімші рұқсаты қажет
elevate-body = { $app_title } { $app_version } нұсқасын орнату үшін әкімші рұқсатын қажет етеді. Бұл жаңартуды жалғастыруға рұқсат бере ме?

# Restart required (prerequisite.rs)
restart-header = Қайта қотару қажет
restart-body = Орнату жалғастырылмас бұрын компьютеріңізді қайта қотару қажет. Компьютеріңізді қайта қотарып, орнатуды қайтадан іске қосыңыз.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Қосымша компоненттер қажет
missing-deps-body = { $app_title } үшін алдымен мыналарды орнату қажет: { $deps }. Оларды қазір жүктеп алып орнатуды қалайсыз ба?

# Uninstall with errors (uninstall)
uninstall-errors-header = Жою қиындықтармен аяқталды
uninstall-errors-body = { $app_title } жойылды, бірақ кейбір файлдар немесе қалталар жойылмады. Оларды қолмен жоюға немесе қолданбаны қайта орнатып, жоюды қайтадан байқап көруге болады.
uninstall-errors-log = Толық мәлімет мұнда сақталды: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } әлдеқашан орнатылған
overwrite-repair-body = Бұл қолданба компьютеріңізге әлдеқашан орнатылған. Егер ол дұрыс жұмыс істемесе, оны қайта орнату арқылы қалпына келтіріп көруге болады.
overwrite-older-installed = { $app_title } әлдеқашан орнатылған
overwrite-update-body = Қазіргі уақытта { $old_version } нұсқасы орнатылған. { $app_version } нұсқасына жаңартуды қалайсыз ба?
overwrite-newer-installed = { $app_title } бағдарламасының жаңа нұсқасы әлдеқашан орнатылған
overwrite-downgrade-body = Қазіргі уақытта осы орнатушыдан жаңарақ болатын { $old_version } нұсқасы орнатылған. Төмен нұсқаға өзгертуге ұсынылмайды және мәселелер тудыруы мүмкін. Бәрібір жалғастыру керек пе?
overwrite-footer = Мұнда орнатылған: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Жою аяқталды
uninstall-body = Қолданба компьютеріңізден сәтті жойылды.

# Install hook failed (install.rs)
install-hook-header = Орнату жартылай сәтті болды
install-hook-body = Орнату аяқталды, бірақ кейбір қадамдар сәтсіз аяқталған болуы мүмкін. Қолданба дұрыс жұмыс істемесе, қайта орнатып көруге немесе қолданба авторына хабарласуға болады.

# Splash fallback (splash.rs)
splash-header = { $app_title } орнатылуда
splash-body = { $app_title } { $app_version } реттелуде, күте тұрыңыз…

# Dependency download (prerequisite.rs)
deps-download-header = Қажетті компонент жүктелуде
deps-download-body = { $dep_name } жүктелуде, күте тұрыңыз…

# Apply progress (apply_*_impl.rs)
apply-header = Жаңарту орнатылуда
apply-body = { $app_version } нұсқасына жаңартылуда, күте тұрыңыз…

# Start error (start_windows_impl.rs)
start-corrupt-header = Орнату бұзылған
start-corrupt-body = Бұл қолданба кейбір файлдары жоқ немесе бұзылғандықтан іске қосылмайды. Бұл мәселені шешу үшін қолданбаны қайта орнатыңыз.

# Generic error
error-header = Бірдеңе дұрыс болмады

# Setup error (wix msi)
setup-error-header = Орнату жалғастырыла алмады

# MSI Installer UI - Common
msi-dlg-title = { $app_title } бағдарламасын орнату
msi-btn-back = &Артқа
msi-btn-next = &Келесі
msi-btn-cancel = Болдырмау
msi-btn-finish = &Аяқтау
msi-btn-ok = OK
msi-btn-yes = &Иә
msi-btn-no = &Жоқ
msi-btn-retry = &Қайталау
msi-btn-ignore = &Елемеу

# MSI Installer UI - Welcome Dialog
msi-welcome-title = { $app_title } бағдарламасының орнату шеберіне қош келдіңіз
msi-welcome-description = Орнату шебері компьютеріңізге { $app_title } бағдарламасын орнатады. Жалғастыру үшін «Келесі» түймешігін немесе орнату шеберінен шығу үшін «Болдырмау» түймешігін басыңыз.
msi-welcome-update-description = Орнату шебері компьютеріңіздегі { $app_title } бағдарламасын жаңартады. Жалғастыру үшін «Келесі» түймешігін немесе орнату шеберінен шығу үшін «Болдырмау» түймешігін басыңыз.

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } бағдарламасының орнату шебері жұмысын аяқтады
msi-exit-description = Орнату шеберінен шығу үшін «Аяқтау» түймешігін басыңыз.
msi-exit-launch-checkbox = { $app_title } іске қосу

# MSI Installer UI - Prepare Dialog
msi-prepare-title = { $app_title } бағдарламасының орнату шеберіне қош келдіңіз
msi-prepare-description = Орнату шебері орнату барысында бағыт беруге дайын болғанша күтіңіз.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Соңғы тұтынушысына арналған лицензиялық келісім
msi-license-description = Төмендегі лицензиялық келісімнің шарттарын мұқият оқып шығыңыз.
msi-license-checkbox = Мен лицензиялық келісімнің шарттарын &қабылдаймын

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Орнату ауқымы
msi-scope-description = Орнату ауқымын таңдаңыз.
msi-scope-per-user = &Тек өзіңіз үшін орнату
msi-scope-per-machine = &Барлық пайдаланушылар үшін орнату
msi-scope-per-user-description = Тек ағымдағы пайдаланушы үшін орнатылады
msi-scope-no-per-user-description = Әкімші құқықтары қажет
msi-scope-per-machine-description = Әкімші құқықтары қажет

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = { $app_title } бағдарламасын орнатуға дайын
msi-ready-install-text = Орнатуды бастау үшін «Орнату» түймешігін басыңыз. Сараптау немесе кез келген орнату параметрлерін өзгерту үшін «Артқа» түймешігін басыңыз.
msi-ready-change-title = { $app_title } бағдарламасын өзгертуге дайын
msi-ready-change-text = Орнатуды өзгертуді бастау үшін «Өзгерту» түймешігін басыңыз. Сараптау немесе кез келген орнату параметрлерін өзгерту үшін «Артқа» түймешігін басыңыз.
msi-ready-repair-title = { $app_title } бағдарламасын қалпына келтіруге дайын
msi-ready-repair-text = Қалпына келтіруді бастау үшін «Қалпына келтіру» түймешігін басыңыз. Сараптау немесе кез келген орнату параметрлерін өзгерту үшін «Артқа» түймешігін басыңыз.
msi-ready-remove-title = { $app_title } бағдарламасын жоюға дайын
msi-ready-remove-text = { $app_title } бағдарламасын компьютеріңізден жою үшін «Жою» түймешігін басыңыз. Сараптау немесе кез келген орнату параметрлерін өзгерту үшін «Артқа» түймешігін басыңыз.
msi-ready-update-title = { $app_title } бағдарламасын жаңартуға дайын
msi-ready-update-text = Жаңартуды бастау үшін «Жаңарту» түймешігін басыңыз. Сараптау немесе кез келген орнату параметрлерін өзгерту үшін «Артқа» түймешігін басыңыз.
msi-ready-btn-install = &Орнату
msi-ready-btn-change = &Өзгерту
msi-ready-btn-repair = Қа&лпына келтіру
msi-ready-btn-remove = &Жою
msi-ready-btn-update = &Жаңарту

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } бағдарламасы орнатылуда
msi-progress-installing-text = Орнату шебері { $app_title } бағдарламасын орнатқанға дейін күте тұрыңыз.
msi-progress-changing-title = { $app_title } бағдарламасы өзгертілуде
msi-progress-changing-text = Орнату шебері { $app_title } бағдарламасын өзгерткенге дейін күте тұрыңыз.
msi-progress-repairing-title = { $app_title } бағдарламасы қалпына келтірілуде
msi-progress-repairing-text = Орнату шебері { $app_title } бағдарламасын қалпына келтіргенге дейін күте тұрыңыз.
msi-progress-removing-title = { $app_title } бағдарламасы жойылуда
msi-progress-removing-text = Орнату шебері { $app_title } бағдарламасын жойғанға дейін күте тұрыңыз.
msi-progress-updating-title = { $app_title } бағдарламасы жаңартылуда
msi-progress-updating-text = Орнату шебері { $app_title } бағдарламасын жаңартқанға дейін күте тұрыңыз.
msi-progress-status = Күйі:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = { $app_title } бағдарламасының орнату шеберіне қош келдіңіз
msi-maint-welcome-description = Орнату шебері { $app_title } бағдарламасын қалпына келтіруге немесе жоюға мүмкіндік береді. Жалғастыру үшін «Келесі» түймешігін немесе орнату шеберінен шығу үшін «Болдырмау» түймешігін басыңыз.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Орнатуды өзгерту, қалпына келтіру немесе жою
msi-maint-type-description = Орындағыңыз келген әрекетті таңдаңыз.
msi-maint-change-button = &Өзгерту...
msi-maint-change-tooltip = Өзгерту...
msi-maint-change-text = Пайдаланушыларға қандай бағдарлама мүмкіндіктерінің орнатылғанын өзгертуге және жекелеген мүмкіндіктерді өзгертуге мүмкіндік береді.
msi-maint-change-disabled = Өзгерту қазір өшірілген.
msi-maint-repair-button = Қа&лпына келтіру
msi-maint-repair-tooltip = Қалпына келтіру
msi-maint-repair-text = Ең соңғы орнатудағы қателерді қалпына келтіреді - жоқ немесе бұзылған файлдарды, ендер мен тізбе жазбаларын түзетеді.
msi-maint-repair-disabled = Қалпына келтіру қазір өшірілген.
msi-maint-remove-button = &Жою
msi-maint-remove-tooltip = Жою
msi-maint-remove-text = { $app_title } бағдарламасын компьютеріңізден жояды.
msi-maint-remove-disabled = Жою қазір өшірілген.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Шынымен { $app_title } бағдарламасын орнатудан бас тартқыңыз келе ме?

# MSI Installer UI - Browse Dialog
msi-browse-title = Ағымдағы тағайындау қалтасын өзгерту
msi-browse-description = Тағайындалған қалтаны шолу.
msi-browse-combo-label = &Іздеу:
msi-browse-path-label = &Қалта атауы:
msi-browse-up-tooltip = Бір деңгей жоғары
msi-browse-new-folder-tooltip = Жаңа қалта жасау

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Көрсетілген тағайындау каталогы жарамсыз немесе қолдау көрсетілмейтін диск түрінде орналасқан.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Дискіде қажет етілетін бос орын
msi-disk-cost-description = Таңдалған мүмкіндіктерді орнату үшін дискіде қажет етілетін бос орын.
msi-disk-cost-text = Бөлектелген мәндерде ағымдағы таңдалған мүмкіндіктер үшін дискіде жеткілікті бос орын жоқ. Бөлектелген мәндерден кейбір файлдарды жоюға, жергілікті дискілерге азырақ мүмкіндіктер орнатуға немесе басқа тағайындау дискілерін таңдауға болады.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } орнатушысы туралы ақпарат

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } бағдарламасының орнату шебері жұмысын мерзімінен бұрын аяқтады
msi-fatal-description1 = { $app_title } бағдарламасын орнату үзілді. Жүйеңіз өзгертілген жоқ. Бұл бағдарламаны кейінірек орнату үшін, орнатуды қайтадан іске қосыңыз.
msi-fatal-description2 = Орнату шеберінен шығу үшін «Аяқтау» түймешігін басыңыз.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } бағдарламасының орнату шеберінің жұмысы үзілді
msi-user-exit-description1 = { $app_title } бағдарламасын орнату үзілді. Жүйеңіз өзгертілген жоқ. Бұл бағдарламаны кейінірек орнату үшін, орнатуды қайтадан іске қосыңыз.
msi-user-exit-description2 = Орнату шеберінен шығу үшін «Аяқтау» түймешігін басыңыз.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Қолданыстағы файлдар
msi-files-in-use-description = Жаңартылуы қажет кейбір файлдар қазір қолданыста.
msi-files-in-use-text = Бұл орнату жаңартуы керек файлдарды келесі бағдарламалар пайдалануда. Бұл бағдарламаларды жабыңыз да, орнатуды жалғастыру үшін «Қайталау» түймешігін немесе одан шығу үшін «Болдырмау» түймешігін басыңыз.
msi-files-in-use-exit = Ш&ығу

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Қолданыстағы файлдар
msi-rm-files-in-use-description = Жаңартылуы қажет кейбір файлдар қазір қолданыста.
msi-rm-files-in-use-text = Бұл орнату жаңартуы керек файлдарды келесі бағдарламалар пайдалануда. Орнату шеберіне оларды автоматты түрде жауып, қайта қотаруға мүмкіндік бере аласыз немесе оларды қолмен жауып, орнатуды жалғастыру үшін «OK» түймешігін баса аласыз.
msi-rm-files-in-use-use-rm = Бағдарламаларды автоматты түрде &жап және орнату аяқталғаннан кейін оларды қайта қотарып көру.
msi-rm-files-in-use-dont-use-rm = &Бағдарламаларды жаппаңыз. (Компьютерді қайта қотару қажет болады.)

# MSI Installer UI - Resume Dialog
msi-resume-title = { $app_title } бағдарламасының орнату шебері жалғастырылуда
msi-resume-description = Орнату шебері { $app_title } бағдарламасын компьютеріңізге орнатуды аяқтайды. Жалғастыру үшін «Орнату» түймешігін немесе орнату шеберінен шығу үшін «Болдырмау» түймешігін басыңыз.
msi-resume-btn-install = &Орнату

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } үшін жұмыс үстелі таңбашасы
msi-start-menu-shortcut-description = { $app_title } үшін Бастау мәзірі таңбашасы
