# Shared titles
title-update = Ενημέρωση του { $app_title }
title-setup = Εγκατάσταση του { $app_title }
title-uninstall = Κατάργηση εγκατάστασης του { $app_title }
error-title = Σφάλμα του { $program_name }

# Shared buttons
btn-cancel = Άκυρο
btn-install-update = Εγκατάσταση ενημέρωσης
btn-install = Εγκατάσταση
btn-update = Ενημέρωση
btn-downgrade = Υποβάθμιση
btn-repair = Επιδιόρθωση
btn-open-log = Άνοιγμα αρχείου καταγραφής
btn-open-install-dir = Άνοιγμα φακέλου εγκατάστασης
btn-ok = ΟΚ
# Elevation (dialogs_common.rs)
elevate-header = Απαιτείται δικαίωμα διαχειριστή
elevate-body = Το { $app_title } χρειάζεται δικαίωμα διαχειριστή για την εγκατάσταση της έκδοσης { $app_version }. Να επιτραπεί η συνέχιση αυτής της ενημέρωσης;

# Restart required (prerequisite.rs)
restart-header = Απαιτείται επανεκκίνηση
restart-body = Ο υπολογιστής σας πρέπει να επανεκκινηθεί προτού συνεχίσει η εγκατάσταση. Επανεκκινήστε τον υπολογιστή σας και εκτελέστε ξανά το πρόγραμμα εγκατάστασης.

# Missing dependencies (prerequisite.rs)
missing-deps-header = Απαιτούνται πρόσθετα στοιχεία
missing-deps-body = Το { $app_title } χρειάζεται να εγκατασταθούν πρώτα τα ακόλουθα: { $deps }. Θέλετε να γίνει λήψη και εγκατάστασή τους τώρα;

# Uninstall with errors (uninstall)
uninstall-errors-header = Η κατάργηση ολοκληρώθηκε με προβλήματα
uninstall-errors-body = Το { $app_title } καταργήθηκε, αλλά ορισμένα αρχεία ή φάκελοι δεν ήταν δυνατό να καταργηθούν. Μπορείτε να τα διαγράψετε με μη αυτόματο τρόπο ή να εγκαταστήσετε ξανά την εφαρμογή και να επιχειρήσετε να την καταργήσετε ξανά.
uninstall-errors-log = Οι λεπτομέρειες αποθηκεύτηκαν στη θέση: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = Το { $app_title } είναι ήδη εγκατεστημένο
overwrite-repair-body = Αυτή η εφαρμογή είναι ήδη εγκατεστημένη στον υπολογιστή σας. Εάν δεν λειτουργεί σωστά, μπορείτε να δοκιμάσετε να την επιδιορθώσετε εγκαθιστώντας την ξανά.
overwrite-older-installed = Το { $app_title } είναι ήδη εγκατεστημένο
overwrite-update-body = Είναι εγκατεστημένη η έκδοση { $old_version }. Θέλετε να γίνει ενημέρωση στην έκδοση { $app_version };
overwrite-newer-installed = Μια νεότερη έκδοση του { $app_title } είναι ήδη εγκατεστημένη
overwrite-downgrade-body = Είναι εγκατεστημένη η έκδοση { $old_version }, που είναι νεότερη από αυτό το πρόγραμμα εγκατάστασης. Η υποβάθμιση δεν συνιστάται και μπορεί να προκαλέσει προβλήματα. Να συνεχιστεί παρ’ όλα αυτά;
overwrite-footer = Εγκατεστημένο στη θέση: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = Η κατάργηση ολοκληρώθηκε
uninstall-body = Η εφαρμογή καταργήθηκε με επιτυχία από τον υπολογιστή σας.

# Install hook failed (install.rs)
install-hook-header = Η εγκατάσταση ολοκληρώθηκε εν μέρει
install-hook-body = Η εγκατάσταση ολοκληρώθηκε, αλλά ορισμένα βήματα ενδέχεται να απέτυχαν. Εάν η εφαρμογή δεν λειτουργεί σωστά, μπορείτε να δοκιμάσετε να την εγκαταστήσετε ξανά ή να επικοινωνήσετε με τον δημιουργό της εφαρμογής.

# Splash fallback (splash.rs)
splash-header = Εγκατάσταση του { $app_title }
splash-body = Ρύθμιση του { $app_title } { $app_version }, περιμένετε…

# Dependency download (prerequisite.rs)
deps-download-header = Λήψη απαιτούμενου στοιχείου
deps-download-body = Λήψη του { $dep_name }, περιμένετε…

# Apply progress (apply_*_impl.rs)
apply-header = Εγκατάσταση της ενημέρωσης
apply-body = Ενημέρωση στην έκδοση { $app_version }, περιμένετε…

# Start error (start_windows_impl.rs)
start-corrupt-header = Η εγκατάσταση είναι κατεστραμμένη
start-corrupt-body = Αυτή η εφαρμογή δεν μπορεί να εκκινηθεί επειδή ορισμένα από τα αρχεία της λείπουν ή είναι κατεστραμμένα. Εγκαταστήστε ξανά την εφαρμογή για να διορθωθεί το πρόβλημα.

# Generic error
error-header = Κάτι πήγε στραβά

# Setup error (wix msi)
setup-error-header = Η εγκατάσταση δεν ήταν δυνατό να συνεχιστεί
setup-disk-space-insufficient = Το { $app_title } απαιτεί τουλάχιστον { $required_space } χώρο στον δίσκο για να εγκατασταθεί. Υπάρχουν μόνο { $available_space } διαθέσιμα.
setup-windows-version-unsupported = Αυτό το πρόγραμμα εγκατάστασης απαιτεί Windows 7 SP1 ή νεότερη έκδοση και δεν μπορεί να εκτελεστεί.
setup-embedded-zip-missing = Δεν ήταν δυνατή η εύρεση του ενσωματωμένου αρχείου zip. Επικοινωνήστε με τον δημιουργό της εφαρμογής.
setup-os-version-required = Αυτή η εφαρμογή απαιτεί Windows { $os_version } ή νεότερη έκδοση.
setup-cpu-arch-unsupported = Αυτή η εφαρμογή ({ $machine_arch }) δεν υποστηρίζει την αρχιτεκτονική του επεξεργαστή σας.
setup-stop-app-failed = Δεν ήταν δυνατή η διακοπή της εφαρμογής ({ $error }). Κλείστε την εφαρμογή και δοκιμάστε να εκτελέσετε ξανά το πρόγραμμα εγκατάστασης.
setup-remove-dir-failed = Δεν ήταν δυνατή η κατάργηση του υπάρχοντος καταλόγου της εφαρμογής. Κλείστε την εφαρμογή και δοκιμάστε να εκτελέσετε ξανά το πρόγραμμα εγκατάστασης. Εάν το πρόβλημα επιμένει, δοκιμάστε πρώτα να καταργήσετε την εγκατάσταση από τα Προγράμματα και δυνατότητες ή επανεκκινήστε τον υπολογιστή σας.
setup-update-exe-missing = Από αυτό το πρόγραμμα εγκατάστασης λείπει ένα κρίσιμο δυαδικό αρχείο (Update.exe). Επικοινωνήστε με τον δημιουργό της εφαρμογής.
setup-main-exe-missing = Το κύριο εκτελέσιμο αρχείο δεν βρέθηκε στο πακέτο. Επικοινωνήστε με τον δημιουργό της εφαρμογής.

# MSI Installer UI - Common
msi-dlg-title = Πρόγραμμα εγκατάστασης του { $app_title }
msi-btn-back = &Πίσω
msi-btn-next = Ε&πόμενο
msi-btn-cancel = Άκυρο
msi-btn-finish = &Τέλος
msi-btn-ok = ΟΚ
msi-btn-yes = &Ναι
msi-btn-no = Όχ&ι
msi-btn-retry = &Επανάληψη
msi-btn-ignore = &Παράβλεψη

# MSI Installer UI - Welcome Dialog
msi-welcome-title = Καλώς ορίσατε στον Οδηγό εγκατάστασης του { $app_title }
msi-welcome-description = Ο Οδηγός εγκατάστασης θα εγκαταστήσει το { $app_title } στον υπολογιστή σας. Κάντε κλικ στο κουμπί «Επόμενο» για να συνεχίσετε ή στο κουμπί «Άκυρο» για να εξέλθετε από τον Οδηγό εγκατάστασης.
msi-welcome-update-description = Ο Οδηγός εγκατάστασης θα ενημερώσει το { $app_title } στον υπολογιστή σας. Κάντε κλικ στο κουμπί «Επόμενο» για να συνεχίσετε ή στο κουμπί «Άκυρο» για να εξέλθετε από τον Οδηγό εγκατάστασης.

# MSI Installer UI - Exit Dialog
msi-exit-title = Ο Οδηγός εγκατάστασης του { $app_title } ολοκληρώθηκε
msi-exit-description = Κάντε κλικ στο κουμπί «Τέλος» για να εξέλθετε από τον Οδηγό εγκατάστασης.
msi-exit-launch-checkbox = Εκκίνηση του { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = Καλώς ορίσατε στον Οδηγό εγκατάστασης του { $app_title }
msi-prepare-description = Περιμένετε όσο ο Οδηγός εγκατάστασης προετοιμάζεται για να σας καθοδηγήσει στην εγκατάσταση.

# MSI Installer UI - License Agreement Dialog
msi-license-title = Άδεια χρήσης τελικού χρήστη
msi-license-description = Διαβάστε προσεκτικά την παρακάτω άδεια χρήσης.
msi-license-checkbox = &Αποδέχομαι τους όρους της άδειας χρήσης

# MSI Installer UI - Install Scope Dialog
msi-scope-title = Εμβέλεια εγκατάστασης
msi-scope-description = Επιλέξτε την εμβέλεια εγκατάστασης.
msi-scope-per-user = Εγκατάσταση &μόνο για εσάς
msi-scope-per-machine = Εγκατάσταση για &όλους τους χρήστες
msi-scope-per-user-description = Εγκατάσταση μόνο για τον τρέχοντα χρήστη
msi-scope-no-per-user-description = Απαιτούνται δικαιώματα διαχειριστή
msi-scope-per-machine-description = Απαιτούνται δικαιώματα διαχειριστή

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = Έτοιμο για εγκατάσταση του { $app_title }
msi-ready-install-text = Κάντε κλικ στο κουμπί «Εγκατάσταση» για να ξεκινήσει η εγκατάσταση. Κάντε κλικ στο κουμπί «Πίσω» για να ελέγξετε ή να αλλάξετε τις ρυθμίσεις της εγκατάστασης.
msi-ready-change-title = Έτοιμο για αλλαγή του { $app_title }
msi-ready-change-text = Κάντε κλικ στο κουμπί «Αλλαγή» για να ξεκινήσει η αλλαγή της εγκατάστασης. Κάντε κλικ στο κουμπί «Πίσω» για να ελέγξετε ή να αλλάξετε τις ρυθμίσεις της εγκατάστασης.
msi-ready-repair-title = Έτοιμο για επιδιόρθωση του { $app_title }
msi-ready-repair-text = Κάντε κλικ στο κουμπί «Επιδιόρθωση» για να ξεκινήσει η επιδιόρθωση. Κάντε κλικ στο κουμπί «Πίσω» για να ελέγξετε ή να αλλάξετε τις ρυθμίσεις της εγκατάστασης.
msi-ready-remove-title = Έτοιμο για κατάργηση του { $app_title }
msi-ready-remove-text = Κάντε κλικ στο κουμπί «Κατάργηση» για να καταργήσετε το { $app_title } από τον υπολογιστή σας. Κάντε κλικ στο κουμπί «Πίσω» για να ελέγξετε ή να αλλάξετε τις ρυθμίσεις της εγκατάστασης.
msi-ready-update-title = Έτοιμο για ενημέρωση του { $app_title }
msi-ready-update-text = Κάντε κλικ στο κουμπί «Ενημέρωση» για να ξεκινήσει η ενημέρωση. Κάντε κλικ στο κουμπί «Πίσω» για να ελέγξετε ή να αλλάξετε τις ρυθμίσεις της εγκατάστασης.
msi-ready-btn-install = &Εγκατάσταση
msi-ready-btn-change = &Αλλαγή
msi-ready-btn-repair = Επι&διόρθωση
msi-ready-btn-remove = &Κατάργηση
msi-ready-btn-update = &Ενημέρωση

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = Εγκατάσταση του { $app_title }
msi-progress-installing-text = Περιμένετε όσο ο Οδηγός εγκατάστασης εγκαθιστά το { $app_title }.
msi-progress-changing-title = Αλλαγή του { $app_title }
msi-progress-changing-text = Περιμένετε όσο ο Οδηγός εγκατάστασης αλλάζει το { $app_title }.
msi-progress-repairing-title = Επιδιόρθωση του { $app_title }
msi-progress-repairing-text = Περιμένετε όσο ο Οδηγός εγκατάστασης επιδιορθώνει το { $app_title }.
msi-progress-removing-title = Κατάργηση του { $app_title }
msi-progress-removing-text = Περιμένετε όσο ο Οδηγός εγκατάστασης καταργεί το { $app_title }.
msi-progress-updating-title = Ενημέρωση του { $app_title }
msi-progress-updating-text = Περιμένετε όσο ο Οδηγός εγκατάστασης ενημερώνει το { $app_title }.
msi-progress-status = Κατάσταση:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = Καλώς ορίσατε στον Οδηγό εγκατάστασης του { $app_title }
msi-maint-welcome-description = Ο Οδηγός εγκατάστασης σάς επιτρέπει να επιδιορθώσετε ή να καταργήσετε το { $app_title }. Κάντε κλικ στο κουμπί «Επόμενο» για να συνεχίσετε ή στο κουμπί «Άκυρο» για να εξέλθετε από τον Οδηγό εγκατάστασης.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = Αλλαγή, επιδιόρθωση ή κατάργηση εγκατάστασης
msi-maint-type-description = Επιλέξτε τη λειτουργία που θέλετε να εκτελέσετε.
msi-maint-change-button = &Αλλαγή...
msi-maint-change-tooltip = Αλλαγή...
msi-maint-change-text = Επιτρέπει στους χρήστες να αλλάξουν τις εγκατεστημένες δυνατότητες του προγράμματος και να αλλάξουν μεμονωμένες δυνατότητες.
msi-maint-change-disabled = Η αλλαγή είναι απενεργοποιημένη αυτήν τη στιγμή.
msi-maint-repair-button = Επι&διόρθωση
msi-maint-repair-tooltip = Επιδιόρθωση
msi-maint-repair-text = Επιδιορθώνει σφάλματα στην πιο πρόσφατη εγκατάσταση - διορθώνει αρχεία, συντομεύσεις και καταχωρήσεις μητρώου που λείπουν ή που έχουν καταστραφεί.
msi-maint-repair-disabled = Η επιδιόρθωση είναι απενεργοποιημένη αυτήν τη στιγμή.
msi-maint-remove-button = &Κατάργηση
msi-maint-remove-tooltip = Κατάργηση
msi-maint-remove-text = Καταργεί το { $app_title } από τον υπολογιστή σας.
msi-maint-remove-disabled = Η κατάργηση είναι απενεργοποιημένη αυτήν τη στιγμή.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = Είστε βέβαιοι ότι θέλετε να ακυρώσετε την εγκατάσταση του { $app_title };

# MSI Installer UI - Browse Dialog
msi-browse-title = Αλλαγή του τρέχοντος φακέλου προορισμού
msi-browse-description = Αναζήτηση του φακέλου προορισμού.
msi-browse-combo-label = &Αναζήτηση σε:
msi-browse-path-label = Ό&νομα φακέλου:
msi-browse-up-tooltip = Ένα επίπεδο επάνω
msi-browse-new-folder-tooltip = Δημιουργία νέου φακέλου

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = Ο καθορισμένος κατάλογος προορισμού δεν είναι έγκυρος ή βρίσκεται σε τύπο μονάδας δίσκου που δεν υποστηρίζεται.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = Απαιτήσεις χώρου στο δίσκο
msi-disk-cost-description = Ο χώρος στο δίσκο που απαιτείται για την εγκατάσταση των επιλεγμένων δυνατοτήτων.
msi-disk-cost-text = Οι τόμοι που επισημαίνονται δεν έχουν αρκετό διαθέσιμο χώρο στο δίσκο για τις τρέχουσες επιλεγμένες δυνατότητες. Μπορείτε να καταργήσετε μερικά αρχεία από τους τόμους που επισημαίνονται, να επιλέξετε εγκατάσταση λιγότερων δυνατοτήτων σε τοπικές μονάδες δίσκου ή να επιλέξετε διαφορετικές μονάδες δίσκου προορισμού.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = Πληροφορίες προγράμματος εγκατάστασης του { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = Ο Οδηγός εγκατάστασης του { $app_title } τερματίστηκε πρόωρα
msi-fatal-description1 = Η εγκατάσταση του { $app_title } διακόπηκε. Το σύστημά σας δεν τροποποιήθηκε. Για να εγκαταστήσετε αυτό το πρόγραμμα αργότερα, εκτελέστε ξανά το πρόγραμμα εγκατάστασης.
msi-fatal-description2 = Κάντε κλικ στο κουμπί «Τέλος» για να εξέλθετε από τον Οδηγό εγκατάστασης.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = Ο Οδηγός εγκατάστασης του { $app_title } διακόπηκε
msi-user-exit-description1 = Η εγκατάσταση του { $app_title } διακόπηκε. Το σύστημά σας δεν τροποποιήθηκε. Για να εγκαταστήσετε αυτό το πρόγραμμα αργότερα, εκτελέστε ξανά το πρόγραμμα εγκατάστασης.
msi-user-exit-description2 = Κάντε κλικ στο κουμπί «Τέλος» για να εξέλθετε από τον Οδηγό εγκατάστασης.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = Αρχεία σε χρήση
msi-files-in-use-description = Κάποια αρχεία που πρέπει να ενημερωθούν βρίσκονται σε χρήση αυτήν τη στιγμή.
msi-files-in-use-text = Οι ακόλουθες εφαρμογές χρησιμοποιούν αρχεία που πρέπει να ενημερωθούν από αυτό το πρόγραμμα εγκατάστασης. Κλείστε αυτές τις εφαρμογές και μετά κάντε κλικ στο κουμπί «Επανάληψη» για να συνεχίσετε την εγκατάσταση ή στο κουμπί «Άκυρο» για να βγείτε από αυτήν.
msi-files-in-use-exit = Έ&ξοδος

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = Αρχεία σε χρήση
msi-rm-files-in-use-description = Κάποια αρχεία που πρέπει να ενημερωθούν βρίσκονται σε χρήση αυτήν τη στιγμή.
msi-rm-files-in-use-text = Οι ακόλουθες εφαρμογές χρησιμοποιούν αρχεία που πρέπει να ενημερωθούν από αυτό το πρόγραμμα εγκατάστασης. Μπορείτε να αφήσετε τον Οδηγό εγκατάστασης να τις κλείσει αυτόματα και να επιχειρήσει να τις επανεκκινήσει ή να τις κλείσετε εσείς και να κάνετε κλικ στο κουμπί «ΟΚ» για να συνεχιστεί η εγκατάσταση.
msi-rm-files-in-use-use-rm = Αυτόματο &κλείσιμο των εφαρμογών και προσπάθεια επανεκκίνησής τους μετά την ολοκλήρωση της εγκατάστασης.
msi-rm-files-in-use-dont-use-rm = &Μην κλείνετε τις εφαρμογές. (Θα απαιτηθεί επανεκκίνηση.)

# MSI Installer UI - Resume Dialog
msi-resume-title = Συνέχιση του Οδηγού εγκατάστασης του { $app_title }
msi-resume-description = Ο Οδηγός εγκατάστασης θα ολοκληρώσει την εγκατάσταση του { $app_title } στον υπολογιστή σας. Κάντε κλικ στο κουμπί «Εγκατάσταση» για να συνεχίσετε ή στο κουμπί «Άκυρο» για να εξέλθετε από τον Οδηγό εγκατάστασης.
msi-resume-btn-install = &Εγκατάσταση

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = Συντόμευση του { $app_title } στην επιφάνεια εργασίας
msi-start-menu-shortcut-description = Συντόμευση του { $app_title } στο μενού Έναρξη
# MSI Installer UI - Readme Dialog
msi-readme-title = Πληροφορίες
msi-readme-description = Παρακαλούμε διαβάστε τις ακόλουθες πληροφορίες πριν συνεχίσετε.
