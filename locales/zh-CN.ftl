# Shared titles
title-update = { $app_title } 更新
title-setup = { $app_title } 安装
title-uninstall = { $app_title } 卸载
error-title = { $program_name } 错误

# Shared buttons
btn-cancel = 取消
btn-install-update = 安装更新
btn-install = 安装
btn-update = 更新
btn-downgrade = 降级
btn-repair = 修复
btn-open-log = 打开日志
btn-open-install-dir = 打开安装目录
btn-ok = 确定
# Elevation (dialogs_common.rs)
elevate-header = 需要管理员权限
elevate-body = 安装 { $app_title } 版本 { $app_version } 需要管理员权限。是否允许继续此更新?

# Restart required (prerequisite.rs)
restart-header = 需要重新启动
restart-body = 在继续安装之前需要重新启动计算机。请重新启动计算机后再次运行安装程序。

# Missing dependencies (prerequisite.rs)
missing-deps-header = 需要其他组件
missing-deps-body = { $app_title } 需要先安装以下组件: { $deps }。是否立即下载并安装它们?

# Uninstall with errors (uninstall)
uninstall-errors-header = 卸载完成，但出现问题
uninstall-errors-body = { $app_title } 已卸载，但部分文件或文件夹无法删除。您可以手动删除它们，或者重新安装应用程序后再尝试卸载。
uninstall-errors-log = 详细信息已保存到: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } 已安装
overwrite-repair-body = 此应用程序已安装在您的计算机上。如果它无法正常工作，您可以尝试通过重新安装来修复它。
overwrite-older-installed = { $app_title } 已安装
overwrite-update-body = 当前已安装版本 { $old_version }。是否要更新到版本 { $app_version }?
overwrite-newer-installed = 已安装更新版本的 { $app_title }
overwrite-downgrade-body = 当前已安装版本 { $old_version }，它比此安装程序更新。不建议降级，可能会导致问题。是否仍要继续?
overwrite-footer = 安装位置: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = 卸载完成
uninstall-body = 应用程序已成功从您的计算机中删除。

# Install hook failed (install.rs)
install-hook-header = 安装部分成功
install-hook-body = 安装已完成，但某些步骤可能已失败。如果应用程序无法正常工作，您可以尝试重新安装或联系应用程序作者。

# Splash fallback (splash.rs)
splash-header = 正在安装 { $app_title }
splash-body = 正在设置 { $app_title } { $app_version }，请稍候...

# Dependency download (prerequisite.rs)
deps-download-header = 正在下载所需组件
deps-download-body = 正在下载 { $dep_name }，请稍候...

# Apply progress (apply_*_impl.rs)
apply-header = 正在安装更新
apply-body = 正在更新到版本 { $app_version }，请稍候...

# Start error (start_windows_impl.rs)
start-corrupt-header = 安装已损坏
start-corrupt-body = 此应用程序无法启动，因为它的某些文件丢失或已损坏。请重新安装应用程序以解决此问题。

# Generic error
error-header = 出现问题

# Setup error (wix msi)
setup-error-header = 无法继续安装
setup-disk-space-insufficient = 安装 { $app_title } 至少需要 { $required_space } 磁盘空间。当前仅有 { $available_space } 可用。
setup-windows-version-unsupported = 此安装程序需要 Windows 7 SP1 或更高版本，无法运行。
setup-embedded-zip-missing = 找不到嵌入的 zip 文件。请联系应用程序作者。
setup-os-version-required = 此应用程序需要 Windows { $os_version } 或更高版本。
setup-cpu-arch-unsupported = 此应用程序 ({ $machine_arch }) 不支持您的 CPU 架构。
setup-stop-app-failed = 无法停止应用程序 ({ $error })，请关闭应用程序后再次运行安装程序。
setup-remove-dir-failed = 无法删除现有的应用程序目录，请关闭应用程序后再次运行安装程序。如果问题仍然存在，请先通过“程序和功能”卸载，或重新启动计算机。
setup-update-exe-missing = 此安装程序缺少关键的二进制文件 (Update.exe)。请联系应用程序作者。
setup-main-exe-missing = 在程序包中找不到主可执行文件。请联系应用程序作者。

# MSI Installer UI - Common
msi-dlg-title = { $app_title } 安装程序
msi-btn-back = 上一步(&B)
msi-btn-next = 下一步(&N)
msi-btn-cancel = 取消
msi-btn-finish = 完成(&F)
msi-btn-ok = 确定
msi-btn-yes = 是(&Y)
msi-btn-no = 否(&N)
msi-btn-retry = 重试(&R)
msi-btn-ignore = 忽略(&I)

# MSI Installer UI - Welcome Dialog
msi-welcome-title = 欢迎使用 { $app_title } 安装向导
msi-welcome-description = 安装向导将在您的计算机上安装 { $app_title }。单击“下一步”继续，或单击“取消”退出安装向导。
msi-welcome-update-description = 安装向导将更新您计算机上的 { $app_title }。单击“下一步”继续，或单击“取消”退出安装向导。

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } 安装向导已完成
msi-exit-description = 单击“完成”按钮退出安装向导。
msi-exit-launch-checkbox = 启动 { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = 欢迎使用 { $app_title } 安装向导
msi-prepare-description = 安装向导正准备指导您完成安装过程，请稍候。

# MSI Installer UI - License Agreement Dialog
msi-license-title = 最终用户许可协议
msi-license-description = 请认真阅读以下许可协议。
msi-license-checkbox = 我接受许可协议中的条款(&A)

# MSI Installer UI - Install Scope Dialog
msi-scope-title = 安装范围
msi-scope-description = 选择安装范围。
msi-scope-per-user = 仅为您安装(&Y)
msi-scope-per-machine = 为所有用户安装(&A)
msi-scope-per-user-description = 仅为当前用户安装
msi-scope-no-per-user-description = 需要管理员权限
msi-scope-per-machine-description = 需要管理员权限

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = 已准备好安装 { $app_title }
msi-ready-install-text = 单击“安装”开始安装。单击“上一步”查看或更改任何安装设置。
msi-ready-change-title = 已准备好更改 { $app_title }
msi-ready-change-text = 单击“更改”开始更改安装。单击“上一步”查看或更改任何安装设置。
msi-ready-repair-title = 已准备好修复 { $app_title }
msi-ready-repair-text = 单击“修复”开始修复。单击“上一步”查看或更改任何安装设置。
msi-ready-remove-title = 已准备好删除 { $app_title }
msi-ready-remove-text = 单击“删除”可从计算机上删除 { $app_title }。单击“上一步”查看或更改任何安装设置。
msi-ready-update-title = 已准备好更新 { $app_title }
msi-ready-update-text = 单击“更新”开始更新。单击“上一步”查看或更改任何安装设置。
msi-ready-btn-install = 安装(&I)
msi-ready-btn-change = 更改(&C)
msi-ready-btn-repair = 修复(&R)
msi-ready-btn-remove = 删除(&R)
msi-ready-btn-update = 更新(&U)

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = 正在安装 { $app_title }
msi-progress-installing-text = 安装向导正在安装 { $app_title }，请稍候。
msi-progress-changing-title = 正在更改 { $app_title }
msi-progress-changing-text = 安装向导正在更改 { $app_title }，请稍候。
msi-progress-repairing-title = 正在修复 { $app_title }
msi-progress-repairing-text = 安装向导正在修复 { $app_title }，请稍候。
msi-progress-removing-title = 正在删除 { $app_title }
msi-progress-removing-text = 安装向导正在删除 { $app_title }，请稍候。
msi-progress-updating-title = 正在更新 { $app_title }
msi-progress-updating-text = 安装向导正在更新 { $app_title }，请稍候。
msi-progress-status = 状态:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = 欢迎使用 { $app_title } 安装向导
msi-maint-welcome-description = 安装向导允许您修复或删除 { $app_title }。单击“下一步”继续，或单击“取消”退出安装向导。

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = 更改、修复或删除安装
msi-maint-type-description = 选择希望执行的操作。
msi-maint-change-button = 更改(&C)...
msi-maint-change-tooltip = 更改...
msi-maint-change-text = 允许用户更改要安装的程序功能以及更改单个功能。
msi-maint-change-disabled = 更改当前已禁用。
msi-maint-repair-button = 修复(&R)
msi-maint-repair-tooltip = 修复
msi-maint-repair-text = 修复最近安装中的错误 - 修复丢失或损坏的文件、快捷方式和注册表项。
msi-maint-repair-disabled = 修复当前已禁用。
msi-maint-remove-button = 删除(&M)
msi-maint-remove-tooltip = 删除
msi-maint-remove-text = 从您的计算机中删除 { $app_title }。
msi-maint-remove-disabled = 删除当前已禁用。

# MSI Installer UI - Cancel Dialog
msi-cancel-text = 是否确实要取消安装 { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = 更改当前目标文件夹
msi-browse-description = 浏览到目标文件夹。
msi-browse-combo-label = 查找范围(&L):
msi-browse-path-label = 文件夹名称(&D):
msi-browse-up-tooltip = 向上一级
msi-browse-new-folder-tooltip = 新建文件夹

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = 指定的目标目录无效或位于不支持的驱动器类型上。

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = 磁盘空间要求
msi-disk-cost-description = 安装所选功能所需的磁盘空间。
msi-disk-cost-text = 突出显示的卷没有足够的磁盘空间用于当前所选功能。您可以从突出显示的卷中删除一些文件，安装较少的功能，或者选择其他目标驱动器。

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } 安装程序信息

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } 安装向导提前结束
msi-fatal-description1 = { $app_title } 安装程序已中断。您的系统尚未修改。若要稍后安装该程序，请再次运行安装程序。
msi-fatal-description2 = 单击“完成”按钮退出安装向导。

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } 安装向导已中断
msi-user-exit-description1 = { $app_title } 安装程序已中断。您的系统尚未修改。若要稍后安装该程序，请再次运行安装程序。
msi-user-exit-description2 = 单击“完成”按钮退出安装向导。

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = 使用中的文件
msi-files-in-use-description = 一些需要更新的文件当前正在使用中。
msi-files-in-use-text = 以下应用程序正在使用需要通过此安装程序更新的文件。请关闭这些应用程序，然后单击“重试”继续安装，或单击“取消”退出安装。
msi-files-in-use-exit = 退出(&X)

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = 使用中的文件
msi-rm-files-in-use-description = 一些需要更新的文件当前正在使用中。
msi-rm-files-in-use-text = 以下应用程序正在使用此安装程序需要更新的文件。可以让安装向导自动关闭并尝试重新启动这些应用程序，也可以手动关闭它们后单击“确定”继续安装。
msi-rm-files-in-use-use-rm = 自动关闭应用程序(&C)，并在安装完成后尝试重新启动它们。
msi-rm-files-in-use-dont-use-rm = 不关闭应用程序(&D)。(需要重新启动。)

# MSI Installer UI - Resume Dialog
msi-resume-title = 正在继续 { $app_title } 安装向导
msi-resume-description = 安装向导将在您的计算机上完成 { $app_title } 的安装。请单击“安装”继续，或单击“取消”退出安装向导。
msi-resume-btn-install = 安装(&I)

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } 桌面快捷方式
msi-start-menu-shortcut-description = { $app_title } 开始菜单快捷方式
# MSI Installer UI - Readme Dialog
msi-readme-title = 自述信息
msi-readme-description = 请在继续之前阅读以下信息。
