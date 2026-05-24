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
btn-downgrade = Fazer Downgrade
btn-repair = Reparar
btn-open-log = Abrir Log
btn-open-install-dir = Abrir Diretório de Instalação

# Elevation (dialogs_common.rs)
elevate-header = Permissão de Administrador Necessária
elevate-body = O { $app_title } precisa de permissão de administrador para instalar a versão { $app_version }. Permitir que esta atualização continue?

# Restart required (prerequisite.rs)
restart-header = Reinicialização Necessária
restart-body = Seu computador precisa ser reiniciado antes que a instalação possa continuar. Reinicie seu computador e execute a instalação novamente.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Componentes Adicionais Necessários
missing-deps-body = O { $app_title } precisa que os seguintes itens sejam instalados primeiro: { $deps }. Deseja baixá-los e instalá-los agora?

# Uninstall with errors (uninstall)
uninstall-errors-header = Desinstalação Concluída com Problemas
uninstall-errors-body = O { $app_title } foi desinstalado, mas alguns arquivos ou pastas não puderam ser removidos. Você pode excluí-los manualmente ou reinstalar o aplicativo e tentar desinstalá-lo novamente.
uninstall-errors-log = Os detalhes foram salvos em: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = O { $app_title } já está instalado
overwrite-repair-body = Este aplicativo já está instalado no seu computador. Se ele não estiver funcionando corretamente, você pode tentar repará-lo reinstalando-o.
overwrite-older-installed = O { $app_title } já está instalado
overwrite-update-body = A versão { $old_version } está instalada atualmente. Deseja atualizar para a versão { $app_version }?
overwrite-newer-installed = Uma versão mais recente do { $app_title } já está instalada
overwrite-downgrade-body = A versão { $old_version } está instalada atualmente, que é mais recente que este instalador. Fazer downgrade não é recomendado e pode causar problemas. Continuar mesmo assim?
overwrite-footer = Instalado em: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Desinstalação Concluída
uninstall-body = O aplicativo foi removido com sucesso do seu computador.

# Install hook failed (install.rs)
install-hook-header = Instalação Parcialmente Bem-sucedida
install-hook-body = A instalação foi concluída, mas algumas etapas podem ter falhado. Se o aplicativo não funcionar corretamente, você pode tentar reinstalá-lo ou entrar em contato com o autor do aplicativo.

# Splash fallback (splash.rs)
splash-header = Instalando o { $app_title }
splash-body = Configurando o { $app_title } { $app_version }, aguarde...

# Dependency download (prerequisite.rs)
deps-download-header = Baixando Componente Necessário
deps-download-body = Baixando { $dep_name }, aguarde...

# Apply progress (apply_*_impl.rs)
apply-header = Instalando Atualização
apply-body = Atualizando para a versão { $app_version }, aguarde...

# Start error (start_windows_impl.rs)
start-corrupt-header = Instalação Danificada
start-corrupt-body = Este aplicativo não pode iniciar porque alguns de seus arquivos estão ausentes ou danificados. Reinstale o aplicativo para corrigir isso.

# Generic error
error-header = Algo Deu Errado

# Setup error (wix msi)
setup-error-header = A Instalação Não Pôde Continuar

# MSI Installer UI - Common
msi-dlg-title = Instalação do { $app_title }
msi-btn-back = &Voltar
msi-btn-next = &Avançar
msi-btn-cancel = Cancelar
msi-btn-finish = &Concluir
msi-btn-ok = OK
msi-btn-yes = &Sim
msi-btn-no = &Não
msi-btn-retry = Tenta&r Novamente
msi-btn-ignore = &Ignorar

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Bem-vindo ao Assistente para Instalação do { $app_title }
msi-welcome-description = O Assistente para Instalação vai instalar o { $app_title } no seu computador. Clique em Avançar para continuar ou em Cancelar para sair do Assistente para Instalação.
msi-welcome-update-description = O Assistente para Instalação atualizará o { $app_title } no seu computador. Clique em Avançar para continuar ou em Cancelar para sair do Assistente para Instalação.

# MSI Installer UI - Exit Dialog
msi-exit-title = Concluído o Assistente para Instalação do { $app_title }
msi-exit-description = Clique no botão Concluir para sair do Assistente para Instalação.
msi-exit-launch-checkbox = Iniciar o { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Bem-vindo ao Assistente para Instalação do { $app_title }
msi-prepare-description = Aguarde enquanto o Assistente para Instalação se prepara para orientar você durante a instalação.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Contrato de Licença de Usuário Final
msi-license-description = Leia atenciosamente o contrato de licença a seguir.
msi-license-checkbox = &Aceito os termos do Contrato de Licença

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Escopo de Instalação
msi-scope-description = Selecione o escopo de instalação.
msi-scope-per-user = Instalar ape&nas para você
msi-scope-per-machine = Instalar para &todos os usuários
msi-scope-per-user-description = Instala apenas para o usuário atual
msi-scope-no-per-user-description = Requer privilégios de administrador
msi-scope-per-machine-description = Requer privilégios de administrador

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Pronto para instalar o { $app_title }
msi-ready-install-text = Clique em Instalar para iniciar a instalação. Clique em Voltar para rever ou alterar as configurações de instalação.
msi-ready-change-title = Pronto para alterar o { $app_title }
msi-ready-change-text = Clique em Alterar para iniciar a alteração da instalação. Clique em Voltar para rever ou alterar as configurações de instalação.
msi-ready-repair-title = Pronto para reparar o { $app_title }
msi-ready-repair-text = Clique em Reparar para iniciar o reparo. Clique em Voltar para rever ou alterar as configurações de instalação.
msi-ready-remove-title = Pronto para remover o { $app_title }
msi-ready-remove-text = Clique em Remover para remover o { $app_title } do seu computador. Clique em Voltar para rever ou alterar as configurações de instalação.
msi-ready-update-title = Pronto para atualizar o { $app_title }
msi-ready-update-text = Clique em Atualizar para iniciar a atualização. Clique em Voltar para rever ou alterar as configurações de instalação.
msi-ready-btn-install = &Instalar
msi-ready-btn-change = &Alterar
msi-ready-btn-repair = Re&parar
msi-ready-btn-remove = &Remover
msi-ready-btn-update = At&ualizar

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Instalando o { $app_title }
msi-progress-installing-text = Aguarde enquanto o Assistente para Instalação instala o { $app_title }.
msi-progress-changing-title = Alterando o { $app_title }
msi-progress-changing-text = Aguarde enquanto o Assistente para Instalação altera o { $app_title }.
msi-progress-repairing-title = Reparando o { $app_title }
msi-progress-repairing-text = Aguarde enquanto o Assistente para Instalação repara o { $app_title }.
msi-progress-removing-title = Removendo o { $app_title }
msi-progress-removing-text = Aguarde enquanto o Assistente para Instalação remove o { $app_title }.
msi-progress-updating-title = Atualizando o { $app_title }
msi-progress-updating-text = Aguarde enquanto o Assistente para Instalação atualiza o { $app_title }.
msi-progress-status = Status:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Bem-vindo ao Assistente para Instalação do { $app_title }
msi-maint-welcome-description = O Assistente para Instalação permitirá que você repare ou remova o { $app_title }. Clique em Avançar para continuar ou em Cancelar para sair do Assistente para Instalação.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Alterar, reparar ou remover a instalação
msi-maint-type-description = Selecione a operação que você deseja executar.
msi-maint-change-button = &Alterar...
msi-maint-change-tooltip = Alterar...
msi-maint-change-text = Permite aos usuários alterar quais recursos do programa estão instalados e alterar recursos individuais.
msi-maint-change-disabled = Alterar está desativado no momento.
msi-maint-repair-button = Re&parar
msi-maint-repair-tooltip = Reparar
msi-maint-repair-text = Repara erros da instalação mais recente, corrigindo arquivos, atalhos e entradas do Registro ausentes ou corrompidos.
msi-maint-repair-disabled = Reparar está desativado no momento.
msi-maint-remove-button = &Remover
msi-maint-remove-tooltip = Remover
msi-maint-remove-text = Remove o { $app_title } do seu computador.
msi-maint-remove-disabled = Remover está desativado no momento.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Tem certeza de que deseja cancelar a instalação do { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = Alterar pasta de destino atual
msi-browse-description = Procurar a pasta de destino.
msi-browse-combo-label = &Examinar:
msi-browse-path-label = &Nome da pasta:
msi-browse-up-tooltip = Um nível acima
msi-browse-new-folder-tooltip = Criar uma nova pasta

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = O diretório de destino especificado é inválido ou está em um tipo de unidade sem suporte.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Requisitos de Espaço em Disco
msi-disk-cost-description = Espaço em disco necessário para a instalação dos recursos selecionados.
msi-disk-cost-text = Os volumes realçados não possuem espaço em disco suficiente disponível para os recursos selecionados atualmente. Você pode remover arquivos dos volumes realçados, escolher instalar menos recursos nas unidades locais, ou selecionar unidades de destino diferentes.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informações do Instalador do { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = O Assistente para Instalação do { $app_title } foi encerrado prematuramente
msi-fatal-description1 = A instalação do { $app_title } foi interrompida. O sistema não foi modificado. Para instalar este programa mais tarde, execute a instalação novamente.
msi-fatal-description2 = Clique no botão Concluir para sair do Assistente para Instalação.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = O Assistente para Instalação do { $app_title } foi interrompido
msi-user-exit-description1 = A instalação do { $app_title } foi interrompida. O sistema não foi modificado. Para instalar este programa mais tarde, execute a instalação novamente.
msi-user-exit-description2 = Clique no botão Concluir para sair do Assistente para Instalação.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Arquivos em Uso
msi-files-in-use-description = Alguns arquivos que precisam ser atualizados estão em uso.
msi-files-in-use-text = Os aplicativos a seguir estão usando arquivos que precisam ser atualizados por esta instalação. Feche os aplicativos e clique em Tentar Novamente para continuar a instalação ou em Cancelar para encerrá-la.
msi-files-in-use-exit = Sai&r

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Arquivos em Uso
msi-rm-files-in-use-description = Alguns arquivos que precisam ser atualizados estão em uso.
msi-rm-files-in-use-text = Os aplicativos a seguir estão usando arquivos que precisam ser atualizados por esta instalação. Você pode permitir que o Assistente para Instalação feche automaticamente e tente reiniciar esses aplicativos ou pode fechá-los manualmente e clicar em OK para continuar a instalação.
msi-rm-files-in-use-use-rm = &Fechar automaticamente os aplicativos e tentar reiniciá-los após a conclusão da instalação.
msi-rm-files-in-use-dont-use-rm = &Não fechar os aplicativos. (Será necessário reinicializar.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Continuando o Assistente para Instalação do { $app_title }
msi-resume-description = O Assistente para Instalação concluirá a instalação do { $app_title } no seu computador. Clique em Instalar para continuar ou em Cancelar para sair do Assistente para Instalação.
msi-resume-btn-install = &Instalar

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Atalho na Área de Trabalho para o { $app_title }
msi-start-menu-shortcut-description = Atalho no Menu Iniciar para o { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Informações importantes
msi-readme-description = Leia as informações a seguir antes de continuar.
