# Shared titles
title-update = { $app_title } の更新
title-setup = { $app_title } セットアップ
title-uninstall = { $app_title } のアンインストール
error-title = { $program_name } エラー

# Shared buttons
btn-cancel = キャンセル
btn-install-update = 更新をインストール
btn-install = インストール
btn-update = 更新
btn-downgrade = ダウングレード
btn-repair = 修復
btn-open-log = ログを開く
btn-open-install-dir = インストール フォルダーを開く

# Elevation (dialogs_common.rs)
elevate-header = 管理者権限が必要です
elevate-body = { $app_title } のバージョン { $app_version } をインストールするには管理者権限が必要です。この更新を続行することを許可しますか?

# Restart required (prerequisite.rs)
restart-header = 再起動が必要です
restart-body = セットアップを続行する前にコンピューターを再起動する必要があります。コンピューターを再起動してからセットアップを再度実行してください。

# Missing dependencies (prerequisite.rs)
missing-deps-header = 追加のコンポーネントが必要です
missing-deps-body = { $app_title } を実行するには、まず以下をインストールする必要があります: { $deps }。今すぐダウンロードしてインストールしますか?

# Uninstall with errors (uninstall)
uninstall-errors-header = アンインストールが問題と共に完了しました
uninstall-errors-body = { $app_title } はアンインストールされましたが、一部のファイルまたはフォルダーを削除できませんでした。手動で削除するか、アプリケーションを再インストールしてからアンインストールを再試行してください。
uninstall-errors-log = 詳細は次に保存されました: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } は既にインストールされています
overwrite-repair-body = このアプリケーションは既にコンピューターにインストールされています。正常に動作していない場合は、再インストールによる修復を試すことができます。
overwrite-older-installed = { $app_title } は既にインストールされています
overwrite-update-body = 現在バージョン { $old_version } がインストールされています。バージョン { $app_version } に更新しますか?
overwrite-newer-installed = { $app_title } のより新しいバージョンが既にインストールされています
overwrite-downgrade-body = 現在バージョン { $old_version } がインストールされており、このインストーラーよりも新しいバージョンです。ダウングレードは推奨されておらず、問題を引き起こす可能性があります。それでも続行しますか?
overwrite-footer = インストール場所: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = アンインストール完了
uninstall-body = アプリケーションはコンピューターから正常に削除されました。

# Install hook failed (install.rs)
install-hook-header = インストールが部分的に成功しました
install-hook-body = インストールは完了しましたが、一部の手順が失敗した可能性があります。アプリケーションが正常に動作しない場合は、再インストールするか、アプリケーションの作者に連絡してください。

# Splash fallback (splash.rs)
splash-header = { $app_title } をインストールしています
splash-body = { $app_title } { $app_version } をセットアップしています。しばらくお待ちください...

# Dependency download (prerequisite.rs)
deps-download-header = 必要なコンポーネントをダウンロード中
deps-download-body = { $dep_name } をダウンロードしています。しばらくお待ちください...

# Apply progress (apply_*_impl.rs)
apply-header = 更新をインストール中
apply-body = バージョン { $app_version } に更新しています。しばらくお待ちください...

# Start error (start_windows_impl.rs)
start-corrupt-header = インストールが破損しています
start-corrupt-body = 一部のファイルが見つからないか破損しているため、このアプリケーションを起動できません。これを修正するには、アプリケーションを再インストールしてください。

# Generic error
error-header = 問題が発生しました

# Setup error (wix msi)
setup-error-header = セットアップを続行できませんでした

# MSI Installer UI - Common
msi-dlg-title = { $app_title } セットアップ
msi-btn-back = 戻る(&B)
msi-btn-next = 次へ(&N)
msi-btn-cancel = キャンセル
msi-btn-finish = 完了(&F)
msi-btn-ok = OK
msi-btn-yes = はい(&Y)
msi-btn-no = いいえ(&N)
msi-btn-retry = 再試行(&R)
msi-btn-ignore = 無視(&I)

# MSI Installer UI - Welcome Dialog
msi-welcome-title = { $app_title } セットアップ ウィザードへようこそ
msi-welcome-description = このセットアップ ウィザードでは、{ $app_title } をコンピューターにインストールします。続行するには [次へ] を、セットアップ ウィザードを終了するには [キャンセル] をクリックしてください。
msi-welcome-update-description = このセットアップ ウィザードでは、コンピューターの { $app_title } を更新します。続行するには [次へ] を、セットアップ ウィザードを終了するには [キャンセル] をクリックしてください。

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } セットアップ ウィザードが完了しました
msi-exit-description = セットアップ ウィザードを終了するには [完了] ボタンをクリックしてください。
msi-exit-launch-checkbox = { $app_title } を起動

# MSI Installer UI - Prepare Dialog
msi-prepare-title = { $app_title } セットアップ ウィザードへようこそ
msi-prepare-description = セットアップ ウィザードがインストールの準備をしている間、しばらくお待ちください。

# MSI Installer UI - License Agreement Dialog
msi-license-title = 使用許諾契約書
msi-license-description = 以下の使用許諾契約書をよくお読みください。
msi-license-checkbox = 使用許諾契約書に同意します(&A)

# MSI Installer UI - Install Scope Dialog
msi-scope-title = インストール範囲
msi-scope-description = インストール範囲を選択してください。
msi-scope-per-user = 自分のみにインストール(&Y)
msi-scope-per-machine = すべてのユーザーにインストール(&A)
msi-scope-per-user-description = 現在のユーザーのみにインストールします
msi-scope-no-per-user-description = 管理者権限が必要です
msi-scope-per-machine-description = 管理者権限が必要です

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = { $app_title } のインストール準備完了
msi-ready-install-text = インストールを開始するには [インストール] をクリックしてください。インストール設定を確認または変更するには [戻る] をクリックしてください。
msi-ready-change-title = { $app_title } の変更準備完了
msi-ready-change-text = インストールの変更を開始するには [変更] をクリックしてください。インストール設定を確認または変更するには [戻る] をクリックしてください。
msi-ready-repair-title = { $app_title } の修復準備完了
msi-ready-repair-text = 修復を開始するには [修復] をクリックしてください。インストール設定を確認または変更するには [戻る] をクリックしてください。
msi-ready-remove-title = { $app_title } の削除準備完了
msi-ready-remove-text = { $app_title } をコンピューターから削除するには [削除] をクリックしてください。インストール設定を確認または変更するには [戻る] をクリックしてください。
msi-ready-update-title = { $app_title } の更新準備完了
msi-ready-update-text = 更新を開始するには [更新] をクリックしてください。インストール設定を確認または変更するには [戻る] をクリックしてください。
msi-ready-btn-install = インストール(&I)
msi-ready-btn-change = 変更(&C)
msi-ready-btn-repair = 修復(&R)
msi-ready-btn-remove = 削除(&R)
msi-ready-btn-update = 更新(&U)

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } をインストール中
msi-progress-installing-text = セットアップ ウィザードが { $app_title } をインストールしている間、しばらくお待ちください。
msi-progress-changing-title = { $app_title } を変更中
msi-progress-changing-text = セットアップ ウィザードが { $app_title } を変更している間、しばらくお待ちください。
msi-progress-repairing-title = { $app_title } を修復中
msi-progress-repairing-text = セットアップ ウィザードが { $app_title } を修復している間、しばらくお待ちください。
msi-progress-removing-title = { $app_title } を削除中
msi-progress-removing-text = セットアップ ウィザードが { $app_title } を削除している間、しばらくお待ちください。
msi-progress-updating-title = { $app_title } を更新中
msi-progress-updating-text = セットアップ ウィザードが { $app_title } を更新している間、しばらくお待ちください。
msi-progress-status = 状態:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = { $app_title } セットアップ ウィザードへようこそ
msi-maint-welcome-description = セットアップ ウィザードでは、{ $app_title } の修復または削除を行うことができます。続行するには [次へ] を、セットアップ ウィザードを終了するには [キャンセル] をクリックしてください。

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = インストールの変更、修復、または削除
msi-maint-type-description = 実行する操作を選択してください。
msi-maint-change-button = 変更(&C)...
msi-maint-change-tooltip = 変更...
msi-maint-change-text = インストールするプログラム機能や個々の機能を変更できます。
msi-maint-change-disabled = 変更は現在無効になっています。
msi-maint-repair-button = 修復(&R)
msi-maint-repair-tooltip = 修復
msi-maint-repair-text = 直近のインストールのエラーを修復します - 見つからないか壊れたファイル、ショートカット、レジストリ エントリを修正します。
msi-maint-repair-disabled = 修復は現在無効になっています。
msi-maint-remove-button = 削除(&M)
msi-maint-remove-tooltip = 削除
msi-maint-remove-text = { $app_title } をコンピューターから削除します。
msi-maint-remove-disabled = 削除は現在無効になっています。

# MSI Installer UI - Cancel Dialog
msi-cancel-text = { $app_title } のインストールをキャンセルしてもよろしいですか?

# MSI Installer UI - Browse Dialog
msi-browse-title = インストール先フォルダーの変更
msi-browse-description = インストール先フォルダーを参照します。
msi-browse-combo-label = 場所(&L):
msi-browse-path-label = フォルダー名(&D):
msi-browse-up-tooltip = 1 つ上のフォルダーへ移動
msi-browse-new-folder-tooltip = 新しいフォルダーの作成

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = 指定されたインストール先ディレクトリが無効か、サポートされていないドライブ タイプにあります。

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = 必要なディスク領域
msi-disk-cost-description = 選択された機能のインストールに必要なディスク領域です。
msi-disk-cost-text = 強調表示されたボリュームには、現在選択中の機能に利用できる十分なディスク領域がありません。強調表示されたボリュームから一部のファイルを削除するか、インストールする機能を減らすか、別のインストール先ドライブを選択することができます。

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } インストーラー情報

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } セットアップ ウィザードが途中で終了しました
msi-fatal-description1 = { $app_title } のセットアップが中断されました。システムは変更されていません。このプログラムを後でインストールするには、セットアップを再度実行してください。
msi-fatal-description2 = セットアップ ウィザードを終了するには [完了] ボタンをクリックしてください。

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } セットアップ ウィザードが中断されました
msi-user-exit-description1 = { $app_title } のセットアップが中断されました。システムは変更されていません。このプログラムを後でインストールするには、セットアップを再度実行してください。
msi-user-exit-description2 = セットアップ ウィザードを終了するには [完了] ボタンをクリックしてください。

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = 使用中のファイル
msi-files-in-use-description = 更新する必要のある一部のファイルは現在使用中です。
msi-files-in-use-text = 以下のアプリケーションが、このセットアップで更新する必要のあるファイルを使用しています。これらのアプリケーションを閉じてから、[再試行] をクリックしてインストールを続行するか、[キャンセル] をクリックして終了してください。
msi-files-in-use-exit = 終了(&X)

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = 使用中のファイル
msi-rm-files-in-use-description = 更新する必要のある一部のファイルは現在使用中です。
msi-rm-files-in-use-text = 以下のアプリケーションが、このセットアップで更新する必要のあるファイルを使用しています。セットアップ ウィザードに自動的にこれらのアプリケーションを閉じて再起動を試みさせるか、手動で閉じてから [OK] をクリックしてインストールを続行することができます。
msi-rm-files-in-use-use-rm = アプリケーションを自動的に閉じて(&C)、セットアップ完了後に再起動を試みる。
msi-rm-files-in-use-dont-use-rm = アプリケーションを閉じない(&D)。(再起動が必要になります。)

# MSI Installer UI - Resume Dialog
msi-resume-title = { $app_title } セットアップ ウィザードの再開
msi-resume-description = セットアップ ウィザードは { $app_title } のインストールをコンピューター上で完了します。続行するには [インストール] を、セットアップ ウィザードを終了するには [キャンセル] をクリックしてください。
msi-resume-btn-install = インストール(&I)

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } のデスクトップ ショートカット
msi-start-menu-shortcut-description = { $app_title } のスタート メニュー ショートカット
# MSI Installer UI - Readme Dialog
msi-readme-title = お読みください
msi-readme-description = 続行する前に、以下の情報をお読みください。
