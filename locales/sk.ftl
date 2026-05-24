# Shared titles
title-update = Aktualizácia { $app_title }
title-setup = Inštalácia { $app_title }
title-uninstall = Odinštalovanie { $app_title }
error-title = Chyba { $program_name }

# Shared buttons
btn-cancel = Zrušiť
btn-install-update = Inštalovať aktualizáciu
btn-install = Inštalovať
btn-update = Aktualizovať
btn-downgrade = Znížiť verziu
btn-repair = Opraviť
btn-open-log = Otvoriť denník
btn-open-install-dir = Otvoriť inštalačný adresár

# Elevation (dialogs_common.rs)
elevate-header = Vyžadované oprávnenia správcu
elevate-body = { $app_title } potrebuje na inštaláciu verzie { $app_version } oprávnenia správcu. Povoliť pokračovanie tejto aktualizácie?

# Restart required (prerequisite.rs)
restart-header = Vyžaduje sa reštart
restart-body = Pred pokračovaním inštalácie je potrebné reštartovať počítač. Reštartujte počítač a spustite inštaláciu znova.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Vyžadované ďalšie súčasti
missing-deps-body = Pre { $app_title } je najprv potrebné nainštalovať tieto súčasti: { $deps }. Chcete ich teraz prevziať a nainštalovať?

# Uninstall with errors (uninstall)
uninstall-errors-header = Odinštalácia dokončená s problémami
uninstall-errors-body = Aplikácia { $app_title } bola odinštalovaná, ale niektoré súbory alebo priečinky nebolo možné odstrániť. Môžete ich odstrániť ručne alebo aplikáciu opätovne nainštalovať a skúsiť odinštalovať znova.
uninstall-errors-log = Podrobnosti boli uložené do: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } je už nainštalovaný
overwrite-repair-body = Táto aplikácia je už v počítači nainštalovaná. Ak nepracuje správne, môžete ju skúsiť opraviť opätovnou inštaláciou.
overwrite-older-installed = { $app_title } je už nainštalovaný
overwrite-update-body = Aktuálne je nainštalovaná verzia { $old_version }. Chcete aktualizovať na verziu { $app_version }?
overwrite-newer-installed = Novšia verzia produktu { $app_title } je už nainštalovaná
overwrite-downgrade-body = Aktuálne je nainštalovaná verzia { $old_version }, ktorá je novšia ako tento inštalátor. Zníženie verzie sa neodporúča a môže spôsobiť problémy. Napriek tomu pokračovať?
overwrite-footer = Nainštalované v: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Odinštalácia dokončená
uninstall-body = Aplikácia bola úspešne odstránená z počítača.

# Install hook failed (install.rs)
install-hook-header = Inštalácia čiastočne úspešná
install-hook-body = Inštalácia bola dokončená, ale niektoré kroky sa nemuseli zdariť. Ak aplikácia nepracuje správne, môžete ju skúsiť znova nainštalovať alebo kontaktovať autora aplikácie.

# Splash fallback (splash.rs)
splash-header = Inštaluje sa { $app_title }
splash-body = Prebieha nastavovanie { $app_title } { $app_version }, počkajte prosím...

# Dependency download (prerequisite.rs)
deps-download-header = Preberá sa požadovaná súčasť
deps-download-body = Prebieha preberanie { $dep_name }, počkajte prosím...

# Apply progress (apply_*_impl.rs)
apply-header = Inštalácia aktualizácie
apply-body = Prebieha aktualizácia na verziu { $app_version }, počkajte prosím...

# Start error (start_windows_impl.rs)
start-corrupt-header = Inštalácia je poškodená
start-corrupt-body = Túto aplikáciu nie je možné spustiť, pretože niektoré jej súbory chýbajú alebo sú poškodené. Pre vyriešenie tohto problému aplikáciu znova nainštalujte.

# Generic error
error-header = Niečo sa pokazilo

# Setup error (wix msi)
setup-error-header = Inštaláciu nie je možné dokončiť

# MSI Installer UI - Common
msi-dlg-title = { $app_title } – inštalácia
msi-btn-back = &Späť
msi-btn-next = Ď&alej
msi-btn-cancel = Zrušiť
msi-btn-finish = &Dokončiť
msi-btn-ok = OK
msi-btn-yes = Án&o
msi-btn-no = &Nie
msi-btn-retry = Z&nova
msi-btn-ignore = &Ignorovať

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Víta vás Sprievodca inštaláciou programu { $app_title }
msi-welcome-description = Sprievodca inštaláciou nainštaluje program { $app_title } v počítači. Ak chcete pokračovať, kliknite na tlačidlo Ďalej. Ak chcete Sprievodcu inštaláciou ukončiť, kliknite na tlačidlo Zrušiť.
msi-welcome-update-description = Sprievodca inštaláciou vykoná aktualizáciu programu { $app_title } v počítači. Ak chcete pokračovať, kliknite na tlačidlo Ďalej. Ak chcete Sprievodcu inštaláciou ukončiť, kliknite na tlačidlo Zrušiť.

# MSI Installer UI - Exit Dialog
msi-exit-title = Sprievodca inštaláciou programu { $app_title } bol dokončený
msi-exit-description = Kliknutím na tlačidlo Dokončiť ukončíte Sprievodcu inštaláciou.
msi-exit-launch-checkbox = Spustiť { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Víta vás Sprievodca inštaláciou programu { $app_title }
msi-prepare-description = Počkajte, kým sa Sprievodca inštaláciou pripraví na prevádzanie inštaláciou.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Licenčná zmluva koncového používateľa
msi-license-description = Pozorne si prečítajte nasledujúcu licenčnú zmluvu.
msi-license-checkbox = &Súhlasím s podmienkami licenčnej zmluvy

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Rozsah inštalácie
msi-scope-description = Vyberte rozsah inštalácie.
msi-scope-per-user = Inštalovať len pre &seba
msi-scope-per-machine = Inštalovať pre &všetkých používateľov
msi-scope-per-user-description = Inštaluje iba pre aktuálneho používateľa
msi-scope-no-per-user-description = Vyžaduje oprávnenia správcu
msi-scope-per-machine-description = Vyžaduje oprávnenia správcu

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Pripravený na inštaláciu programu { $app_title }
msi-ready-install-text = Ak chcete spustiť inštaláciu, kliknite na tlačidlo Inštalovať. Ak chcete skontrolovať alebo zmeniť niektoré z nastavení inštalácie, kliknite na tlačidlo Späť.
msi-ready-change-title = Pripravený na zmenu programu { $app_title }
msi-ready-change-text = Ak chcete spustiť zmenu inštalácie, kliknite na tlačidlo Zmeniť. Ak chcete skontrolovať alebo zmeniť niektoré z nastavení inštalácie, kliknite na tlačidlo Späť.
msi-ready-repair-title = Pripravený na opravu programu { $app_title }
msi-ready-repair-text = Kliknutím na tlačidlo Opraviť spustíte opravu. Ak chcete skontrolovať alebo zmeniť niektoré z nastavení inštalácie, kliknite na tlačidlo Späť.
msi-ready-remove-title = Pripravený na odstránenie programu { $app_title }
msi-ready-remove-text = Kliknutím na tlačidlo Odstrániť odstránite program { $app_title } z počítača. Ak chcete skontrolovať alebo zmeniť niektoré z nastavení inštalácie, kliknite na tlačidlo Späť.
msi-ready-update-title = Pripravený na aktualizáciu programu { $app_title }
msi-ready-update-text = Kliknutím na tlačidlo Aktualizovať spustíte aktualizáciu. Ak chcete skontrolovať alebo zmeniť niektoré z nastavení inštalácie, kliknite na tlačidlo Späť.
msi-ready-btn-install = I&nštalovať
msi-ready-btn-change = Z&meniť
msi-ready-btn-repair = Opr&aviť
msi-ready-btn-remove = &Odstrániť
msi-ready-btn-update = &Aktualizovať

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Inštaluje sa program { $app_title }
msi-progress-installing-text = Počkajte, kým Sprievodca inštaláciou nainštaluje program { $app_title }.
msi-progress-changing-title = Mení sa program { $app_title }
msi-progress-changing-text = Počkajte, kým Sprievodca inštaláciou zmení program { $app_title }.
msi-progress-repairing-title = Opravuje sa program { $app_title }
msi-progress-repairing-text = Počkajte, kým Sprievodca inštaláciou opraví program { $app_title }.
msi-progress-removing-title = Odstraňuje sa program { $app_title }
msi-progress-removing-text = Počkajte, kým Sprievodca inštaláciou odstráni program { $app_title }.
msi-progress-updating-title = Aktualizuje sa program { $app_title }
msi-progress-updating-text = Počkajte, kým Sprievodca inštaláciou aktualizuje program { $app_title }.
msi-progress-status = Stav:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Víta vás Sprievodca inštaláciou programu { $app_title }
msi-maint-welcome-description = Sprievodca inštaláciou vám umožňuje opraviť alebo odstrániť program { $app_title }. Ak chcete pokračovať, kliknite na tlačidlo Ďalej. Ak chcete Sprievodcu inštaláciou ukončiť, kliknite na tlačidlo Zrušiť.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Zmena, oprava alebo odstránenie inštalácie
msi-maint-type-description = Vyberte operáciu, ktorú chcete vykonať.
msi-maint-change-button = Z&meniť...
msi-maint-change-tooltip = Zmeniť...
msi-maint-change-text = Umožňuje používateľom zmeniť, ktoré súčasti programu sú nainštalované, a zmeniť jednotlivé súčasti.
msi-maint-change-disabled = Zmena je momentálne zakázaná.
msi-maint-repair-button = Opr&aviť
msi-maint-repair-tooltip = Opraviť
msi-maint-repair-text = Slúži na opravu chýb poslednej inštalácie prostredníctvom opravy chýbajúcich a poškodených súborov, odkazov a položiek databázy Registry.
msi-maint-repair-disabled = Oprava je momentálne zakázaná.
msi-maint-remove-button = &Odstrániť
msi-maint-remove-tooltip = Odstrániť
msi-maint-remove-text = Odstráni program { $app_title } z počítača.
msi-maint-remove-disabled = Odstránenie je momentálne zakázané.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Naozaj chcete zrušiť inštaláciu programu { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Zmena aktuálneho cieľového priečinka
msi-browse-description = Vyhľadať cieľový priečinok.
msi-browse-combo-label = &Kde hľadať:
msi-browse-path-label = &Názov priečinka:
msi-browse-up-tooltip = O úroveň vyššie
msi-browse-new-folder-tooltip = Vytvoriť nový priečinok

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Zadaný cieľový adresár je neplatný alebo sa nachádza na nepodporovanom type jednotky.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Požiadavky na miesto na disku
msi-disk-cost-description = Požadované miesto na disku na inštaláciu vybratých súčastí.
msi-disk-cost-text = Označené zväzky nemajú dostatok miesta na disku na aktuálne vybraté súčasti. Môžete odstrániť niektoré súbory z vyznačených zväzkov, nainštalovať menej súčastí na lokálnu jednotku alebo vybrať iné cieľové jednotky.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } – informácie inštalátora

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Sprievodca inštaláciou programu { $app_title } sa predčasne ukončil
msi-fatal-description1 = Inštalácia programu { $app_title } sa prerušila. Nastavenie systému sa nezmenilo. Ak budete chcieť tento program nainštalovať neskôr, znova spustite inštaláciu.
msi-fatal-description2 = Kliknutím na tlačidlo Dokončiť ukončíte Sprievodcu inštaláciou.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Sprievodca inštaláciou programu { $app_title } sa prerušil
msi-user-exit-description1 = Inštalácia programu { $app_title } sa prerušila. Nastavenie systému sa nezmenilo. Ak budete chcieť tento program nainštalovať neskôr, znova spustite inštaláciu.
msi-user-exit-description2 = Kliknutím na tlačidlo Dokončiť ukončíte Sprievodcu inštaláciou.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Používané súbory
msi-files-in-use-description = Niektoré súbory určené na aktualizáciu sa momentálne používajú.
msi-files-in-use-text = Nasledujúce aplikácie používajú súbory, ktoré musí táto inštalácia aktualizovať. Zavrite tieto aplikácie, kliknite na tlačidlo Znova a pokračujte v inštalácii. Ak chcete skončiť, kliknite na tlačidlo Zrušiť.
msi-files-in-use-exit = Sk&ončiť

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Používané súbory
msi-rm-files-in-use-description = Niektoré súbory určené na aktualizáciu sa momentálne používajú.
msi-rm-files-in-use-text = Nasledujúce aplikácie používajú súbory, ktoré musí táto inštalácia aktualizovať. Môžete Sprievodcovi inštaláciou povoliť, aby ich automaticky zavrel a po dokončení inštalácie sa pokúsil znova spustiť, alebo ich môžete zavrieť ručne a kliknutím na tlačidlo OK pokračovať v inštalácii.
msi-rm-files-in-use-use-rm = Automaticky &zavrieť aplikácie a po dokončení inštalácie sa ich pokúsiť spustiť znova.
msi-rm-files-in-use-dont-use-rm = &Nezatvárať aplikácie. (Bude sa vyžadovať reštartovanie počítača.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Obnovuje sa Sprievodca inštaláciou programu { $app_title }
msi-resume-description = Sprievodca inštaláciou dokončí inštaláciu programu { $app_title } v počítači. Ak chcete pokračovať, kliknite na tlačidlo Inštalovať. Ak chcete Sprievodcu inštaláciou ukončiť, kliknite na tlačidlo Zrušiť.
msi-resume-btn-install = I&nštalovať

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Odkaz na pracovnej ploche pre { $app_title }
msi-start-menu-shortcut-description = Odkaz v ponuke Štart pre { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Dôležité informácie
msi-readme-description = Pred pokračovaním si prečítajte nasledujúce informácie.
