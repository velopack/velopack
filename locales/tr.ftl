# Shared titles
title-update = { $app_title } Güncelleme
title-setup = { $app_title } Kurulumu
title-uninstall = { $app_title } Kaldırma
error-title = { $program_name } Hatası

# Shared buttons
btn-cancel = İptal
btn-install-update = Güncellemeyi Yükle
btn-install = Yükle
btn-update = Güncelle
btn-downgrade = Sürümü Düşür
btn-repair = Onar
btn-open-log = Günlüğü Aç
btn-open-install-dir = Yükleme Dizinini Aç
btn-ok = Tamam
# Elevation (dialogs_common.rs)
elevate-header = Yönetici İzni Gerekli
elevate-body = { $app_title }, { $app_version } sürümünü yüklemek için yönetici iznine ihtiyaç duyuyor. Bu güncellemenin devam etmesine izin verilsin mi?

# Restart required (prerequisite.rs)
restart-header = Yeniden Başlatma Gerekli
restart-body = Kurulum devam etmeden önce bilgisayarınızın yeniden başlatılması gerekiyor. Lütfen bilgisayarınızı yeniden başlatıp kurulumu tekrar çalıştırın.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Ek Bileşenler Gerekli
missing-deps-body = { $app_title } için önce şunların yüklenmesi gerekiyor: { $deps }. Bunları şimdi indirip yüklemek ister misiniz?

# Uninstall with errors (uninstall)
uninstall-errors-header = Kaldırma Sorunlarla Tamamlandı
uninstall-errors-body = { $app_title } kaldırıldı, ancak bazı dosya veya klasörler kaldırılamadı. Bunları el ile silebilir veya uygulamayı yeniden yükleyip kaldırmayı tekrar deneyebilirsiniz.
uninstall-errors-log = Ayrıntılar şuraya kaydedildi: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } zaten yüklü
overwrite-repair-body = Bu uygulama bilgisayarınızda zaten yüklü. Düzgün çalışmıyorsa, yeniden yükleyerek onarmayı deneyebilirsiniz.
overwrite-older-installed = { $app_title } zaten yüklü
overwrite-update-body = Şu anda { $old_version } sürümü yüklü. { $app_version } sürümüne güncellemek ister misiniz?
overwrite-newer-installed = { $app_title } ürününün daha yeni bir sürümü zaten yüklü
overwrite-downgrade-body = Şu anda { $old_version } sürümü yüklü, bu sürüm bu yükleyiciden daha yeni. Sürüm düşürme önerilmez ve sorunlara yol açabilir. Yine de devam edilsin mi?
overwrite-footer = Şu konuma yüklü: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Kaldırma Tamamlandı
uninstall-body = Uygulama bilgisayarınızdan başarıyla kaldırıldı.

# Install hook failed (install.rs)
install-hook-header = Yükleme Kısmen Başarılı
install-hook-body = Yükleme tamamlandı, ancak bazı adımlar başarısız olmuş olabilir. Uygulama düzgün çalışmıyorsa yeniden yüklemeyi deneyebilir veya uygulama yazarıyla iletişime geçebilirsiniz.

# Splash fallback (splash.rs)
splash-header = { $app_title } yükleniyor
splash-body = { $app_title } { $app_version } kuruluyor, lütfen bekleyin…

# Dependency download (prerequisite.rs)
deps-download-header = Gerekli Bileşen İndiriliyor
deps-download-body = { $dep_name } indiriliyor, lütfen bekleyin…

# Apply progress (apply_*_impl.rs)
apply-header = Güncelleme Yükleniyor
apply-body = { $app_version } sürümüne güncelleniyor, lütfen bekleyin…

# Start error (start_windows_impl.rs)
start-corrupt-header = Yükleme Hasarlı
start-corrupt-body = Bu uygulama, bazı dosyaları eksik veya bozuk olduğundan başlatılamıyor. Lütfen bu sorunu düzeltmek için uygulamayı yeniden yükleyin.

# Generic error
error-header = Bir Sorun Oluştu

# Setup error (wix msi)
setup-error-header = Kurulum Devam Edemedi
setup-disk-space-insufficient = { $app_title } requires at least { $required_space } disk space to be installed. There is only { $available_space } available.

# MSI Installer UI - Common
msi-dlg-title = { $app_title } Kurulumu
msi-btn-back = &Geri
msi-btn-next = İ&leri
msi-btn-cancel = İptal
msi-btn-finish = &Son
msi-btn-ok = Tamam
msi-btn-yes = &Evet
msi-btn-no = &Hayır
msi-btn-retry = Yeniden &Dene
msi-btn-ignore = &Yoksay

# MSI Installer UI - Welcome Dialog
msi-welcome-title = { $app_title } Kurulum Sihirbazı'na Hoş Geldiniz
msi-welcome-description = Kurulum Sihirbazı { $app_title } ürününü bilgisayarınıza yükleyecek. Devam etmek için İleri'yi, Kurulum Sihirbazı'ndan çıkmak içinse İptal'i tıklatın.
msi-welcome-update-description = Kurulum Sihirbazı { $app_title } ürününü bilgisayarınızda güncelleştirecek. Devam etmek için İleri'yi, Kurulum Sihirbazı'ndan çıkmak içinse İptal'i tıklatın.

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } Kurulum Sihirbazı tamamlandı
msi-exit-description = Kurulum Sihirbazı'ndan çıkmak için Son düğmesini tıklatın.
msi-exit-launch-checkbox = { $app_title } başlat

# MSI Installer UI - Prepare Dialog
msi-prepare-title = { $app_title } Kurulum Sihirbazı'na Hoş Geldiniz
msi-prepare-description = Kurulum Sihirbazı yükleme sırasında size yol göstermek için hazırlanırken lütfen bekleyin.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Son Kullanıcı Lisans Sözleşmesi
msi-license-description = Lütfen aşağıdaki lisans sözleşmesini dikkatle okuyun.
msi-license-checkbox = Lisans Sözleşmesi'nin koşullarını kabul &ediyorum

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Yükleme Kapsamı
msi-scope-description = Yükleme kapsamını seçin.
msi-scope-per-user = Yalnızca &sizin için yükle
msi-scope-per-machine = &Tüm kullanıcılar için yükle
msi-scope-per-user-description = Yalnızca geçerli kullanıcı için yükler
msi-scope-no-per-user-description = Yönetici ayrıcalıkları gerektirir
msi-scope-per-machine-description = Yönetici ayrıcalıkları gerektirir

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = { $app_title } ürünü yüklenmeye hazır
msi-ready-install-text = Yüklemeyi başlatmak için Yükle'yi tıklatın. Yükleme ayarlarınızdan herhangi birini gözden geçirmek veya değiştirmek için Geri'yi tıklatın.
msi-ready-change-title = { $app_title } ürünü değiştirilmeye hazır
msi-ready-change-text = Yükleme işlemini değiştirmeye başlamak için Değiştir'i tıklatın. Yükleme ayarlarınızdan herhangi birini gözden geçirmek veya değiştirmek için Geri'yi tıklatın.
msi-ready-repair-title = { $app_title } ürünü onarılmaya hazır
msi-ready-repair-text = Onarımı başlatmak için Onar'ı tıklatın. Yükleme ayarlarınızdan herhangi birini gözden geçirmek veya değiştirmek için Geri'yi tıklatın.
msi-ready-remove-title = { $app_title } ürünü kaldırılmaya hazır
msi-ready-remove-text = { $app_title } ürününü bilgisayarınızdan kaldırmak için Kaldır'ı tıklatın. Yükleme ayarlarınızdan herhangi birini gözden geçirmek veya değiştirmek için Geri'yi tıklatın.
msi-ready-update-title = { $app_title } ürünü güncellenmeye hazır
msi-ready-update-text = Güncellemeyi başlatmak için Güncelle'yi tıklatın. Yükleme ayarlarınızdan herhangi birini gözden geçirmek veya değiştirmek için Geri'yi tıklatın.
msi-ready-btn-install = Yü&kle
msi-ready-btn-change = &Değiştir
msi-ready-btn-repair = &Onar
msi-ready-btn-remove = &Kaldır
msi-ready-btn-update = &Güncelle

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } yükleniyor
msi-progress-installing-text = Kurulum Sihirbazı { $app_title } ürününü yüklerken lütfen bekleyin.
msi-progress-changing-title = { $app_title } değiştiriliyor
msi-progress-changing-text = Kurulum Sihirbazı { $app_title } ürününü değiştirirken lütfen bekleyin.
msi-progress-repairing-title = { $app_title } onarılıyor
msi-progress-repairing-text = Kurulum Sihirbazı { $app_title } ürününü onarırken lütfen bekleyin.
msi-progress-removing-title = { $app_title } kaldırılıyor
msi-progress-removing-text = Kurulum Sihirbazı { $app_title } ürününü kaldırırken lütfen bekleyin.
msi-progress-updating-title = { $app_title } güncelleniyor
msi-progress-updating-text = Kurulum Sihirbazı { $app_title } ürününü güncellerken lütfen bekleyin.
msi-progress-status = Durum:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = { $app_title } Kurulum Sihirbazı'na Hoş Geldiniz
msi-maint-welcome-description = Kurulum Sihirbazı { $app_title } ürününü onarmanıza veya kaldırmanıza olanak verir. Devam etmek için İleri'yi, Kurulum Sihirbazı'ndan çıkmak içinse İptal'i tıklatın.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Yüklemeyi değiştirin, onarın veya kaldırın
msi-maint-type-description = Gerçekleştirmek istediğiniz işlemi seçin.
msi-maint-change-button = &Değiştir...
msi-maint-change-tooltip = Değiştir...
msi-maint-change-text = Kullanıcıların hangi program özelliklerinin yüklendiğini değiştirmesine ve tek tek özellikleri değiştirmesine olanak verir.
msi-maint-change-disabled = Değiştirme şu anda devre dışı.
msi-maint-repair-button = &Onar
msi-maint-repair-tooltip = Onar
msi-maint-repair-text = En son yüklemedeki eksik veya bozuk dosyaları, kısayolları ve kayıt defteri girdilerini düzelterek hataları onarır.
msi-maint-repair-disabled = Onarım şu anda devre dışı.
msi-maint-remove-button = &Kaldır
msi-maint-remove-tooltip = Kaldır
msi-maint-remove-text = { $app_title } ürününü bilgisayarınızdan kaldırır.
msi-maint-remove-disabled = Kaldırma şu anda devre dışı.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = { $app_title } yükleme işlemini iptal etmek istediğinizden emin misiniz?

# MSI Installer UI - Browse Dialog
msi-browse-title = Geçerli hedef klasörü değiştir
msi-browse-description = Hedef klasöre gözat.
msi-browse-combo-label = K&onum:
msi-browse-path-label = K&lasör adı:
msi-browse-up-tooltip = Bir düzey yukarı
msi-browse-new-folder-tooltip = Yeni bir klasör oluştur

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Belirtilen hedef dizin geçersiz veya desteklenmeyen bir sürücü türünde bulunuyor.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Disk Alanı Gereksinimleri
msi-disk-cost-description = Seçili özelliklerin yüklenmesi için gereken disk alanı.
msi-disk-cost-text = Vurgulanan birimlerde şu anda seçili olan özellikler için yeterli disk alanı yok. Vurgulanan birimlerdeki bazı dosyaları kaldırabilir, yerel sürücülere daha az özellik yükleyebilir veya farklı hedef sürücüler seçebilirsiniz.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } Yükleyici Bilgileri

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } Kurulum Sihirbazı tamamlanmadan sona erdi
msi-fatal-description1 = { $app_title } kurulumu kesildi. Sisteminizde değişiklik yapılmadı. Daha sonra bu programı yüklemek için lütfen kurulumu yeniden çalıştırın.
msi-fatal-description2 = Kurulum Sihirbazı'ndan çıkmak için Son düğmesini tıklatın.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } Kurulum Sihirbazı'nın çalışması kesildi
msi-user-exit-description1 = { $app_title } kurulumu kesildi. Sisteminizde değişiklik yapılmadı. Daha sonra bu programı yüklemek için lütfen kurulumu yeniden çalıştırın.
msi-user-exit-description2 = Kurulum Sihirbazı'ndan çıkmak için Son düğmesini tıklatın.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Kullanılan Dosyalar
msi-files-in-use-description = Güncelleştirilmesi gereken bazı dosyalar şu anda kullanılıyor.
msi-files-in-use-text = Aşağıdaki uygulamalar, bu kurulum tarafından güncelleştirilmesi gereken dosyaları kullanıyor. Söz konusu uygulamaları kapatın ve yükleme işlemine devam etmek için Yeniden Dene'yi veya yüklemeden çıkmak için İptal'i tıklatın.
msi-files-in-use-exit = Çı&kış

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Kullanılan Dosyalar
msi-rm-files-in-use-description = Güncelleştirilmesi gereken bazı dosyalar şu anda kullanılıyor.
msi-rm-files-in-use-text = Aşağıdaki uygulamalar, bu kurulum tarafından güncelleştirilmesi gereken dosyaları kullanıyor. Kurulum Sihirbazı'nın söz konusu uygulamaları otomatik olarak kapatmasına ve yeniden başlatmayı denemesine izin verebilir veya bunları el ile kapatıp yükleme işlemine devam etmek için Tamam'ı tıklatabilirsiniz.
msi-rm-files-in-use-use-rm = Uygulamaları otomatik olarak &kapat ve kurulum tamamlandıktan sonra yeniden başlatmayı dene.
msi-rm-files-in-use-dont-use-rm = Uygula&maları kapatma. (Yeniden başlatma gerekecek.)

# MSI Installer UI - Resume Dialog
msi-resume-title = { $app_title } Kurulum Sihirbazı sürdürülüyor
msi-resume-description = Kurulum Sihirbazı { $app_title } ürününü bilgisayarınıza yüklemeyi tamamlayacak. Devam etmek için Yükle'yi, Kurulum Sihirbazı'ndan çıkmak içinse İptal'i tıklatın.
msi-resume-btn-install = Yü&kle

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } için masaüstü kısayolu
msi-start-menu-shortcut-description = { $app_title } için Başlat Menüsü kısayolu
# MSI Installer UI - Readme Dialog
msi-readme-title = Önemli Bilgiler
msi-readme-description = Devam etmeden önce lütfen aşağıdaki bilgileri okuyun.
