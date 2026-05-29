# Shared titles
title-update = { $app_title } 更新
title-setup = { $app_title } 安裝
title-uninstall = { $app_title } 解除安裝
error-title = { $program_name } 錯誤

# Shared buttons
btn-cancel = 取消
btn-install-update = 安裝更新
btn-install = 安裝
btn-update = 更新
btn-downgrade = 降級
btn-repair = 修復
btn-open-log = 開啟記錄檔
btn-open-install-dir = 開啟安裝目錄
btn-ok = 確定
btn-hide = 隱藏
# Elevation (dialogs_common.rs)
elevate-header = 需要系統管理員權限
elevate-body = 安裝 { $app_title } 版本 { $app_version } 需要系統管理員權限。是否允許繼續此更新?

# Restart required (prerequisite.rs)
restart-header = 需要重新開機
restart-body = 在繼續安裝之前需要重新開機。請重新開機後再次執行安裝程式。

# Missing dependencies (prerequisite.rs)
missing-deps-header = 需要其他元件
missing-deps-body = { $app_title } 需要先安裝下列項目: { $deps }。是否立即下載並安裝它們?

# Uninstall with errors (uninstall)
uninstall-errors-header = 解除安裝完成，但發生問題
uninstall-errors-body = { $app_title } 已解除安裝，但部分檔案或資料夾無法移除。您可以手動刪除它們，或重新安裝應用程式後再次嘗試解除安裝。
uninstall-errors-log = 詳細資料已儲存至: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } 已安裝
overwrite-repair-body = 此應用程式已安裝在您的電腦上。如果無法正常運作，您可以嘗試重新安裝來修復。
overwrite-older-installed = { $app_title } 已安裝
overwrite-update-body = 目前已安裝版本 { $old_version }。是否要更新到版本 { $app_version }?
overwrite-newer-installed = 已安裝較新版本的 { $app_title }
overwrite-downgrade-body = 目前已安裝版本 { $old_version }，比此安裝程式還要新。不建議降級，可能會造成問題。是否仍要繼續?
overwrite-footer = 安裝位置: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = 解除安裝完成
uninstall-body = 應用程式已從您的電腦成功移除。

# Install hook failed (install.rs)
install-hook-header = 安裝部分成功
install-hook-body = 安裝已完成，但某些步驟可能失敗。如果應用程式無法正常運作，您可以嘗試重新安裝或聯絡應用程式作者。

# Splash fallback (splash.rs)
splash-header = 正在安裝 { $app_title }
splash-body = 正在設定 { $app_title } { $app_version }，請稍候...

# Dependency download (prerequisite.rs)
deps-download-header = 正在下載所需元件
deps-download-body = 正在下載 { $dep_name }，請稍候...

# Apply progress (apply_*_impl.rs)
apply-header = 正在安裝更新
apply-body = 正在更新到版本 { $app_version }，請稍候...
progress-cancelling = 正在取消...

# Start error (start_windows_impl.rs)
start-corrupt-header = 安裝已損毀
start-corrupt-body = 此應用程式無法啟動，因為部分檔案遺失或損毀。請重新安裝應用程式以解決此問題。

# Generic error
error-header = 發生問題

# Setup error (wix msi)
setup-error-header = 無法繼續安裝

# MSI Installer UI - Common
msi-dlg-title = { $app_title } 安裝程式
msi-btn-back = 上一步(&B)
msi-btn-next = 下一步(&N)
msi-btn-cancel = 取消
msi-btn-finish = 完成(&F)
msi-btn-ok = 確定
msi-btn-yes = 是(&Y)
msi-btn-no = 否(&N)
msi-btn-retry = 重試(&R)
msi-btn-ignore = 忽略(&I)

# MSI Installer UI - Welcome Dialog
msi-welcome-title = 歡迎使用 { $app_title } 安裝精靈
msi-welcome-description = 安裝精靈將在您的電腦上安裝 { $app_title }。請按 [下一步] 繼續進行，或按 [取消] 結束安裝精靈。
msi-welcome-update-description = 安裝精靈將更新您電腦上的 { $app_title }。請按 [下一步] 繼續進行，或按 [取消] 結束安裝精靈。

# MSI Installer UI - Exit Dialog
msi-exit-title = 已完成 { $app_title } 安裝精靈
msi-exit-description = 按一下 [完成] 按鈕結束安裝精靈。
msi-exit-launch-checkbox = 啟動 { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = 歡迎使用 { $app_title } 安裝精靈
msi-prepare-description = 安裝精靈正在準備引導您完成安裝，請稍候。

# MSI Installer UI - License Agreement Dialog
msi-license-title = 使用者授權合約
msi-license-description = 請仔細閱讀下面的授權合約。
msi-license-checkbox = 我接受授權合約中的條款(&A)

# MSI Installer UI - Install Scope Dialog
msi-scope-title = 安裝範圍
msi-scope-description = 選擇安裝範圍。
msi-scope-per-user = 僅為您安裝(&Y)
msi-scope-per-machine = 為所有使用者安裝(&A)
msi-scope-per-user-description = 僅為目前的使用者安裝
msi-scope-no-per-user-description = 需要系統管理員權限
msi-scope-per-machine-description = 需要系統管理員權限

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = 準備安裝 { $app_title }
msi-ready-install-text = 按一下 [安裝] 即可開始安裝。按一下 [上一步] 可檢閱或變更您的任何安裝設定。
msi-ready-change-title = 準備變更 { $app_title }
msi-ready-change-text = 按一下 [變更] 即可開始變更安裝。按一下 [上一步] 可檢閱或變更您的任何安裝設定。
msi-ready-repair-title = 準備修復 { $app_title }
msi-ready-repair-text = 按一下 [修復] 即可開始修復。按一下 [上一步] 可檢閱或變更您的任何安裝設定。
msi-ready-remove-title = 準備移除 { $app_title }
msi-ready-remove-text = 按一下 [移除] 即可從電腦移除 { $app_title }。按一下 [上一步] 可檢閱或變更您的任何安裝設定。
msi-ready-update-title = 準備更新 { $app_title }
msi-ready-update-text = 按一下 [更新] 即可開始更新。按一下 [上一步] 可檢閱或變更您的任何安裝設定。
msi-ready-btn-install = 安裝(&I)
msi-ready-btn-change = 變更(&C)
msi-ready-btn-repair = 修復(&R)
msi-ready-btn-remove = 移除(&R)
msi-ready-btn-update = 更新(&U)

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = 正在安裝 { $app_title }
msi-progress-installing-text = 安裝精靈正在安裝 { $app_title }，請稍候。
msi-progress-changing-title = 正在變更 { $app_title }
msi-progress-changing-text = 安裝精靈正在變更 { $app_title }，請稍候。
msi-progress-repairing-title = 正在修復 { $app_title }
msi-progress-repairing-text = 安裝精靈正在修復 { $app_title }，請稍候。
msi-progress-removing-title = 正在移除 { $app_title }
msi-progress-removing-text = 安裝精靈正在移除 { $app_title }，請稍候。
msi-progress-updating-title = 正在更新 { $app_title }
msi-progress-updating-text = 安裝精靈正在更新 { $app_title }，請稍候。
msi-progress-status = 狀態:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = 歡迎使用 { $app_title } 安裝精靈
msi-maint-welcome-description = 安裝精靈可以讓您修復或移除 { $app_title }。請按 [下一步] 繼續進行，或按 [取消] 結束安裝精靈。

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = 變更、修復或移除安裝
msi-maint-type-description = 選取您要執行的作業。
msi-maint-change-button = 變更(&C)...
msi-maint-change-tooltip = 變更...
msi-maint-change-text = 讓使用者變更已安裝的程式功能以及變更個別功能。
msi-maint-change-disabled = 目前已停用變更。
msi-maint-repair-button = 修復(&R)
msi-maint-repair-tooltip = 修復
msi-maint-repair-text = 修復最近安裝中的錯誤 - 修正遺失或損毀的檔案、捷徑和登錄項目。
msi-maint-repair-disabled = 目前已停用修復。
msi-maint-remove-button = 移除(&M)
msi-maint-remove-tooltip = 移除
msi-maint-remove-text = 從您的電腦移除 { $app_title }。
msi-maint-remove-disabled = 目前已停用移除。

# MSI Installer UI - Cancel Dialog
msi-cancel-text = 您確定要取消 { $app_title } 安裝嗎?

# MSI Installer UI - Browse Dialog
msi-browse-title = 變更目前目的地資料夾
msi-browse-description = 瀏覽到目的地資料夾。
msi-browse-combo-label = 查詢(&L):
msi-browse-path-label = 資料夾名稱(&D):
msi-browse-up-tooltip = 上移一層
msi-browse-new-folder-tooltip = 建立新資料夾

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = 指定的目的地目錄無效或位於不支援的磁碟機類型。

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = 磁碟空間需求
msi-disk-cost-description = 安裝已選取功能所需的磁碟空間。
msi-disk-cost-text = 反白顯示的磁碟區沒有足夠磁碟空間可供目前選取的功能使用。您可以從反白顯示的磁碟區移除一些檔案、安裝較少功能，或是選取其他目的地磁碟機。

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } 安裝程式資訊

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } 安裝精靈提前結束
msi-fatal-description1 = { $app_title } 安裝程式已中斷。您的系統尚未被修改。若要稍後再安裝此程式，請再執行一次安裝程式。
msi-fatal-description2 = 按一下 [完成] 按鈕結束安裝精靈。

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } 安裝精靈已中斷
msi-user-exit-description1 = { $app_title } 安裝程式已中斷。您的系統尚未被修改。若要稍後再安裝此程式，請再執行一次安裝程式。
msi-user-exit-description2 = 按一下 [完成] 按鈕結束安裝精靈。

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = 檔案使用中
msi-files-in-use-description = 有些需要更新的檔案目前正在使用中。
msi-files-in-use-text = 下列應用程式正在使用要由此安裝程式更新的檔案。請關閉這些應用程式，然後按一下 [重試] 繼續安裝，或按一下 [取消] 結束安裝。
msi-files-in-use-exit = 結束(&X)

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = 檔案使用中
msi-rm-files-in-use-description = 有些需要更新的檔案目前正在使用中。
msi-rm-files-in-use-text = 下列應用程式正在使用要由此安裝程式更新的檔案。您可以讓安裝精靈自動關閉並嘗試重新啟動這些應用程式，或是手動關閉它們後按 [確定] 繼續安裝。
msi-rm-files-in-use-use-rm = 自動關閉應用程式(&C)，並在安裝完成後嘗試重新啟動它們。
msi-rm-files-in-use-dont-use-rm = 不關閉應用程式(&D)。(必須重新開機。)

# MSI Installer UI - Resume Dialog
msi-resume-title = 繼續執行 { $app_title } 安裝精靈
msi-resume-description = 安裝精靈即將完成在您的電腦上安裝 { $app_title }。請按 [安裝] 繼續進行，或按 [取消] 結束安裝精靈。
msi-resume-btn-install = 安裝(&I)

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } 桌面捷徑
msi-start-menu-shortcut-description = { $app_title } 開始功能表捷徑
# MSI Installer UI - Readme Dialog
msi-readme-title = 讀我資訊
msi-readme-description = 請在繼續之前閱讀以下資訊。
