# Shared titles
title-update = Mise à jour de { $app_title }
title-setup = Installation de { $app_title }
title-uninstall = Désinstallation de { $app_title }
error-title = Erreur { $program_name }

# Shared buttons
btn-cancel = Annuler
btn-install-update = Installer la mise à jour
btn-install = Installer
btn-update = Mettre à jour
btn-downgrade = Rétrograder
btn-repair = Réparer
btn-open-log = Ouvrir le journal
btn-open-install-dir = Ouvrir le dossier d'installation
btn-ok = OK
# Elevation (dialogs_common.rs)
elevate-header = Autorisation administrateur requise
elevate-body = { $app_title } a besoin d'une autorisation administrateur pour installer la version { $app_version }. Autoriser cette mise à jour à continuer ?

# Restart required (prerequisite.rs)
restart-header = Redémarrage requis
restart-body = Votre ordinateur doit redémarrer avant que l'installation puisse continuer. Veuillez redémarrer votre ordinateur et exécuter l'installation à nouveau.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Composants supplémentaires requis
missing-deps-body = { $app_title } nécessite l'installation préalable des éléments suivants : { $deps }. Souhaitez-vous les télécharger et les installer maintenant ?

# Uninstall with errors (uninstall)
uninstall-errors-header = Désinstallation terminée avec des problèmes
uninstall-errors-body = { $app_title } a été désinstallé, mais certains fichiers ou dossiers n'ont pas pu être supprimés. Vous pouvez les supprimer manuellement, ou réinstaller l'application et essayer de la désinstaller à nouveau.
uninstall-errors-log = Les détails ont été enregistrés dans : { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } est déjà installé
overwrite-repair-body = Cette application est déjà installée sur votre ordinateur. Si elle ne fonctionne pas correctement, vous pouvez essayer de la réparer en la réinstallant.
overwrite-older-installed = { $app_title } est déjà installé
overwrite-update-body = La version { $old_version } est actuellement installée. Souhaitez-vous effectuer une mise à jour vers la version { $app_version } ?
overwrite-newer-installed = Une version plus récente de { $app_title } est déjà installée
overwrite-downgrade-body = La version { $old_version } est actuellement installée et est plus récente que cet installateur. La rétrogradation n'est pas recommandée et peut causer des problèmes. Continuer quand même ?
overwrite-footer = Installé dans : { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Désinstallation terminée
uninstall-body = L'application a été supprimée avec succès de votre ordinateur.

# Install hook failed (install.rs)
install-hook-header = Installation partiellement réussie
install-hook-body = L'installation est terminée, mais certaines étapes ont peut-être échoué. Si l'application ne fonctionne pas correctement, vous pouvez essayer de la réinstaller ou de contacter l'auteur de l'application.

# Splash fallback (splash.rs)
splash-header = Installation de { $app_title }
splash-body = Configuration de { $app_title } { $app_version }, veuillez patienter...

# Dependency download (prerequisite.rs)
deps-download-header = Téléchargement du composant requis
deps-download-body = Téléchargement de { $dep_name }, veuillez patienter...

# Apply progress (apply_*_impl.rs)
apply-header = Installation de la mise à jour
apply-body = Mise à jour vers la version { $app_version }, veuillez patienter...

# Start error (start_windows_impl.rs)
start-corrupt-header = Installation endommagée
start-corrupt-body = Cette application ne peut pas démarrer car certains de ses fichiers sont manquants ou endommagés. Veuillez réinstaller l'application pour résoudre ce problème.

# Generic error
error-header = Une erreur s'est produite

# Setup error (wix msi)
setup-error-header = L'installation n'a pas pu continuer
setup-disk-space-insufficient = { $app_title } nécessite au moins { $required_space } d'espace disque pour être installé. Il n'y a que { $available_space } de disponible.
setup-windows-version-unsupported = Cet installateur nécessite Windows 7 SP1 ou ultérieur et ne peut pas s'exécuter.
setup-embedded-zip-missing = Impossible de trouver le fichier zip intégré. Veuillez contacter l'auteur de l'application.
setup-os-version-required = Cette application nécessite Windows { $os_version } ou ultérieur.
setup-cpu-arch-unsupported = Cette application ({ $machine_arch }) ne prend pas en charge l'architecture de votre processeur.
setup-stop-app-failed = Impossible d'arrêter l'application ({ $error }), veuillez fermer l'application et réessayer d'exécuter l'installateur.
setup-remove-dir-failed = Impossible de supprimer le répertoire existant de l'application, veuillez fermer l'application et réessayer d'exécuter l'installateur. Si le problème persiste, essayez d'abord de désinstaller via Programmes et fonctionnalités, ou de redémarrer votre ordinateur.
setup-update-exe-missing = Il manque un fichier binaire essentiel (Update.exe) à cet installateur. Veuillez contacter l'auteur de l'application.
setup-main-exe-missing = L'exécutable principal est introuvable dans le paquet. Veuillez contacter l'auteur de l'application.

# MSI Installer UI - Common
msi-dlg-title = Installation de { $app_title }
msi-btn-back = &Précédent
msi-btn-next = &Suivant
msi-btn-cancel = Annuler
msi-btn-finish = &Terminer
msi-btn-ok = OK
msi-btn-yes = &Oui
msi-btn-no = &Non
msi-btn-retry = &Réessayer
msi-btn-ignore = &Ignorer

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Bienvenue dans l'Assistant Installation de { $app_title }
msi-welcome-description = L'Assistant Installation va installer { $app_title } sur l'ordinateur. Cliquez sur Suivant pour continuer, ou sur Annuler pour quitter l'Assistant Installation.
msi-welcome-update-description = L'Assistant Installation va mettre à jour { $app_title } sur l'ordinateur. Cliquez sur Suivant pour continuer, ou sur Annuler pour quitter l'Assistant Installation.

# MSI Installer UI - Exit Dialog
msi-exit-title = Assistant Installation de { $app_title } terminé
msi-exit-description = Cliquez sur le bouton Terminer pour quitter l'Assistant Installation.
msi-exit-launch-checkbox = Lancer { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Bienvenue dans l'Assistant Installation de { $app_title }
msi-prepare-description = Veuillez patienter pendant que l'Assistant Installation se prépare pour vous guider dans l'installation.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Contrat de Licence Utilisateur Final
msi-license-description = Lisez attentivement le contrat de licence suivant.
msi-license-checkbox = J'&accepte les termes du contrat de licence

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Étendue d'installation
msi-scope-description = Sélectionnez l'étendue d'installation.
msi-scope-per-user = Installer &uniquement pour vous
msi-scope-per-machine = Installer pour &tous les utilisateurs
msi-scope-per-user-description = Installe uniquement pour l'utilisateur actuel
msi-scope-no-per-user-description = Nécessite des privilèges administrateur
msi-scope-per-machine-description = Nécessite des privilèges administrateur

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Prêt à installer { $app_title }
msi-ready-install-text = Cliquez sur Installer pour commencer l'installation. Cliquez sur Précédent pour vérifier ou modifier vos paramètres d'installation.
msi-ready-change-title = Prêt à modifier { $app_title }
msi-ready-change-text = Cliquez sur Modifier pour lancer la modification de l'installation. Cliquez sur Précédent pour vérifier ou modifier vos paramètres d'installation.
msi-ready-repair-title = Prêt à réparer { $app_title }
msi-ready-repair-text = Cliquez sur Réparer pour lancer la réparation. Cliquez sur Précédent pour vérifier ou modifier vos paramètres d'installation.
msi-ready-remove-title = Prêt à supprimer { $app_title }
msi-ready-remove-text = Cliquez sur Supprimer pour supprimer { $app_title } de votre ordinateur. Cliquez sur Précédent pour vérifier ou modifier vos paramètres d'installation.
msi-ready-update-title = Prêt à mettre à jour { $app_title }
msi-ready-update-text = Cliquez sur Mettre à jour pour lancer la mise à jour. Cliquez sur Précédent pour vérifier ou modifier vos paramètres d'installation.
msi-ready-btn-install = &Installer
msi-ready-btn-change = &Modifier
msi-ready-btn-repair = Ré&parer
msi-ready-btn-remove = &Supprimer
msi-ready-btn-update = Mettre à jo&ur

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Installation de { $app_title }
msi-progress-installing-text = Veuillez patienter pendant que l'Assistant Installation installe { $app_title }.
msi-progress-changing-title = Modification de { $app_title }
msi-progress-changing-text = Veuillez patienter pendant que l'Assistant Installation modifie { $app_title }.
msi-progress-repairing-title = Réparation de { $app_title }
msi-progress-repairing-text = Veuillez patienter pendant que l'Assistant Installation répare { $app_title }.
msi-progress-removing-title = Suppression de { $app_title }
msi-progress-removing-text = Veuillez patienter pendant que l'Assistant Installation supprime { $app_title }.
msi-progress-updating-title = Mise à jour de { $app_title }
msi-progress-updating-text = Veuillez patienter pendant que l'Assistant Installation met à jour { $app_title }.
msi-progress-status = Statut :

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Bienvenue dans l'Assistant Installation de { $app_title }
msi-maint-welcome-description = L'Assistant Installation vous permettra de réparer ou de supprimer { $app_title }. Cliquez sur Suivant pour continuer, ou sur Annuler pour quitter l'Assistant Installation.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Modifier, réparer ou supprimer l'installation
msi-maint-type-description = Sélectionnez l'opération à exécuter.
msi-maint-change-button = &Modifier...
msi-maint-change-tooltip = Modifier...
msi-maint-change-text = Permet aux utilisateurs de modifier les composants de programme installés et de modifier des composants individuels.
msi-maint-change-disabled = Modifier est actuellement désactivé.
msi-maint-repair-button = Ré&parer
msi-maint-repair-tooltip = Réparer
msi-maint-repair-text = Corrige les erreurs de l'installation la plus récente en réparant les fichiers, raccourcis et entrées de Registre manquants ou endommagés.
msi-maint-repair-disabled = Réparer est actuellement désactivé.
msi-maint-remove-button = &Supprimer
msi-maint-remove-tooltip = Supprimer
msi-maint-remove-text = Supprime { $app_title } de votre ordinateur.
msi-maint-remove-disabled = Supprimer est actuellement désactivé.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Êtes-vous sûr de vouloir annuler l'installation de { $app_title } ?

# MSI Installer UI - Browse Dialog
msi-browse-title = Modifier le dossier de destination actuel
msi-browse-description = Sélectionnez le dossier de destination.
msi-browse-combo-label = &Regarder dans :
msi-browse-path-label = &Nom du dossier :
msi-browse-up-tooltip = Remonter d'un niveau
msi-browse-new-folder-tooltip = Créer un dossier

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Le répertoire de destination spécifié est invalide ou se trouve sur un type de lecteur non pris en charge.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Espace disque nécessaire
msi-disk-cost-description = Espace disque nécessaire pour l'installation des composants sélectionnés.
msi-disk-cost-text = Les volumes mis en surbrillance ne disposent pas de suffisamment d'espace disque pour les composants actuellement sélectionnés. Vous pouvez supprimer certains fichiers des volumes mis en surbrillance, installer moins de composants sur le(s) lecteur(s) local(aux), ou sélectionner d'autres lecteurs de destination.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Informations de l'installateur { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = L'Assistant Installation de { $app_title } a pris fin prématurément
msi-fatal-description1 = L'installation de { $app_title } a été interrompue. Votre système n'a pas été modifié. Pour installer ce programme ultérieurement, veuillez exécuter à nouveau l'installation.
msi-fatal-description2 = Cliquez sur le bouton Terminer pour quitter l'Assistant Installation.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = L'Assistant Installation de { $app_title } a été interrompu
msi-user-exit-description1 = L'installation de { $app_title } a été interrompue. Votre système n'a pas été modifié. Pour installer ce programme ultérieurement, veuillez exécuter à nouveau l'installation.
msi-user-exit-description2 = Cliquez sur le bouton Terminer pour quitter l'Assistant Installation.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Fichiers en cours d'utilisation
msi-files-in-use-description = Certains fichiers qui doivent être mis à jour sont en cours d'utilisation.
msi-files-in-use-text = Les applications suivantes utilisent des fichiers qui doivent être mis à jour par ce programme d'installation. Fermez ces applications et cliquez sur Réessayer pour continuer l'installation, ou cliquez sur Annuler pour la quitter.
msi-files-in-use-exit = &Quitter

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Fichiers en cours d'utilisation
msi-rm-files-in-use-description = Certains fichiers qui doivent être mis à jour sont en cours d'utilisation.
msi-rm-files-in-use-text = Les applications suivantes utilisent des fichiers qui doivent être mis à jour par ce programme d'installation. Vous pouvez laisser l'Assistant Installation fermer automatiquement ces applications et tenter de les redémarrer, ou vous pouvez les fermer manuellement et cliquer sur OK pour continuer l'installation.
msi-rm-files-in-use-use-rm = &Fermer automatiquement les applications et tenter de les redémarrer une fois l'installation terminée.
msi-rm-files-in-use-dont-use-rm = &Ne pas fermer les applications. (Un redémarrage sera nécessaire.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Reprise de l'Assistant Installation de { $app_title }
msi-resume-description = L'Assistant Installation va terminer l'installation de { $app_title } sur votre ordinateur. Cliquez sur Installer pour continuer, ou sur Annuler pour quitter l'Assistant Installation.
msi-resume-btn-install = &Installer

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Raccourci Bureau pour { $app_title }
msi-start-menu-shortcut-description = Raccourci Menu Démarrer pour { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = Informations importantes
msi-readme-description = Veuillez lire les informations suivantes avant de continuer.
