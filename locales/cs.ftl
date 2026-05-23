# Shared titles
title-update = Aktualizace { $app_title }
title-setup = Instalace { $app_title }
title-uninstall = Odinstalování { $app_title }
error-title = Chyba { $program_name }

# Shared buttons
btn-cancel = Storno
btn-install-update = Nainstalovat aktualizaci
btn-install = Nainstalovat
btn-update = Aktualizovat
btn-downgrade = Snížit verzi
btn-repair = Opravit
btn-open-log = Otevřít protokol
btn-open-install-dir = Otevřít instalační adresář

# Elevation (dialogs_common.rs)
elevate-header = Vyžadováno oprávnění správce
elevate-body = { $app_title } potřebuje oprávnění správce k instalaci verze { $app_version }. Povolit pokračování této aktualizace?

# Restart required (prerequisite.rs)
restart-header = Vyžadováno restartování
restart-body = Před pokračováním instalace je nutné restartovat počítač. Restartujte počítač a spusťte instalaci znovu.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Vyžadovány další součásti
missing-deps-body = Pro { $app_title } je nejprve nutné nainstalovat následující: { $deps }. Chcete je nyní stáhnout a nainstalovat?

# Uninstall with errors (uninstall)
uninstall-errors-header = Odinstalace dokončena s problémy
uninstall-errors-body = Produkt { $app_title } byl odinstalován, ale některé soubory nebo složky nebylo možné odebrat. Můžete je odstranit ručně, nebo aplikaci znovu nainstalovat a zkusit odinstalovat znovu.
uninstall-errors-log = Podrobnosti byly uloženy do: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } je již nainstalován
overwrite-repair-body = Tato aplikace je již nainstalována v počítači. Pokud nefunguje správně, můžete ji zkusit opravit opětovnou instalací.
overwrite-older-installed = { $app_title } je již nainstalován
overwrite-update-body = Aktuálně je nainstalována verze { $old_version }. Chcete aktualizovat na verzi { $app_version }?
overwrite-newer-installed = Novější verze produktu { $app_title } je již nainstalována
overwrite-downgrade-body = Aktuálně je nainstalována verze { $old_version }, která je novější než tento instalační program. Snížení verze se nedoporučuje a může způsobit problémy. Přesto pokračovat?
overwrite-footer = Nainstalováno v: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Odinstalace dokončena
uninstall-body = Aplikace byla úspěšně odebrána z počítače.

# Install hook failed (install.rs)
install-hook-header = Instalace částečně úspěšná
install-hook-body = Instalace byla dokončena, ale některé kroky se nemusely zdařit. Pokud aplikace nepracuje správně, můžete ji zkusit znovu nainstalovat nebo kontaktovat autora aplikace.

# Splash fallback (splash.rs)
splash-header = Instalace { $app_title }
splash-body = Probíhá nastavování { $app_title } { $app_version }, počkejte prosím...

# Dependency download (prerequisite.rs)
deps-download-header = Stahování požadované součásti
deps-download-body = Probíhá stahování { $dep_name }, počkejte prosím...

# Apply progress (apply_*_impl.rs)
apply-header = Instalace aktualizace
apply-body = Probíhá aktualizace na verzi { $app_version }, počkejte prosím...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalace je poškozená
start-corrupt-body = Tuto aplikaci nelze spustit, protože některé její soubory chybí nebo jsou poškozeny. Pro odstranění tohoto problému aplikaci znovu nainstalujte.

# Generic error
error-header = Něco se nepovedlo

# Setup error (wix msi)
setup-error-header = Instalaci nelze dokončit

# MSI Installer UI - Common
msi-dlg-title = Instalace produktu { $app_title }
msi-btn-back = &Zpět
msi-btn-next = &Další
msi-btn-cancel = Storno
msi-btn-finish = &Dokončit
msi-btn-ok = OK
msi-btn-yes = &Ano
msi-btn-no = &Ne
msi-btn-retry = &Opakovat
msi-btn-ignore = &Ignorovat

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Vítá vás Průvodce instalací produktu { $app_title }
msi-welcome-description = Průvodce instalací nainstaluje do počítače produkt { $app_title }. Pokračujte kliknutím na tlačítko Další, nebo kliknutím na tlačítko Storno Průvodce instalací ukončete.
msi-welcome-update-description = Průvodce instalací aktualizuje v počítači produkt { $app_title }. Pokračujte kliknutím na tlačítko Další, nebo kliknutím na tlačítko Storno Průvodce instalací ukončete.

# MSI Installer UI - Exit Dialog
msi-exit-title = Průvodce instalací produktu { $app_title } byl dokončen
msi-exit-description = Kliknutím na tlačítko Dokončit Průvodce instalací ukončete.
msi-exit-launch-checkbox = Spustit { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Vítá vás Průvodce instalací produktu { $app_title }
msi-prepare-description = Počkejte prosím, než se Průvodce instalací připraví na požadované kroky instalace.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Licenční smlouva s koncovým uživatelem
msi-license-description = Přečtěte si pečlivě následující licenční smlouvu.
msi-license-checkbox = &S podmínkami licenční smlouvy souhlasím

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Obor instalace
msi-scope-description = Vyberte obor instalace.
msi-scope-per-user = Nainstalovat &pouze pro vás
msi-scope-per-machine = Nainstalovat pro &všechny uživatele
msi-scope-per-user-description = Instaluje pouze pro aktuálního uživatele
msi-scope-no-per-user-description = Vyžaduje oprávnění správce
msi-scope-per-machine-description = Vyžaduje oprávnění správce

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Instalace produktu { $app_title } je připravena
msi-ready-install-text = Chcete-li zahájit instalaci, klikněte na tlačítko Nainstalovat. Jestliže chcete zkontrolovat nebo změnit nastavení instalace, klikněte na tlačítko Zpět.
msi-ready-change-title = Změna produktu { $app_title } je připravena
msi-ready-change-text = Chcete-li zahájit změnu instalace, klikněte na tlačítko Změnit. Jestliže chcete zkontrolovat nebo změnit nastavení instalace, klikněte na tlačítko Zpět.
msi-ready-repair-title = Oprava produktu { $app_title } je připravena
msi-ready-repair-text = Chcete-li zahájit opravu, klikněte na tlačítko Opravit. Jestliže chcete zkontrolovat nebo změnit nastavení instalace, klikněte na tlačítko Zpět.
msi-ready-remove-title = Odebrání produktu { $app_title } je připraveno
msi-ready-remove-text = Chcete-li odebrat produkt { $app_title } z počítače, klikněte na tlačítko Odebrat. Jestliže chcete zkontrolovat nebo změnit nastavení instalace, klikněte na tlačítko Zpět.
msi-ready-update-title = Připraveno k aktualizaci produktu { $app_title }
msi-ready-update-text = Chcete-li zahájit aktualizaci, klikněte na tlačítko Aktualizovat. Jestliže chcete zkontrolovat nebo změnit nastavení instalace, klikněte na tlačítko Zpět.
msi-ready-btn-install = &Nainstalovat
msi-ready-btn-change = &Změnit
msi-ready-btn-repair = O&pravit
msi-ready-btn-remove = &Odebrat
msi-ready-btn-update = &Aktualizovat

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Instalace produktu { $app_title }
msi-progress-installing-text = Počkejte prosím, než Průvodce instalací nainstaluje produkt { $app_title }.
msi-progress-changing-title = Změna produktu { $app_title }
msi-progress-changing-text = Počkejte prosím, než Průvodce instalací změní produkt { $app_title }.
msi-progress-repairing-title = Oprava produktu { $app_title }
msi-progress-repairing-text = Počkejte prosím, než Průvodce instalací opraví produkt { $app_title }.
msi-progress-removing-title = Odebírání produktu { $app_title }
msi-progress-removing-text = Počkejte prosím, než Průvodce instalací odebere produkt { $app_title }.
msi-progress-updating-title = Aktualizace produktu { $app_title }
msi-progress-updating-text = Počkejte prosím, než Průvodce instalací aktualizuje produkt { $app_title }.
msi-progress-status = Stav:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Vítá vás Průvodce instalací produktu { $app_title }
msi-maint-welcome-description = Pomocí Průvodce instalací můžete opravit nebo odebrat produkt { $app_title }. Pokračujte kliknutím na tlačítko Další, nebo kliknutím na tlačítko Storno Průvodce instalací ukončete.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Změna, oprava nebo odebrání instalace
msi-maint-type-description = Vyberte operaci, kterou chcete provést.
msi-maint-change-button = &Změnit...
msi-maint-change-tooltip = Změnit...
msi-maint-change-text = Umožňuje uživatelům změnit, které součásti programu jsou nainstalovány, a změnit jednotlivé součásti.
msi-maint-change-disabled = Změna je v současné době zakázána.
msi-maint-repair-button = O&pravit
msi-maint-repair-tooltip = Opravit
msi-maint-repair-text = Opraví chyby v nejnovější instalaci opravou chybějících a poškozených souborů, zástupců a položek registru.
msi-maint-repair-disabled = Oprava je v současné době zakázána.
msi-maint-remove-button = &Odebrat
msi-maint-remove-tooltip = Odebrat
msi-maint-remove-text = Odebere z počítače produkt { $app_title }.
msi-maint-remove-disabled = Odebrání je v současné době zakázáno.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Opravdu chcete zrušit instalaci produktu { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Změnit aktuální cílovou složku
msi-browse-description = Umožňuje přejít do cílové složky.
msi-browse-combo-label = &Oblast hledání:
msi-browse-path-label = &Název složky:
msi-browse-up-tooltip = O úroveň výš
msi-browse-new-folder-tooltip = Vytvořit novou složku

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Zadaný cílový adresář je neplatný nebo se nachází na nepodporovaném typu jednotky.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Požadavky na místo na disku
msi-disk-cost-description = Místo na disku potřebné k instalaci vybraných součástí.
msi-disk-cost-text = Na zvýrazněných svazcích není dostatek místa pro aktuálně vybrané součásti. Můžete odebrat některé soubory ze zvýrazněných svazků, nainstalovat méně součástí na místní disk(y) nebo vybrat jiné cílové jednotky.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Instalátor produktu { $app_title } – informace

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Průvodce instalací produktu { $app_title } byl předčasně ukončen
msi-fatal-description1 = Instalace produktu { $app_title } byla přerušena. Systém nebyl změněn. Chcete-li tento program nainstalovat později, spusťte znovu instalaci.
msi-fatal-description2 = Kliknutím na tlačítko Dokončit Průvodce instalací ukončete.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Průvodce instalací produktu { $app_title } byl přerušen
msi-user-exit-description1 = Instalace produktu { $app_title } byla přerušena. Systém nebyl změněn. Chcete-li tento program nainstalovat později, spusťte znovu instalaci.
msi-user-exit-description2 = Kliknutím na tlačítko Dokončit Průvodce instalací ukončete.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Používané soubory
msi-files-in-use-description = Některé soubory, které je třeba aktualizovat, jsou právě používány.
msi-files-in-use-text = Následující aplikace používají soubory, které je třeba při instalaci aktualizovat. Ukončete tyto aplikace a pokračujte v instalaci kliknutím na tlačítko Opakovat, nebo kliknutím na tlačítko Storno instalaci ukončete.
msi-files-in-use-exit = &Konec

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Používané soubory
msi-rm-files-in-use-description = Některé soubory, které je třeba aktualizovat, jsou právě používány.
msi-rm-files-in-use-text = Následující aplikace používají soubory, které je třeba při instalaci aktualizovat. Můžete nechat Průvodce instalací, aby je automaticky zavřel a pokusil se je po dokončení instalace znovu spustit, nebo je můžete zavřít ručně a kliknout na OK pro pokračování v instalaci.
msi-rm-files-in-use-use-rm = Automaticky &ukončit aplikace a pokusit se je po dokončení instalace znovu spustit.
msi-rm-files-in-use-dont-use-rm = &Neukončovat aplikace. (Bude nutné restartovat počítač.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Pokračování Průvodce instalací produktu { $app_title }
msi-resume-description = Průvodce instalací dokončí instalaci produktu { $app_title } do počítače. Pokračujte kliknutím na tlačítko Nainstalovat, nebo kliknutím na tlačítko Storno ukončete Průvodce instalací.
msi-resume-btn-install = &Nainstalovat

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Zástupce { $app_title } na ploše
msi-start-menu-shortcut-description = Zástupce { $app_title } v nabídce Start
