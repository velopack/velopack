# Shared titles
title-update = Atualização do { $app_title }
title-setup = Instalação do { $app_title }
title-uninstall = Desinstalação do { $app_title }
error-title = Erro do { $program_name }

# Shared buttons
btn-cancel = Cancelar
btn-install-update = Instalar Atualização
btn-install = Instalar
btn-update = Atualizar
btn-downgrade = Reverter versão
btn-repair = Reparar
btn-open-log = Abrir Registo
btn-open-install-dir = Abrir Diretório de Instalação

# Elevation (dialogs_common.rs)
elevate-header = Permissão de Administrador Necessária
elevate-body = O { $app_title } necessita de permissão de administrador para instalar a versão { $app_version }. Permitir que esta atualização continue?

# Restart required (prerequisite.rs)
restart-header = Reinício Necessário
restart-body = O computador precisa de ser reiniciado antes que a instalação possa continuar. Reinicie o computador e execute a instalação novamente.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Componentes Adicionais Necessários
missing-deps-body = O { $app_title } necessita que os seguintes itens sejam instalados primeiro: { $deps }. Pretende transferi-los e instalá-los agora?

# Uninstall with errors (uninstall)
uninstall-errors-header = Desinstalação Concluída com Problemas
uninstall-errors-body = O { $app_title } foi desinstalado, mas alguns ficheiros ou pastas não puderam ser removidos. Pode eliminá-los manualmente ou reinstalar a aplicação e tentar desinstalá-la novamente.
uninstall-errors-log = Os detalhes foram guardados em: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = O { $app_title } já está instalado
overwrite-repair-body = Esta aplicação já está instalada no seu computador. Se não estiver a funcionar corretamente, pode tentar repará-la reinstalando-a.
overwrite-older-installed = O { $app_title } já está instalado
overwrite-update-body = A versão { $old_version } está atualmente instalada. Pretende atualizar para a versão { $app_version }?
overwrite-newer-installed = Já está instalada uma versão mais recente do { $app_title }
overwrite-downgrade-body = A versão { $old_version } está atualmente instalada e é mais recente do que este instalador. Reverter a versão não é recomendado e pode causar problemas. Continuar mesmo assim?
overwrite-footer = Instalado em: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Desinstalação Concluída
uninstall-body = A aplicação foi removida com sucesso do seu computador.

# Install hook failed (install.rs)
install-hook-header = Instalação Parcialmente Bem-sucedida
install-hook-body = A instalação foi concluída, mas algumas etapas podem ter falhado. Se a aplicação não funcionar corretamente, pode tentar reinstalá-la ou contactar o autor da aplicação.

# Splash fallback (splash.rs)
splash-header = A instalar o { $app_title }
splash-body = A configurar o { $app_title } { $app_version }, aguarde...

# Dependency download (prerequisite.rs)
deps-download-header = A transferir o componente necessário
deps-download-body = A transferir { $dep_name }, aguarde...

# Apply progress (apply_*_impl.rs)
apply-header = A instalar a atualização
apply-body = A atualizar para a versão { $app_version }, aguarde...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalação Danificada
start-corrupt-body = Esta aplicação não pode iniciar porque alguns dos seus ficheiros estão em falta ou danificados. Reinstale a aplicação para corrigir isto.

# Generic error
error-header = Algo Correu Mal

# Setup error (wix msi)
setup-error-header = A Instalação Não Pôde Continuar

# MSI Installer UI - Common
msi-dlg-title = Instalação do { $app_title }
msi-btn-back = &Anterior
msi-btn-next = &Seguinte
msi-btn-cancel = Cancelar
msi-btn-finish = &Concluir
msi-btn-ok = OK
msi-btn-yes = &Sim
msi-btn-no = &Não
msi-btn-retry = &Repetir
msi-btn-ignore = &Ignorar

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Bem-vindo ao Assistente de Configuração do { $app_title }
msi-welcome-description = O Assistente de Configuração vai instalar o { $app_title } no seu computador. Clique em Seguinte para continuar ou em Cancelar para sair do Assistente de Configuração.
msi-welcome-update-description = O Assistente de Configuração atualizará o { $app_title } no computador. Clique em Seguinte para continuar ou em Cancelar para sair do Assistente de Configuração.

# MSI Installer UI - Exit Dialog
msi-exit-title = Concluiu o Assistente de Configuração do { $app_title }
msi-exit-description = Clique no botão Concluir para sair do Assistente de Configuração.
msi-exit-launch-checkbox = Iniciar o { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Bem-vindo ao Assistente de Configuração do { $app_title }
msi-prepare-description = Aguarde enquanto o Assistente de Configuração se prepara para o orientar através da instalação.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Contrato de Licença do Utilizador Final
msi-license-description = Leia atentamente o seguinte contrato de licença.
msi-license-checkbox = &Aceito os termos do Contrato de Licença

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Âmbito de Instalação
msi-scope-description = Selecione o âmbito de instalação.
msi-scope-per-user = In&stalar apenas para si
msi-scope-per-machine = Instalar para &todos os utilizadores
msi-scope-per-user-description = Instala apenas para o utilizador atual
msi-scope-no-per-user-description = Requer privilégios de administrador
msi-scope-per-machine-description = Requer privilégios de administrador

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Pronto para instalar o { $app_title }
msi-ready-install-text = Clique em Instalar para iniciar a instalação. Clique em Anterior para rever ou alterar qualquer uma das definições de instalação.
msi-ready-change-title = Pronto para alterar o { $app_title }
msi-ready-change-text = Clique em Alterar para iniciar a alteração da instalação. Clique em Anterior para rever ou alterar qualquer uma das definições de instalação.
msi-ready-repair-title = Pronto para reparar o { $app_title }
msi-ready-repair-text = Clique em Reparar para iniciar a reparação. Clique em Anterior para rever ou alterar qualquer uma das definições de instalação.
msi-ready-remove-title = Pronto para remover o { $app_title }
msi-ready-remove-text = Clique em Remover para remover o { $app_title } do seu computador. Clique em Anterior para rever ou alterar qualquer uma das definições de instalação.
msi-ready-update-title = Pronto para atualizar o { $app_title }
msi-ready-update-text = Clique em Atualizar para iniciar a atualização. Clique em Anterior para rever ou alterar qualquer uma das definições de instalação.
msi-ready-btn-install = &Instalar
msi-ready-btn-change = Alt&erar
msi-ready-btn-repair = Re&parar
msi-ready-btn-remove = &Remover
msi-ready-btn-update = &Atualizar

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = A instalar o { $app_title }
msi-progress-installing-text = Aguarde enquanto o Assistente de Configuração instala o { $app_title }.
msi-progress-changing-title = A alterar o { $app_title }
msi-progress-changing-text = Aguarde enquanto o Assistente de Configuração altera o { $app_title }.
msi-progress-repairing-title = A reparar o { $app_title }
msi-progress-repairing-text = Aguarde enquanto o Assistente de Configuração repara o { $app_title }.
msi-progress-removing-title = A remover o { $app_title }
msi-progress-removing-text = Aguarde enquanto o Assistente de Configuração remove o { $app_title }.
msi-progress-updating-title = A atualizar o { $app_title }
msi-progress-updating-text = Aguarde enquanto o Assistente de Configuração atualiza o { $app_title }.
msi-progress-status = Estado:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Bem-vindo ao Assistente de Configuração do { $app_title }
msi-maint-welcome-description = O Assistente de Configuração permitir-lhe-á reparar ou remover o { $app_title }. Clique em Seguinte para continuar ou em Cancelar para sair do Assistente de Configuração.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Alterar, reparar ou remover a instalação
msi-maint-type-description = Selecione a operação que pretende executar.
msi-maint-change-button = Alt&erar...
msi-maint-change-tooltip = Alterar...
msi-maint-change-text = Permite aos utilizadores alterar quais as funcionalidades do programa que estão instaladas e alterar funcionalidades individuais.
msi-maint-change-disabled = Alterar está atualmente desativado.
msi-maint-repair-button = Re&parar
msi-maint-repair-tooltip = Reparar
msi-maint-repair-text = Repara erros na instalação mais recente - corrige ficheiros, atalhos e entradas do registo em falta ou danificados.
msi-maint-repair-disabled = Reparar está atualmente desativado.
msi-maint-remove-button = &Remover
msi-maint-remove-tooltip = Remover
msi-maint-remove-text = Remove o { $app_title } do seu computador.
msi-maint-remove-disabled = Remover está atualmente desativado.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Tem a certeza de que pretende cancelar a instalação do { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Alterar a pasta de destino atual
msi-browse-description = Navegar para a pasta de destino.
msi-browse-combo-label = &Procurar em:
msi-browse-path-label = &Nome da pasta:
msi-browse-up-tooltip = Subir um nível
msi-browse-new-folder-tooltip = Criar uma nova pasta

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = O diretório de destino especificado é inválido ou encontra-se num tipo de unidade não suportado.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Requisitos de Espaço em Disco
msi-disk-cost-description = O espaço em disco necessário para a instalação das funcionalidades selecionadas.
msi-disk-cost-text = Os volumes realçados não têm espaço em disco suficiente disponível para as funcionalidades atualmente selecionadas. Pode remover alguns ficheiros dos volumes realçados, escolher instalar menos funcionalidades nas unidades locais, ou selecionar unidades de destino diferentes.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informações do Instalador do { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = O Assistente de Configuração do { $app_title } terminou prematuramente
msi-fatal-description1 = A configuração do { $app_title } foi interrompida. O seu sistema não foi modificado. Para instalar este programa mais tarde, execute novamente a instalação.
msi-fatal-description2 = Clique no botão Concluir para sair do Assistente de Configuração.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = O Assistente de Configuração do { $app_title } foi interrompido
msi-user-exit-description1 = A configuração do { $app_title } foi interrompida. O seu sistema não foi modificado. Para instalar este programa mais tarde, execute novamente a instalação.
msi-user-exit-description2 = Clique no botão Concluir para sair do Assistente de Configuração.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Ficheiros em Utilização
msi-files-in-use-description = Alguns ficheiros que é necessário atualizar estão atualmente em utilização.
msi-files-in-use-text = As aplicações seguintes estão a utilizar ficheiros que necessitam de ser atualizados por esta configuração. Feche estas aplicações e, em seguida, clique em Repetir para continuar a instalação ou em Cancelar para sair.
msi-files-in-use-exit = Sai&r

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Ficheiros em Utilização
msi-rm-files-in-use-description = Alguns ficheiros que é necessário atualizar estão atualmente em utilização.
msi-rm-files-in-use-text = As aplicações seguintes estão a utilizar ficheiros que necessitam de ser atualizados por esta configuração. Pode deixar o Assistente de Configuração fechar automaticamente e tentar reiniciar estas aplicações, ou pode fechá-las manualmente e clicar em OK para continuar a instalação.
msi-rm-files-in-use-use-rm = &Fechar automaticamente as aplicações e tentar reiniciá-las após a conclusão da configuração.
msi-rm-files-in-use-dont-use-rm = &Não fechar as aplicações. (Será necessário reiniciar.)

# MSI Installer UI - Resume Dialog
msi-resume-title = A retomar o Assistente de Configuração do { $app_title }
msi-resume-description = O Assistente de Configuração irá concluir a instalação do { $app_title } no seu computador. Clique em Instalar para continuar ou em Cancelar para sair do Assistente de Configuração.
msi-resume-btn-install = &Instalar

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Atalho de Ambiente de Trabalho para o { $app_title }
msi-start-menu-shortcut-description = Atalho do Menu Iniciar para o { $app_title }
