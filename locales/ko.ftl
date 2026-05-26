# Shared titles
title-update = { $app_title } 업데이트
title-setup = { $app_title } 설치
title-uninstall = { $app_title } 제거
error-title = { $program_name } 오류

# Shared buttons
btn-cancel = 취소
btn-install-update = 업데이트 설치
btn-install = 설치
btn-update = 업데이트
btn-downgrade = 다운그레이드
btn-repair = 복구
btn-open-log = 로그 열기
btn-open-install-dir = 설치 폴더 열기
btn-ok = 확인
# Elevation (dialogs_common.rs)
elevate-header = 관리자 권한이 필요합니다
elevate-body = { $app_title } 버전 { $app_version }을(를) 설치하려면 관리자 권한이 필요합니다. 이 업데이트를 계속하도록 허용하시겠습니까?

# Restart required (prerequisite.rs)
restart-header = 다시 시작해야 합니다
restart-body = 설치를 계속하려면 컴퓨터를 다시 시작해야 합니다. 컴퓨터를 다시 시작한 후 설치를 다시 실행하십시오.

# Missing dependencies (prerequisite.rs)
missing-deps-header = 추가 구성 요소가 필요합니다
missing-deps-body = { $app_title }을(를) 실행하려면 먼저 다음을 설치해야 합니다: { $deps }. 지금 다운로드하여 설치하시겠습니까?

# Uninstall with errors (uninstall)
uninstall-errors-header = 제거가 문제와 함께 완료되었습니다
uninstall-errors-body = { $app_title }이(가) 제거되었지만 일부 파일이나 폴더를 제거할 수 없습니다. 수동으로 삭제하거나 응용 프로그램을 다시 설치한 후 제거를 다시 시도할 수 있습니다.
uninstall-errors-log = 세부 정보가 다음에 저장되었습니다: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title }이(가) 이미 설치되어 있습니다
overwrite-repair-body = 이 응용 프로그램은 이미 컴퓨터에 설치되어 있습니다. 올바르게 작동하지 않는 경우 다시 설치하여 복구를 시도할 수 있습니다.
overwrite-older-installed = { $app_title }이(가) 이미 설치되어 있습니다
overwrite-update-body = 현재 버전 { $old_version }이(가) 설치되어 있습니다. 버전 { $app_version }(으)로 업데이트하시겠습니까?
overwrite-newer-installed = { $app_title }의 최신 버전이 이미 설치되어 있습니다
overwrite-downgrade-body = 현재 버전 { $old_version }이(가) 설치되어 있으며, 이 설치 프로그램보다 최신 버전입니다. 다운그레이드는 권장되지 않으며 문제가 발생할 수 있습니다. 계속하시겠습니까?
overwrite-footer = 설치 위치: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = 제거 완료
uninstall-body = 응용 프로그램이 컴퓨터에서 성공적으로 제거되었습니다.

# Install hook failed (install.rs)
install-hook-header = 설치가 부분적으로 성공했습니다
install-hook-body = 설치가 완료되었지만 일부 단계가 실패했을 수 있습니다. 응용 프로그램이 올바르게 작동하지 않는 경우 다시 설치하거나 응용 프로그램 작성자에게 문의하십시오.

# Splash fallback (splash.rs)
splash-header = { $app_title } 설치 중
splash-body = { $app_title } { $app_version }을(를) 설정하는 중입니다. 잠시 기다려 주십시오...

# Dependency download (prerequisite.rs)
deps-download-header = 필수 구성 요소 다운로드 중
deps-download-body = { $dep_name }을(를) 다운로드하는 중입니다. 잠시 기다려 주십시오...

# Apply progress (apply_*_impl.rs)
apply-header = 업데이트 설치 중
apply-body = 버전 { $app_version }(으)로 업데이트하는 중입니다. 잠시 기다려 주십시오...

# Start error (start_windows_impl.rs)
start-corrupt-header = 설치가 손상되었습니다
start-corrupt-body = 일부 파일이 없거나 손상되어 이 응용 프로그램을 시작할 수 없습니다. 이 문제를 해결하려면 응용 프로그램을 다시 설치하십시오.

# Generic error
error-header = 문제가 발생했습니다

# Setup error (wix msi)
setup-error-header = 설치를 계속할 수 없습니다

# MSI Installer UI - Common
msi-dlg-title = { $app_title } 설치
msi-btn-back = 뒤로(&B)
msi-btn-next = 다음(&N)
msi-btn-cancel = 취소
msi-btn-finish = 마침(&F)
msi-btn-ok = 확인
msi-btn-yes = 예(&Y)
msi-btn-no = 아니요(&N)
msi-btn-retry = 다시 시도(&R)
msi-btn-ignore = 무시(&I)

# MSI Installer UI - Welcome Dialog
msi-welcome-title = { $app_title } 설치 마법사 시작
msi-welcome-description = 설치 마법사가 { $app_title }을(를) 컴퓨터에 설치합니다. 계속하려면 [다음]을 클릭하고 설치 마법사를 끝내려면 [취소]를 클릭하십시오.
msi-welcome-update-description = 설치 마법사에서 컴퓨터의 { $app_title }을(를) 업데이트합니다. 계속하려면 [다음]을 클릭하고 설치 마법사를 끝내려면 [취소]를 클릭하십시오.

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } 설치 마법사를 완료했습니다
msi-exit-description = 설치 마법사를 끝내려면 [마침] 단추를 클릭하십시오.
msi-exit-launch-checkbox = { $app_title } 실행

# MSI Installer UI - Prepare Dialog
msi-prepare-title = { $app_title } 설치 마법사 시작
msi-prepare-description = 설치 마법사가 설치 안내를 준비하는 동안 잠시 기다려 주십시오.

# MSI Installer UI - License Agreement Dialog
msi-license-title = 최종 사용자 사용권 계약
msi-license-description = 다음 사용 조건을 자세히 읽어 주십시오.
msi-license-checkbox = 사용권 계약 조항에 동의함(&A)

# MSI Installer UI - Install Scope Dialog
msi-scope-title = 설치 범위
msi-scope-description = 설치 범위를 선택하십시오.
msi-scope-per-user = 사용자에 대해서만 설치(&Y)
msi-scope-per-machine = 모든 사용자에 대해 설치(&A)
msi-scope-per-user-description = 현재 사용자에 대해서만 설치합니다
msi-scope-no-per-user-description = 관리자 권한이 필요합니다
msi-scope-per-machine-description = 관리자 권한이 필요합니다

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = { $app_title } 설치 준비 완료
msi-ready-install-text = 설치를 시작하려면 [설치]를 클릭하십시오. 설치 설정을 검토하거나 변경하려면 [뒤로]를 클릭하십시오.
msi-ready-change-title = { $app_title } 변경 준비 완료
msi-ready-change-text = 설치 변경을 시작하려면 [변경]을 클릭하십시오. 설치 설정을 검토하거나 변경하려면 [뒤로]를 클릭하십시오.
msi-ready-repair-title = { $app_title } 복구 준비 완료
msi-ready-repair-text = 복구를 시작하려면 [복구]를 클릭하십시오. 설치 설정을 검토하거나 변경하려면 [뒤로]를 클릭하십시오.
msi-ready-remove-title = { $app_title } 제거 준비 완료
msi-ready-remove-text = 컴퓨터에서 { $app_title }을(를) 제거하려면 [제거]를 클릭하십시오. 설치 설정을 검토하거나 변경하려면 [뒤로]를 클릭하십시오.
msi-ready-update-title = { $app_title } 업데이트 준비 완료
msi-ready-update-text = 업데이트를 시작하려면 [업데이트]를 클릭하십시오. 설치 설정을 검토하거나 변경하려면 [뒤로]를 클릭하십시오.
msi-ready-btn-install = 설치(&I)
msi-ready-btn-change = 변경(&C)
msi-ready-btn-repair = 복구(&R)
msi-ready-btn-remove = 제거(&R)
msi-ready-btn-update = 업데이트(&U)

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } 설치 중
msi-progress-installing-text = 설치 마법사가 { $app_title }을(를) 설치하는 동안 잠시 기다려 주십시오.
msi-progress-changing-title = { $app_title } 변경 중
msi-progress-changing-text = 설치 마법사가 { $app_title }을(를) 변경하는 동안 잠시 기다려 주십시오.
msi-progress-repairing-title = { $app_title } 복구 중
msi-progress-repairing-text = 설치 마법사가 { $app_title }을(를) 복구하는 동안 잠시 기다려 주십시오.
msi-progress-removing-title = { $app_title } 제거 중
msi-progress-removing-text = 설치 마법사가 { $app_title }을(를) 제거하는 동안 잠시 기다려 주십시오.
msi-progress-updating-title = { $app_title } 업데이트 중
msi-progress-updating-text = 설치 마법사가 { $app_title }을(를) 업데이트하는 동안 잠시 기다려 주십시오.
msi-progress-status = 상태:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = { $app_title } 설치 마법사 시작
msi-maint-welcome-description = 설치 마법사로 { $app_title }을(를) 복구하거나 제거할 수 있습니다. 계속하려면 [다음]을 클릭하고 설치 마법사를 끝내려면 [취소]를 클릭하십시오.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = 설치 변경, 복구 또는 제거
msi-maint-type-description = 수행할 작업을 선택하십시오.
msi-maint-change-button = 변경(&C)...
msi-maint-change-tooltip = 변경...
msi-maint-change-text = 설치된 프로그램 기능 및 개별 기능을 변경할 수 있습니다.
msi-maint-change-disabled = 변경은 현재 사용할 수 없습니다.
msi-maint-repair-button = 복구(&R)
msi-maint-repair-tooltip = 복구
msi-maint-repair-text = 가장 최근 설치의 오류를 복구합니다. 손실되거나 손상된 파일, 바로 가기 및 레지스트리 항목을 수정합니다.
msi-maint-repair-disabled = 복구는 현재 사용할 수 없습니다.
msi-maint-remove-button = 제거(&M)
msi-maint-remove-tooltip = 제거
msi-maint-remove-text = 컴퓨터에서 { $app_title }을(를) 제거합니다.
msi-maint-remove-disabled = 제거는 현재 사용할 수 없습니다.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = { $app_title } 설치를 취소하시겠습니까?

# MSI Installer UI - Browse Dialog
msi-browse-title = 현재 대상 폴더 변경
msi-browse-description = 대상 폴더로 이동합니다.
msi-browse-combo-label = 찾는 위치(&L):
msi-browse-path-label = 폴더 이름(&D):
msi-browse-up-tooltip = 한 수준 위로
msi-browse-new-folder-tooltip = 새 폴더 만들기

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = 지정된 대상 디렉터리가 잘못되었거나 지원되지 않는 드라이브 유형에 있습니다.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = 디스크 공간 요구 사항
msi-disk-cost-description = 선택한 기능을 설치하는 데 필요한 디스크 공간입니다.
msi-disk-cost-text = 선택한 볼륨의 디스크 공간이 부족하여 현재 선택한 기능을 설치할 수 없습니다. 선택한 볼륨에서 일부 파일을 제거하거나, 선택한 기능 중 일부를 취소하거나, 다른 대상 드라이브를 선택하십시오.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } 설치 관리자 정보

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } 설치 마법사가 중간에 중단되었습니다
msi-fatal-description1 = { $app_title } 설치가 중단되었습니다. 시스템이 수정되지 않았습니다. 나중에 이 프로그램을 설치하려면 설치 프로그램을 다시 실행하십시오.
msi-fatal-description2 = 설치 마법사를 끝내려면 [마침] 단추를 클릭하십시오.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } 설치 마법사가 중단되었습니다
msi-user-exit-description1 = { $app_title } 설치가 중단되었습니다. 시스템이 수정되지 않았습니다. 나중에 이 프로그램을 설치하려면 설치 프로그램을 다시 실행하십시오.
msi-user-exit-description2 = 설치 마법사를 끝내려면 [마침] 단추를 클릭하십시오.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = 사용 중인 파일
msi-files-in-use-description = 업데이트해야 할 일부 파일을 현재 사용하고 있습니다.
msi-files-in-use-text = 이 설치를 통해 업데이트해야 하는 파일을 다음 응용 프로그램에서 사용하고 있습니다. 해당 응용 프로그램을 닫은 후 [다시 시도]를 클릭하여 설치를 계속하거나 [취소]를 클릭하여 끝내십시오.
msi-files-in-use-exit = 끝내기(&X)

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = 사용 중인 파일
msi-rm-files-in-use-description = 업데이트해야 할 일부 파일을 현재 사용하고 있습니다.
msi-rm-files-in-use-text = 이 설치 프로그램이 업데이트해야 하는 파일을 다음 응용 프로그램에서 사용하고 있습니다. 설치 마법사가 자동으로 해당 응용 프로그램을 닫고 다시 시작하도록 하거나, 수동으로 닫은 후 [확인]을 클릭하여 설치를 계속할 수 있습니다.
msi-rm-files-in-use-use-rm = 응용 프로그램을 자동으로 닫고(&C) 설치가 완료된 후 다시 시작을 시도합니다.
msi-rm-files-in-use-dont-use-rm = 응용 프로그램을 닫지 않습니다(&D). (다시 부팅해야 합니다.)

# MSI Installer UI - Resume Dialog
msi-resume-title = { $app_title } 설치 마법사를 계속하는 중
msi-resume-description = 설치 마법사가 컴퓨터에서 { $app_title } 설치를 완료합니다. 계속하려면 [설치]를 클릭하고 설치 마법사를 끝내려면 [취소]를 클릭하십시오.
msi-resume-btn-install = 설치(&I)

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } 바탕 화면 바로 가기
msi-start-menu-shortcut-description = { $app_title } 시작 메뉴 바로 가기
# MSI Installer UI - Readme Dialog
msi-readme-title = 추가 정보
msi-readme-description = 계속하기 전에 다음 정보를 읽어 주십시오.
