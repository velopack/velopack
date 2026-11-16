# Shared titles
title-update = Cập nhật { $app_title }
title-setup = Cài đặt { $app_title }
title-uninstall = Gỡ cài đặt { $app_title }
error-title = Lỗi { $program_name }

# Shared buttons
btn-cancel = Hủy
btn-install-update = Cài đặt bản cập nhật
btn-install = Cài đặt
btn-update = Cập nhật
btn-downgrade = Hạ phiên bản
btn-repair = Sửa chữa
btn-open-log = Mở nhật ký
btn-open-install-dir = Mở thư mục cài đặt
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Cần quyền quản trị viên
elevate-body = { $app_title } cần quyền quản trị viên để cài đặt phiên bản { $app_version }. Cho phép tiếp tục cập nhật?

# Restart required (prerequisite.rs)
restart-header = Cần khởi động lại
restart-body = Bạn cần khởi động lại máy tính trước khi tiếp tục cài đặt. Vui lòng khởi động lại máy tính rồi chạy lại trình cài đặt.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Cần cài thêm thành phần
missing-deps-body = { $app_title } cần cài đặt các thành phần sau trước: { $deps }. Bạn có muốn tải xuống và cài đặt ngay không?

# Uninstall with errors (uninstall)
uninstall-errors-header = Đã gỡ cài đặt nhưng có lỗi
uninstall-errors-body = Đã gỡ cài đặt { $app_title }, nhưng không thể xóa một số tệp hoặc thư mục. Bạn có thể tự xóa chúng, hoặc cài đặt lại ứng dụng rồi thử gỡ cài đặt lần nữa.
uninstall-errors-log = Chi tiết đã được lưu tại: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } đã được cài đặt
overwrite-repair-body = Ứng dụng này đã được cài đặt trên máy tính của bạn. Nếu ứng dụng không hoạt động bình thường, bạn có thể thử sửa chữa bằng cách cài đặt lại.
overwrite-older-installed = { $app_title } đã được cài đặt
overwrite-update-body = Máy tính đang cài phiên bản { $old_version }. Bạn có muốn cập nhật lên phiên bản { $app_version } không?
overwrite-newer-installed = Một phiên bản mới hơn của { $app_title } đã được cài đặt
overwrite-downgrade-body = Máy tính đang cài phiên bản { $old_version }, mới hơn phiên bản của trình cài đặt này. Không nên hạ phiên bản vì có thể gây ra sự cố. Vẫn tiếp tục?
overwrite-footer = Đã cài đặt tại: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Đã gỡ cài đặt xong
uninstall-body = Ứng dụng đã được gỡ khỏi máy tính của bạn.

# Uninstall progress (uninstall.rs)
uninstall-progress-header = Đang gỡ cài đặt { $app_title }
uninstall-progress-body = Đang xóa các tệp và lối tắt, vui lòng đợi...

# Install hook failed (install.rs)
install-hook-header = Cài đặt chưa hoàn chỉnh
install-hook-body = Quá trình cài đặt đã kết thúc, nhưng một số bước có thể đã thất bại. Nếu ứng dụng không hoạt động bình thường, bạn có thể thử cài đặt lại hoặc liên hệ với tác giả ứng dụng.

# Splash fallback (splash.rs)
splash-header = Đang cài đặt { $app_title }
splash-body = Đang thiết lập { $app_title } { $app_version }, vui lòng đợi...

# Dependency download (prerequisite.rs)
deps-download-header = Đang tải thành phần cần thiết
deps-download-body = Đang tải { $dep_name }, vui lòng đợi...

# Apply progress (apply_*_impl.rs)
apply-header = Đang cài đặt bản cập nhật
apply-body = Đang cập nhật lên phiên bản { $app_version }, vui lòng đợi...

# Start error (start_windows_impl.rs)
start-corrupt-header = Bản cài đặt bị hỏng
start-corrupt-body = Không thể khởi động ứng dụng vì một số tệp bị thiếu hoặc bị hỏng. Vui lòng cài đặt lại ứng dụng để khắc phục.

# Generic error
error-header = Đã xảy ra lỗi

# Setup error (wix msi)
setup-error-header = Không thể tiếp tục cài đặt
setup-disk-space-insufficient = { $app_title } cần ít nhất { $required_space } dung lượng đĩa để cài đặt, nhưng chỉ còn trống { $available_space }.
setup-windows-version-unsupported = Trình cài đặt này yêu cầu Windows 7 SP1 trở lên và không thể chạy.
setup-embedded-zip-missing = Không tìm thấy tệp zip nhúng. Vui lòng liên hệ với tác giả ứng dụng.
setup-os-version-required = Ứng dụng này yêu cầu Windows { $os_version } trở lên.
setup-cpu-arch-unsupported = Ứng dụng này ({ $machine_arch }) không hỗ trợ kiến trúc CPU của bạn.
setup-stop-app-failed = Không thể dừng ứng dụng ({ $error }). Vui lòng đóng ứng dụng rồi chạy lại trình cài đặt.
setup-remove-dir-failed = Không thể xóa thư mục ứng dụng hiện có. Vui lòng đóng ứng dụng rồi chạy lại trình cài đặt. Nếu vẫn gặp lỗi, hãy thử gỡ cài đặt trước trong Programs & Features hoặc khởi động lại máy tính.
setup-update-exe-missing = Trình cài đặt này thiếu một tệp thực thi quan trọng (Update.exe). Vui lòng liên hệ với tác giả ứng dụng.
setup-main-exe-missing = Không tìm thấy tệp thực thi chính trong gói cài đặt. Vui lòng liên hệ với tác giả ứng dụng.

# MSI Installer UI - Common
msi-dlg-title = Cài đặt { $app_title }
msi-btn-back = &Quay lại
msi-btn-next = &Tiếp theo
msi-btn-cancel = Hủy
msi-btn-finish = &Hoàn tất
msi-btn-ok = OK
msi-btn-yes = &Có
msi-btn-no = &Không
msi-btn-retry = Thử &lại
msi-btn-ignore = &Bỏ qua

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Chào mừng bạn đến với Trình hướng dẫn cài đặt { $app_title }
msi-welcome-description = Trình hướng dẫn sẽ cài đặt { $app_title } trên máy tính của bạn. Bấm Tiếp theo để tiếp tục hoặc Hủy để thoát Trình hướng dẫn cài đặt.
msi-welcome-update-description = Trình hướng dẫn sẽ cập nhật { $app_title } trên máy tính của bạn. Bấm Tiếp theo để tiếp tục hoặc Hủy để thoát Trình hướng dẫn cài đặt.

# MSI Installer UI - Exit Dialog
msi-exit-title = Đã hoàn tất Trình hướng dẫn cài đặt { $app_title }
msi-exit-description = Bấm nút Hoàn tất để thoát Trình hướng dẫn cài đặt.
msi-exit-launch-checkbox = Mở { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Chào mừng bạn đến với Trình hướng dẫn cài đặt { $app_title }
msi-prepare-description = Vui lòng đợi trong khi Trình hướng dẫn chuẩn bị các bước cài đặt.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Thỏa thuận cấp phép người dùng cuối
msi-license-description = Vui lòng đọc kỹ thỏa thuận cấp phép sau.
msi-license-checkbox = &Tôi đồng ý với các điều khoản trong Thỏa thuận cấp phép

# MSI Installer UI - Readme Dialog
msi-readme-title = Thông tin cần đọc
msi-readme-description = Vui lòng đọc thông tin sau trước khi tiếp tục.

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Phạm vi cài đặt
msi-scope-description = Chọn phạm vi cài đặt.
msi-scope-per-user = Chỉ cài đặt cho &bạn
msi-scope-per-machine = Cài đặt cho &tất cả người dùng
msi-scope-per-user-description = Chỉ cài đặt cho người dùng hiện tại
msi-scope-no-per-user-description = Cần quyền quản trị viên
msi-scope-per-machine-description = Cần quyền quản trị viên

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Sẵn sàng cài đặt { $app_title }
msi-ready-install-text = Bấm Cài đặt để bắt đầu. Bấm Quay lại để xem lại hoặc thay đổi các thiết lập cài đặt.
msi-ready-change-title = Sẵn sàng thay đổi { $app_title }
msi-ready-change-text = Bấm Thay đổi để bắt đầu thay đổi bản cài đặt. Bấm Quay lại để xem lại hoặc thay đổi các thiết lập cài đặt.
msi-ready-repair-title = Sẵn sàng sửa chữa { $app_title }
msi-ready-repair-text = Bấm Sửa chữa để bắt đầu sửa chữa. Bấm Quay lại để xem lại hoặc thay đổi các thiết lập cài đặt.
msi-ready-remove-title = Sẵn sàng gỡ { $app_title }
msi-ready-remove-text = Bấm Gỡ để gỡ { $app_title } khỏi máy tính của bạn. Bấm Quay lại để xem lại hoặc thay đổi các thiết lập cài đặt.
msi-ready-update-title = Sẵn sàng cập nhật { $app_title }
msi-ready-update-text = Bấm Cập nhật để bắt đầu cập nhật. Bấm Quay lại để xem lại hoặc thay đổi các thiết lập cài đặt.
msi-ready-btn-install = &Cài đặt
msi-ready-btn-change = &Thay đổi
msi-ready-btn-repair = &Sửa chữa
msi-ready-btn-remove = &Gỡ
msi-ready-btn-update = &Cập nhật

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Đang cài đặt { $app_title }
msi-progress-installing-text = Vui lòng đợi trong khi Trình hướng dẫn cài đặt { $app_title }.
msi-progress-changing-title = Đang thay đổi { $app_title }
msi-progress-changing-text = Vui lòng đợi trong khi Trình hướng dẫn thay đổi { $app_title }.
msi-progress-repairing-title = Đang sửa chữa { $app_title }
msi-progress-repairing-text = Vui lòng đợi trong khi Trình hướng dẫn sửa chữa { $app_title }.
msi-progress-removing-title = Đang gỡ { $app_title }
msi-progress-removing-text = Vui lòng đợi trong khi Trình hướng dẫn gỡ { $app_title }.
msi-progress-updating-title = Đang cập nhật { $app_title }
msi-progress-updating-text = Vui lòng đợi trong khi Trình hướng dẫn cập nhật { $app_title }.
msi-progress-status = Trạng thái:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Chào mừng bạn đến với Trình hướng dẫn cài đặt { $app_title }
msi-maint-welcome-description = Trình hướng dẫn cho phép bạn sửa chữa hoặc gỡ { $app_title }. Bấm Tiếp theo để tiếp tục hoặc Hủy để thoát Trình hướng dẫn cài đặt.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Thay đổi, sửa chữa hoặc gỡ bản cài đặt
msi-maint-type-description = Chọn thao tác bạn muốn thực hiện.
msi-maint-change-button = &Thay đổi...
msi-maint-change-tooltip = Thay đổi...
msi-maint-change-text = Cho phép thay đổi những tính năng được cài đặt của chương trình và thay đổi từng tính năng.
msi-maint-change-disabled = Tính năng Thay đổi hiện đang bị tắt.
msi-maint-repair-button = &Sửa chữa
msi-maint-repair-tooltip = Sửa chữa
msi-maint-repair-text = Sửa lỗi trong lần cài đặt gần nhất: khôi phục các tệp, lối tắt và mục đăng ký bị thiếu hoặc bị hỏng.
msi-maint-repair-disabled = Tính năng Sửa chữa hiện đang bị tắt.
msi-maint-remove-button = &Gỡ
msi-maint-remove-tooltip = Gỡ
msi-maint-remove-text = Gỡ { $app_title } khỏi máy tính của bạn.
msi-maint-remove-disabled = Tính năng Gỡ hiện đang bị tắt.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Bạn có chắc muốn hủy cài đặt { $app_title } không?

# MSI Installer UI - Browse Dialog
msi-browse-title = Thay đổi thư mục đích hiện tại
msi-browse-description = Duyệt đến thư mục đích.
msi-browse-combo-label = Tìm &trong:
msi-browse-path-label = Tên thư &mục:
msi-browse-up-tooltip = Lên một cấp
msi-browse-new-folder-tooltip = Tạo thư mục mới

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Thư mục đích đã chọn không hợp lệ hoặc nằm trên loại ổ đĩa không được hỗ trợ.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Dung lượng đĩa cần thiết
msi-disk-cost-description = Dung lượng đĩa cần để cài đặt các tính năng đã chọn.
msi-disk-cost-text = Các ổ đĩa được đánh dấu không đủ dung lượng trống cho những tính năng đang chọn. Bạn có thể xóa bớt tệp trên các ổ đĩa đó, cài đặt ít tính năng hơn lên ổ đĩa cục bộ, hoặc chọn ổ đĩa đích khác.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Thông tin trình cài đặt { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Trình hướng dẫn cài đặt { $app_title } đã kết thúc sớm
msi-fatal-description1 = Quá trình cài đặt { $app_title } đã bị gián đoạn. Hệ thống của bạn chưa bị thay đổi. Để cài đặt chương trình này sau, vui lòng chạy lại trình cài đặt.
msi-fatal-description2 = Bấm nút Hoàn tất để thoát Trình hướng dẫn cài đặt.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Trình hướng dẫn cài đặt { $app_title } đã bị gián đoạn
msi-user-exit-description1 = Quá trình cài đặt { $app_title } đã bị gián đoạn. Hệ thống của bạn chưa bị thay đổi. Để cài đặt chương trình này sau, vui lòng chạy lại trình cài đặt.
msi-user-exit-description2 = Bấm nút Hoàn tất để thoát Trình hướng dẫn cài đặt.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Tệp đang được sử dụng
msi-files-in-use-description = Một số tệp cần cập nhật hiện đang được sử dụng.
msi-files-in-use-text = Các ứng dụng sau đang sử dụng những tệp mà trình cài đặt cần cập nhật. Hãy đóng các ứng dụng này rồi bấm Thử lại để tiếp tục cài đặt, hoặc bấm Hủy để thoát.
msi-files-in-use-exit = T&hoát

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Tệp đang được sử dụng
msi-rm-files-in-use-description = Một số tệp cần cập nhật hiện đang được sử dụng.
msi-rm-files-in-use-text = Các ứng dụng sau đang sử dụng những tệp mà trình cài đặt cần cập nhật. Bạn có thể để Trình hướng dẫn tự động đóng và thử khởi động lại các ứng dụng này, hoặc tự đóng chúng rồi bấm OK để tiếp tục cài đặt.
msi-rm-files-in-use-use-rm = Tự động đó&ng ứng dụng và thử khởi động lại chúng sau khi cài đặt xong.
msi-rm-files-in-use-dont-use-rm = &Không đóng ứng dụng. (Sẽ cần khởi động lại máy tính.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Tiếp tục Trình hướng dẫn cài đặt { $app_title }
msi-resume-description = Trình hướng dẫn sẽ hoàn tất việc cài đặt { $app_title } trên máy tính của bạn. Bấm Cài đặt để tiếp tục hoặc Hủy để thoát Trình hướng dẫn cài đặt.
msi-resume-btn-install = &Cài đặt

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Lối tắt trên màn hình nền của { $app_title }
msi-start-menu-shortcut-description = Lối tắt trong menu Start của { $app_title }

# MSI Installer UI - Downgrade Error
msi-downgrade-error = Một phiên bản mới hơn của { $app_title } đã được cài đặt.
