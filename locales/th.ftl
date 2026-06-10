# Shared titles
title-update = อัปเดต { $app_title }
title-setup = การติดตั้ง { $app_title }
title-uninstall = ถอนการติดตั้ง { $app_title }
error-title = ข้อผิดพลาด { $program_name }

# Shared buttons
btn-cancel = ยกเลิก
btn-install-update = ติดตั้งการอัปเดต
btn-install = ติดตั้ง
btn-update = อัปเดต
btn-downgrade = ดาวน์เกรด
btn-repair = ซ่อมแซม
btn-open-log = เปิดบันทึก
btn-open-install-dir = เปิดโฟลเดอร์การติดตั้ง
btn-ok = ตกลง
# Elevation (dialogs_common.rs)
elevate-header = ต้องการสิทธิ์ผู้ดูแลระบบ
elevate-body = { $app_title } จำเป็นต้องมีสิทธิ์ผู้ดูแลระบบเพื่อติดตั้งเวอร์ชัน { $app_version } อนุญาตให้การอัปเดตนี้ดำเนินการต่อหรือไม่?

# Restart required (prerequisite.rs)
restart-header = ต้องเริ่มระบบใหม่
restart-body = คอมพิวเตอร์ของคุณต้องเริ่มระบบใหม่ก่อนที่การติดตั้งจะดำเนินการต่อได้ โปรดเริ่มระบบคอมพิวเตอร์ของคุณใหม่และเรียกใช้การติดตั้งอีกครั้ง

# Missing dependencies (prerequisite.rs)
missing-deps-header = ต้องการคอมโพเนนต์เพิ่มเติม
missing-deps-body = { $app_title } ต้องติดตั้งสิ่งต่อไปนี้ก่อน: { $deps } คุณต้องการดาวน์โหลดและติดตั้งตอนนี้หรือไม่?

# Uninstall with errors (uninstall)
uninstall-errors-header = ถอนการติดตั้งเสร็จสิ้นพร้อมปัญหา
uninstall-errors-body = { $app_title } ถูกถอนการติดตั้งแล้ว แต่ไม่สามารถลบไฟล์หรือโฟลเดอร์บางรายการได้ คุณสามารถลบด้วยตนเอง หรือติดตั้งแอปพลิเคชันใหม่และลองถอนการติดตั้งอีกครั้ง
uninstall-errors-log = รายละเอียดถูกบันทึกไว้ที่: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = ติดตั้ง { $app_title } แล้ว
overwrite-repair-body = แอปพลิเคชันนี้ติดตั้งบนคอมพิวเตอร์ของคุณแล้ว หากทำงานไม่ถูกต้อง คุณสามารถลองซ่อมแซมโดยการติดตั้งใหม่
overwrite-older-installed = ติดตั้ง { $app_title } แล้ว
overwrite-update-body = ปัจจุบันติดตั้งเวอร์ชัน { $old_version } อยู่ คุณต้องการอัปเดตเป็นเวอร์ชัน { $app_version } หรือไม่?
overwrite-newer-installed = ติดตั้งเวอร์ชันใหม่กว่าของ { $app_title } แล้ว
overwrite-downgrade-body = ปัจจุบันติดตั้งเวอร์ชัน { $old_version } ซึ่งใหม่กว่าตัวติดตั้งนี้ ไม่แนะนำให้ดาวน์เกรดและอาจทำให้เกิดปัญหา ต้องการดำเนินการต่อหรือไม่?
overwrite-footer = ติดตั้งที่: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = ถอนการติดตั้งเสร็จสมบูรณ์
uninstall-body = แอปพลิเคชันถูกลบออกจากคอมพิวเตอร์ของคุณเรียบร้อยแล้ว

# Install hook failed (install.rs)
install-hook-header = การติดตั้งสำเร็จบางส่วน
install-hook-body = การติดตั้งเสร็จสมบูรณ์ แต่บางขั้นตอนอาจล้มเหลว หากแอปพลิเคชันทำงานไม่ถูกต้อง คุณสามารถลองติดตั้งใหม่หรือติดต่อผู้สร้างแอปพลิเคชัน

# Splash fallback (splash.rs)
splash-header = กำลังติดตั้ง { $app_title }
splash-body = กำลังตั้งค่า { $app_title } { $app_version } โปรดรอสักครู่...

# Dependency download (prerequisite.rs)
deps-download-header = กำลังดาวน์โหลดคอมโพเนนต์ที่จำเป็น
deps-download-body = กำลังดาวน์โหลด { $dep_name } โปรดรอสักครู่...

# Apply progress (apply_*_impl.rs)
apply-header = กำลังติดตั้งการอัปเดต
apply-body = กำลังอัปเดตเป็นเวอร์ชัน { $app_version } โปรดรอสักครู่...

# Start error (start_windows_impl.rs)
start-corrupt-header = การติดตั้งเสียหาย
start-corrupt-body = ไม่สามารถเริ่มแอปพลิเคชันนี้ได้เนื่องจากไฟล์บางรายการสูญหายหรือเสียหาย โปรดติดตั้งแอปพลิเคชันใหม่เพื่อแก้ไขปัญหานี้

# Generic error
error-header = เกิดข้อผิดพลาด

# Setup error (wix msi)
setup-error-header = ไม่สามารถดำเนินการติดตั้งต่อได้
setup-disk-space-insufficient = { $app_title } ต้องการพื้นที่ดิสก์อย่างน้อย { $required_space } ในการติดตั้ง มีพื้นที่ว่างเพียง { $available_space } เท่านั้น
setup-windows-version-unsupported = ตัวติดตั้งนี้ต้องการ Windows 7 SP1 ขึ้นไปและไม่สามารถทำงานได้
setup-embedded-zip-missing = ไม่พบไฟล์ zip ที่ฝังอยู่ โปรดติดต่อผู้สร้างแอปพลิเคชัน
setup-os-version-required = แอปพลิเคชันนี้ต้องการ Windows { $os_version } ขึ้นไป
setup-cpu-arch-unsupported = แอปพลิเคชันนี้ ({ $machine_arch }) ไม่รองรับสถาปัตยกรรม CPU ของคุณ
setup-stop-app-failed = ไม่สามารถหยุดแอปพลิเคชันได้ ({ $error }) โปรดปิดแอปพลิเคชันแล้วลองเรียกใช้ตัวติดตั้งอีกครั้ง
setup-remove-dir-failed = ไม่สามารถลบไดเรกทอรีแอปพลิเคชันที่มีอยู่ได้ โปรดปิดแอปพลิเคชันแล้วลองเรียกใช้ตัวติดตั้งอีกครั้ง หากปัญหายังคงอยู่ ให้ลองถอนการติดตั้งก่อนผ่านโปรแกรมและคุณลักษณะ หรือรีสตาร์ทคอมพิวเตอร์ของคุณ
setup-update-exe-missing = ตัวติดตั้งนี้ขาดไฟล์ไบนารีที่สำคัญ (Update.exe) โปรดติดต่อผู้สร้างแอปพลิเคชัน
setup-main-exe-missing = ไม่พบไฟล์ปฏิบัติการหลักในแพ็กเกจ โปรดติดต่อผู้สร้างแอปพลิเคชัน

# MSI Installer UI - Common
msi-dlg-title = การติดตั้ง { $app_title }
msi-btn-back = ย้อน&กลับ
msi-btn-next = ถัด&ไป
msi-btn-cancel = ยกเลิก
msi-btn-finish = เ&สร็จสิ้น
msi-btn-ok = ตกลง
msi-btn-yes = &ใช่
msi-btn-no = &ไม่
msi-btn-retry = &ลองใหม่
msi-btn-ignore = &ละเว้น

# MSI Installer UI - Welcome Dialog
msi-welcome-title = ยินดีต้อนรับสู่ตัวช่วยสร้างการติดตั้ง { $app_title }
msi-welcome-description = ตัวช่วยสร้างการติดตั้งจะติดตั้ง { $app_title } บนเครื่องคอมพิวเตอร์ของคุณ คลิก ถัดไป เพื่อดำเนินการต่อ หรือ ยกเลิก เพื่อออกจากตัวช่วยสร้างการติดตั้ง
msi-welcome-update-description = ตัวช่วยสร้างการติดตั้งจะอัปเดต { $app_title } บนคอมพิวเตอร์ของคุณ คลิก ถัดไป เพื่อดำเนินการต่อ หรือ ยกเลิก เพื่อออกจากตัวช่วยสร้างการติดตั้ง

# MSI Installer UI - Exit Dialog
msi-exit-title = ตัวช่วยสร้างการติดตั้ง { $app_title } ดำเนินการเสร็จสมบูรณ์
msi-exit-description = คลิกปุ่ม เสร็จสิ้น เพื่อออกจากตัวช่วยสร้างการติดตั้ง
msi-exit-launch-checkbox = เรียกใช้ { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = ยินดีต้อนรับสู่ตัวช่วยสร้างการติดตั้ง { $app_title }
msi-prepare-description = โปรดรอสักครู่ขณะที่ตัวช่วยสร้างการติดตั้งเตรียมการที่จะแนะนำคุณตลอดขั้นตอนการติดตั้ง

# MSI Installer UI - License Agreement Dialog
msi-license-title = ข้อตกลงสิทธิ์การใช้งานสำหรับผู้ใช้
msi-license-description = โปรดอ่านข้อตกลงสิทธิ์การใช้งานต่อไปนี้อย่างถี่ถ้วน
msi-license-checkbox = ฉัน&ยอมรับเงื่อนไขในข้อตกลงสิทธิ์การใช้งาน

# MSI Installer UI - Install Scope Dialog
msi-scope-title = ขอบเขตการติดตั้ง
msi-scope-description = เลือกขอบเขตการติดตั้ง
msi-scope-per-user = ติดตั้งสำหรับ&คุณเท่านั้น
msi-scope-per-machine = ติดตั้งสำหรับผู้ใช้&ทุกราย
msi-scope-per-user-description = ติดตั้งสำหรับผู้ใช้ปัจจุบันเท่านั้น
msi-scope-no-per-user-description = ต้องการสิทธิ์ผู้ดูแลระบบ
msi-scope-per-machine-description = ต้องการสิทธิ์ผู้ดูแลระบบ

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = พร้อมทำการติดตั้ง { $app_title }
msi-ready-install-text = คลิก ติดตั้ง เพื่อเริ่มการติดตั้ง คลิก ย้อนกลับ เพื่อตรวจทานหรือเปลี่ยนแปลงการตั้งค่าการติดตั้งของคุณ
msi-ready-change-title = พร้อมทำการเปลี่ยนแปลง { $app_title }
msi-ready-change-text = คลิก เปลี่ยน เพื่อเริ่มการเปลี่ยนแปลงการติดตั้ง คลิก ย้อนกลับ เพื่อตรวจทานหรือเปลี่ยนแปลงการตั้งค่าการติดตั้งของคุณ
msi-ready-repair-title = พร้อมทำการซ่อมแซม { $app_title }
msi-ready-repair-text = คลิก ซ่อมแซม เพื่อเริ่มการซ่อมแซม คลิก ย้อนกลับ เพื่อตรวจทานหรือเปลี่ยนแปลงการตั้งค่าการติดตั้งของคุณ
msi-ready-remove-title = พร้อมทำการเอา { $app_title } ออก
msi-ready-remove-text = คลิก เอาออก เพื่อเอา { $app_title } ออกจากคอมพิวเตอร์ของคุณ คลิก ย้อนกลับ เพื่อตรวจทานหรือเปลี่ยนแปลงการตั้งค่าการติดตั้งของคุณ
msi-ready-update-title = พร้อมทำการอัปเดต { $app_title }
msi-ready-update-text = คลิก อัปเดต เพื่อเริ่มการอัปเดต คลิก ย้อนกลับ เพื่อตรวจทานหรือเปลี่ยนแปลงการตั้งค่าการติดตั้งของคุณ
msi-ready-btn-install = &ติดตั้ง
msi-ready-btn-change = เ&ปลี่ยน
msi-ready-btn-repair = ซ่อ&มแซม
msi-ready-btn-remove = เอ&าออก
msi-ready-btn-update = &อัปเดต

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = กำลังติดตั้ง { $app_title }
msi-progress-installing-text = โปรดรอสักครู่ขณะที่ตัวช่วยสร้างการติดตั้งทำการติดตั้ง { $app_title }
msi-progress-changing-title = กำลังเปลี่ยนแปลง { $app_title }
msi-progress-changing-text = โปรดรอสักครู่ขณะที่ตัวช่วยสร้างการติดตั้งทำการเปลี่ยนแปลง { $app_title }
msi-progress-repairing-title = กำลังซ่อมแซม { $app_title }
msi-progress-repairing-text = โปรดรอสักครู่ขณะที่ตัวช่วยสร้างการติดตั้งทำการซ่อมแซม { $app_title }
msi-progress-removing-title = กำลังเอา { $app_title } ออก
msi-progress-removing-text = โปรดรอสักครู่ขณะที่ตัวช่วยสร้างการติดตั้งทำการเอา { $app_title } ออก
msi-progress-updating-title = กำลังอัปเดต { $app_title }
msi-progress-updating-text = โปรดรอสักครู่ขณะที่ตัวช่วยสร้างการติดตั้งอัปเดต { $app_title }
msi-progress-status = สถานะ:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = ยินดีต้อนรับสู่ตัวช่วยสร้างการติดตั้ง { $app_title }
msi-maint-welcome-description = ตัวช่วยสร้างการติดตั้งจะอนุญาตให้คุณซ่อมแซมหรือเอา { $app_title } ออก คลิก ถัดไป เพื่อดำเนินการต่อ หรือ ยกเลิก เพื่อออกจากตัวช่วยสร้างการติดตั้ง

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = เปลี่ยนแปลง ซ่อมแซม หรือเอาการติดตั้งออก
msi-maint-type-description = เลือกการดำเนินการที่คุณต้องการกระทำ
msi-maint-change-button = เ&ปลี่ยน...
msi-maint-change-tooltip = เปลี่ยน...
msi-maint-change-text = อนุญาตให้ผู้ใช้เปลี่ยนแปลงคุณลักษณะของโปรแกรมที่จะติดตั้งและเปลี่ยนแปลงคุณลักษณะแต่ละรายการ
msi-maint-change-disabled = ขณะนี้ปิดใช้งานการเปลี่ยน
msi-maint-repair-button = ซ่อ&มแซม
msi-maint-repair-tooltip = ซ่อมแซม
msi-maint-repair-text = ซ่อมแซมข้อผิดพลาดในการติดตั้งครั้งล่าสุด - แก้ไขแฟ้มที่ขาดหายและเสียหาย ทางลัด และรายการรีจิสทรี
msi-maint-repair-disabled = ขณะนี้ปิดใช้งานการซ่อมแซม
msi-maint-remove-button = เ&อาออก
msi-maint-remove-tooltip = เอาออก
msi-maint-remove-text = เอา { $app_title } ออกจากคอมพิวเตอร์ของคุณ
msi-maint-remove-disabled = ขณะนี้ปิดใช้งานการเอาออก

# MSI Installer UI - Cancel Dialog
msi-cancel-text = คุณแน่ใจหรือไม่ว่าคุณต้องการยกเลิกการติดตั้ง { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = เปลี่ยนโฟลเดอร์ปลายทางปัจจุบัน
msi-browse-description = เรียกดูโฟลเดอร์ปลายทาง
msi-browse-combo-label = &มองหาใน:
msi-browse-path-label = &ชื่อโฟลเดอร์:
msi-browse-up-tooltip = เลื่อนขึ้นหนึ่งระดับ
msi-browse-new-folder-tooltip = สร้างโฟลเดอร์ใหม่

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = ไดเรกทอรีปลายทางที่ระบุไม่ถูกต้องหรืออยู่ในชนิดของไดรฟ์ที่ไม่รองรับ

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = ความต้องการเนื้อที่ดิสก์
msi-disk-cost-description = เนื้อที่ดิสก์ที่ต้องการสำหรับการติดตั้งคุณลักษณะที่เลือก
msi-disk-cost-text = ไดรฟ์ข้อมูลที่เลือกมีเนื้อที่ดิสก์ไม่เพียงพอสำหรับคุณลักษณะที่เลือกอยู่ในขณะนี้ คุณสามารถเอาบางแฟ้มออกจากไดรฟ์ข้อมูลที่เลือก ติดตั้งคุณลักษณะน้อยลง หรือเลือกไดรฟ์ปลายทางอื่น

# MSI Installer UI - Error Dialog
msi-error-dlg-title = ข้อมูลตัวติดตั้ง { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = ตัวช่วยสร้างการติดตั้ง { $app_title } สิ้นสุดลงก่อนเสร็จสมบูรณ์
msi-fatal-description1 = การติดตั้ง { $app_title } ถูกขัดจังหวะ ระบบของคุณยังไม่ถูกปรับเปลี่ยน เมื่อต้องการติดตั้งโปรแกรมนี้ในภายหลัง โปรดเรียกใช้การติดตั้งอีกครั้ง
msi-fatal-description2 = คลิกปุ่ม เสร็จสิ้น เพื่อออกจากตัวช่วยสร้างการติดตั้ง

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = ตัวช่วยสร้างการติดตั้ง { $app_title } ถูกขัดจังหวะ
msi-user-exit-description1 = การติดตั้ง { $app_title } ถูกขัดจังหวะ ระบบของคุณยังไม่ถูกปรับเปลี่ยน เมื่อต้องการติดตั้งโปรแกรมนี้ในภายหลัง โปรดเรียกใช้การติดตั้งอีกครั้ง
msi-user-exit-description2 = คลิกปุ่ม เสร็จสิ้น เพื่อออกจากตัวช่วยสร้างการติดตั้ง

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = แฟ้มถูกใช้งานอยู่
msi-files-in-use-description = แฟ้มที่จำเป็นต้องปรับปรุงมีการใช้งานอยู่แล้วในขณะนี้
msi-files-in-use-text = โปรแกรมประยุกต์ต่อไปนี้กำลังใช้งานแฟ้มที่จำเป็นต้องได้รับการปรับปรุงโดยโปรแกรมติดตั้งนี้ ปิดโปรแกรมประยุกต์เหล่านี้แล้วคลิก ลองใหม่ เพื่อทำการติดตั้งต่อหรือคลิก ยกเลิก เพื่อออก
msi-files-in-use-exit = &จบการทำงาน

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = แฟ้มถูกใช้งานอยู่
msi-rm-files-in-use-description = แฟ้มที่จำเป็นต้องปรับปรุงมีการใช้งานอยู่แล้วในขณะนี้
msi-rm-files-in-use-text = โปรแกรมประยุกต์ต่อไปนี้กำลังใช้แฟ้มที่จำเป็นต้องได้รับการปรับปรุงโดยโปรแกรมติดตั้งนี้ คุณสามารถปล่อยให้ตัวช่วยสร้างการติดตั้งปิดและพยายามเริ่มโปรแกรมประยุกต์เหล่านี้ใหม่โดยอัตโนมัติ หรือคุณสามารถปิดด้วยตนเองและคลิก ตกลง เพื่อทำการติดตั้งต่อ
msi-rm-files-in-use-use-rm = ปิ&ดโปรแกรมประยุกต์โดยอัตโนมัติและพยายามเริ่มใหม่หลังจากการติดตั้งเสร็จสมบูรณ์
msi-rm-files-in-use-dont-use-rm = &อย่าปิดโปรแกรมประยุกต์ (จำเป็นต้องเริ่มระบบใหม่)

# MSI Installer UI - Resume Dialog
msi-resume-title = กำลังดำเนินการตัวช่วยสร้างการติดตั้ง { $app_title } ต่อ
msi-resume-description = ตัวช่วยสร้างการติดตั้งจะดำเนินการติดตั้ง { $app_title } บนคอมพิวเตอร์ของคุณให้เสร็จสมบูรณ์ ให้คลิก ติดตั้ง เพื่อดำเนินการต่อหรือคลิก ยกเลิก เพื่อออกจากตัวช่วยสร้างการติดตั้ง
msi-resume-btn-install = &ติดตั้ง

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = ทางลัดเดสก์ท็อปสำหรับ { $app_title }
msi-start-menu-shortcut-description = ทางลัดเมนูเริ่มสำหรับ { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = ข้อมูลสำคัญ
msi-readme-description = โปรดอ่านข้อมูลต่อไปนี้ก่อนดำเนินการต่อ
