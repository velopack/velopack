# Shared titles
title-update = Aktualizacja { $app_title }
title-setup = Instalacja { $app_title }
title-uninstall = Dezinstalacja { $app_title }
error-title = Błąd { $program_name }

# Shared buttons
btn-cancel = Anuluj
btn-install-update = Zainstaluj aktualizację
btn-install = Zainstaluj
btn-update = Aktualizuj
btn-downgrade = Obniż wersję
btn-repair = Napraw
btn-open-log = Otwórz dziennik
btn-open-install-dir = Otwórz katalog instalacji

# Elevation (dialogs_common.rs)
elevate-header = Wymagane uprawnienia administratora
elevate-body = { $app_title } wymaga uprawnień administratora do zainstalowania wersji { $app_version }. Zezwolić na kontynuowanie tej aktualizacji?

# Restart required (prerequisite.rs)
restart-header = Wymagane ponowne uruchomienie
restart-body = Komputer musi zostać ponownie uruchomiony, zanim instalacja będzie mogła być kontynuowana. Uruchom ponownie komputer i ponownie uruchom instalację.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Wymagane dodatkowe składniki
missing-deps-body = { $app_title } wymaga wcześniejszego zainstalowania następujących składników: { $deps }. Czy chcesz je teraz pobrać i zainstalować?

# Uninstall with errors (uninstall)
uninstall-errors-header = Dezinstalacja zakończona z problemami
uninstall-errors-body = Aplikacja { $app_title } została odinstalowana, ale niektórych plików lub folderów nie udało się usunąć. Możesz usunąć je ręcznie lub zainstalować aplikację ponownie i spróbować odinstalować ją jeszcze raz.
uninstall-errors-log = Szczegóły zapisano w: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } jest już zainstalowane
overwrite-repair-body = Ta aplikacja jest już zainstalowana na komputerze. Jeśli nie działa poprawnie, możesz spróbować ją naprawić przez ponowną instalację.
overwrite-older-installed = { $app_title } jest już zainstalowane
overwrite-update-body = Obecnie zainstalowana jest wersja { $old_version }. Czy chcesz zaktualizować do wersji { $app_version }?
overwrite-newer-installed = Nowsza wersja { $app_title } jest już zainstalowana
overwrite-downgrade-body = Obecnie zainstalowana jest wersja { $old_version }, która jest nowsza niż ten instalator. Obniżanie wersji nie jest zalecane i może powodować problemy. Kontynuować mimo to?
overwrite-footer = Zainstalowano w: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Dezinstalacja zakończona
uninstall-body = Aplikacja została pomyślnie usunięta z komputera.

# Install hook failed (install.rs)
install-hook-header = Instalacja zakończona częściowo
install-hook-body = Instalacja została zakończona, ale niektóre kroki mogły się nie powieść. Jeśli aplikacja nie działa poprawnie, możesz spróbować ponownie ją zainstalować lub skontaktować się z autorem aplikacji.

# Splash fallback (splash.rs)
splash-header = Instalowanie { $app_title }
splash-body = Konfigurowanie { $app_title } { $app_version }, proszę czekać...

# Dependency download (prerequisite.rs)
deps-download-header = Pobieranie wymaganego składnika
deps-download-body = Pobieranie { $dep_name }, proszę czekać...

# Apply progress (apply_*_impl.rs)
apply-header = Instalowanie aktualizacji
apply-body = Aktualizowanie do wersji { $app_version }, proszę czekać...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalacja uszkodzona
start-corrupt-body = Tej aplikacji nie można uruchomić, ponieważ niektóre z jej plików są brakujące lub uszkodzone. Zainstaluj aplikację ponownie, aby to naprawić.

# Generic error
error-header = Coś poszło nie tak

# Setup error (wix msi)
setup-error-header = Nie można kontynuować instalacji

# MSI Installer UI - Common
msi-dlg-title = Instalator produktu { $app_title }
msi-btn-back = &Wstecz
msi-btn-next = &Dalej
msi-btn-cancel = Anuluj
msi-btn-finish = &Zakończ
msi-btn-ok = OK
msi-btn-yes = &Tak
msi-btn-no = &Nie
msi-btn-retry = &Ponów próbę
msi-btn-ignore = &Ignoruj

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Kreator instalacji produktu { $app_title } — Zapraszamy!
msi-welcome-description = Kreator instalacji zainstaluje produkt { $app_title } na tym komputerze. Kliknij przycisk Dalej, aby kontynuować, lub Anuluj, aby zakończyć pracę Kreatora instalacji.
msi-welcome-update-description = Kreator instalacji zaktualizuje produkt { $app_title } na tym komputerze. Kliknij przycisk Dalej, aby kontynuować, lub Anuluj, aby zakończyć pracę Kreatora instalacji.

# MSI Installer UI - Exit Dialog
msi-exit-title = Kreator instalacji produktu { $app_title } ukończył pracę
msi-exit-description = Kliknij przycisk Zakończ, aby zakończyć pracę Kreatora instalacji.
msi-exit-launch-checkbox = Uruchom { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Kreator instalacji produktu { $app_title } — Zapraszamy!
msi-prepare-description = Zaczekaj, aż Kreator instalacji zakończy przygotowania do przeprowadzenia instalacji.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Umowa licencyjna użytkownika oprogramowania
msi-license-description = Przeczytaj uważnie poniższą umowę licencyjną.
msi-license-checkbox = &Akceptuję warunki Umowy licencyjnej

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Zakres instalacji
msi-scope-description = Wybierz zakres instalacji.
msi-scope-per-user = Zainstaluj tylko dla &siebie
msi-scope-per-machine = Zainstaluj dla &wszystkich użytkowników
msi-scope-per-user-description = Instaluje tylko dla bieżącego użytkownika
msi-scope-no-per-user-description = Wymaga uprawnień administratora
msi-scope-per-machine-description = Wymaga uprawnień administratora

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Wszystko gotowe do zainstalowania produktu { $app_title }
msi-ready-install-text = Kliknij przycisk Zainstaluj, aby rozpocząć instalację. Kliknij przycisk Wstecz, aby przejrzeć lub zmienić dowolne ustawienia instalacji.
msi-ready-change-title = Wszystko gotowe do wprowadzenia zmian w produkcie { $app_title }
msi-ready-change-text = Kliknij przycisk Zmień, aby rozpocząć wprowadzanie zmian w instalacji. Kliknij przycisk Wstecz, aby przejrzeć lub zmienić dowolne ustawienia instalacji.
msi-ready-repair-title = Wszystko gotowe do naprawienia produktu { $app_title }
msi-ready-repair-text = Kliknij przycisk Napraw, aby rozpocząć naprawę. Kliknij przycisk Wstecz, aby przejrzeć lub zmienić dowolne ustawienia instalacji.
msi-ready-remove-title = Wszystko gotowe do usunięcia produktu { $app_title }
msi-ready-remove-text = Kliknij przycisk Usuń, aby usunąć produkt { $app_title } z tego komputera. Kliknij przycisk Wstecz, aby przejrzeć lub zmienić dowolne ustawienia instalacji.
msi-ready-update-title = Wszystko gotowe do zaktualizowania produktu { $app_title }
msi-ready-update-text = Kliknij przycisk Aktualizuj, aby rozpocząć aktualizację. Kliknij przycisk Wstecz, aby przejrzeć lub zmienić dowolne ustawienia instalacji.
msi-ready-btn-install = &Zainstaluj
msi-ready-btn-change = Z&mień
msi-ready-btn-repair = &Napraw
msi-ready-btn-remove = &Usuń
msi-ready-btn-update = Akt&ualizuj

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Instalowanie produktu { $app_title }
msi-progress-installing-text = Czekaj, aż Kreator instalacji zainstaluje produkt { $app_title }.
msi-progress-changing-title = Wprowadzanie zmian w produkcie { $app_title }
msi-progress-changing-text = Czekaj, aż Kreator instalacji wprowadzi zmiany w produkcie { $app_title }.
msi-progress-repairing-title = Naprawianie produktu { $app_title }
msi-progress-repairing-text = Czekaj, aż Kreator instalacji naprawi produkt { $app_title }.
msi-progress-removing-title = Usuwanie produktu { $app_title }
msi-progress-removing-text = Czekaj, aż Kreator instalacji usunie produkt { $app_title }.
msi-progress-updating-title = Aktualizowanie produktu { $app_title }
msi-progress-updating-text = Czekaj, aż Kreator instalacji zaktualizuje produkt { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Kreator instalacji produktu { $app_title } — Zapraszamy!
msi-maint-welcome-description = Kreator instalacji pozwala naprawić lub usunąć produkt { $app_title }. Kliknij przycisk Dalej, aby kontynuować, lub Anuluj, aby zakończyć pracę Kreatora instalacji.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Zmienianie, naprawa lub usuwanie instalacji
msi-maint-type-description = Wybierz operację, którą chcesz wykonać.
msi-maint-change-button = Z&mień...
msi-maint-change-tooltip = Zmień...
msi-maint-change-text = Pozwala użytkownikom zmienić, które funkcje programu są zainstalowane, oraz zmienić poszczególne funkcje.
msi-maint-change-disabled = Zmiana jest obecnie wyłączona.
msi-maint-repair-button = &Napraw
msi-maint-repair-tooltip = Napraw
msi-maint-repair-text = Naprawia błędy w najnowszej instalacji — rozwiązuje problemy z brakującymi lub uszkodzonymi plikami, skrótami i wpisami rejestru.
msi-maint-repair-disabled = Naprawa jest obecnie wyłączona.
msi-maint-remove-button = &Usuń
msi-maint-remove-tooltip = Usuń
msi-maint-remove-text = Usuwa produkt { $app_title } z tego komputera.
msi-maint-remove-disabled = Usuwanie jest obecnie wyłączone.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Czy na pewno chcesz anulować instalację produktu { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Zmień bieżący folder docelowy
msi-browse-description = Przejdź do folderu docelowego.
msi-browse-combo-label = &Szukaj w:
msi-browse-path-label = Nazwa &folderu:
msi-browse-up-tooltip = Do góry o jeden poziom
msi-browse-new-folder-tooltip = Utwórz nowy folder

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Wskazany katalog docelowy jest nieprawidłowy lub znajduje się na nieobsługiwanym typie dysku.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Wymagane miejsce na dysku
msi-disk-cost-description = Miejsce na dysku wymagane do zainstalowania wybranych funkcji.
msi-disk-cost-text = Wyróżnione woluminy nie mają wystarczająco dużo dostępnego miejsca na dysku na obecnie wybrane funkcje. Możesz usunąć niektóre pliki z wyróżnionych woluminów, zainstalować mniej funkcji na dysk(i) lokalny(e) lub wybrać inne dyski docelowe.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } — informacje instalatora

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Kreator instalacji produktu { $app_title } przedwcześnie zakończył pracę
msi-fatal-description1 = Instalacja produktu { $app_title } została przerwana. System nie został zmodyfikowany. Aby zainstalować ten program później, ponownie uruchom instalację.
msi-fatal-description2 = Kliknij przycisk Zakończ, aby zakończyć pracę Kreatora instalacji.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Działanie Kreatora instalacji produktu { $app_title } zostało przerwane
msi-user-exit-description1 = Instalacja produktu { $app_title } została przerwana. System nie został zmodyfikowany. Aby zainstalować ten program później, ponownie uruchom instalację.
msi-user-exit-description2 = Kliknij przycisk Zakończ, aby zakończyć pracę Kreatora instalacji.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Pliki w użyciu
msi-files-in-use-description = Niektóre pliki wymagające aktualizacji są obecnie używane.
msi-files-in-use-text = Poniższe aplikacje korzystają z plików, które wymagają zaktualizowania za pomocą tego instalatora. Zamknij te aplikacje, a następnie kliknij przycisk Ponów próbę, aby kontynuować instalację, lub Anuluj, aby ją zakończyć.
msi-files-in-use-exit = Z&akończ

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Pliki w użyciu
msi-rm-files-in-use-description = Niektóre pliki wymagające aktualizacji są obecnie używane.
msi-rm-files-in-use-text = Poniższe aplikacje korzystają z plików, które wymagają zaktualizowania za pomocą tego instalatora. Możesz zezwolić, aby Kreator instalacji automatycznie zamknął te aplikacje i spróbował uruchomić je ponownie po zakończeniu instalacji, lub zamknąć je ręcznie i kliknąć OK, aby kontynuować instalację.
msi-rm-files-in-use-use-rm = Automatycznie &zamknij aplikacje i spróbuj uruchomić je ponownie po zakończeniu instalacji.
msi-rm-files-in-use-dont-use-rm = &Nie zamykaj aplikacji. (Wymagane będzie ponowne uruchomienie.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Wznawianie pracy Kreatora instalacji produktu { $app_title }
msi-resume-description = Kreator instalacji wykona instalację produktu { $app_title } na tym komputerze. Kliknij przycisk Zainstaluj, aby kontynuować, lub kliknij przycisk Anuluj, aby zakończyć pracę Kreatora instalacji.
msi-resume-btn-install = &Zainstaluj

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Skrót na pulpicie dla { $app_title }
msi-start-menu-shortcut-description = Skrót w menu Start dla { $app_title }
