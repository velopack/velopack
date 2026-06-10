# Shared titles
title-update = { $app_title } अद्यतन
title-setup = { $app_title } सेटअप
title-uninstall = { $app_title } अनइंस्टॉल
error-title = { $program_name } त्रुटि

# Shared buttons
btn-cancel = रद्द करें
btn-install-update = अद्यतन स्थापित करें
btn-install = स्थापित करें
btn-update = अद्यतन
btn-downgrade = डाउनग्रेड
btn-repair = सुधारें
btn-open-log = लॉग खोलें
btn-open-install-dir = स्थापना निर्देशिका खोलें
btn-ok = ठीक
btn-hide = छुपाएँ
# Elevation (dialogs_common.rs)
elevate-header = व्यवस्थापक अनुमति आवश्यक है
elevate-body = { $app_title } को संस्करण { $app_version } स्थापित करने के लिए व्यवस्थापक अनुमति की आवश्यकता है. क्या इस अद्यतन को जारी रखने की अनुमति दी जाए?

# Restart required (prerequisite.rs)
restart-header = पुनः प्रारंभ आवश्यक है
restart-body = सेटअप जारी रखने से पहले आपके कंप्यूटर को पुनः प्रारंभ करने की आवश्यकता है. कृपया अपने कंप्यूटर को पुनः प्रारंभ करें और सेटअप पुनः चलाएँ.

# Missing dependencies (prerequisite.rs)
missing-deps-header = अतिरिक्त घटक आवश्यक हैं
missing-deps-body = { $app_title } को पहले निम्नलिखित स्थापित करने की आवश्यकता है: { $deps }. क्या आप उन्हें अभी डाउनलोड और स्थापित करना चाहेंगे?

# Uninstall with errors (uninstall)
uninstall-errors-header = समस्याओं के साथ अनइंस्टॉल पूर्ण
uninstall-errors-body = { $app_title } को अनइंस्टॉल कर दिया गया था, लेकिन कुछ फ़ाइलें या फ़ोल्डर निकाले नहीं जा सके. आप उन्हें मैन्युअल रूप से हटा सकते हैं, या अनुप्रयोग पुनः स्थापित कर अनइंस्टॉल का प्रयास फिर से कर सकते हैं.
uninstall-errors-log = विवरण यहाँ सहेजे गए: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } पहले से स्थापित है
overwrite-repair-body = यह अनुप्रयोग आपके कंप्यूटर पर पहले से स्थापित है. यदि यह सही ढंग से कार्य नहीं कर रहा है, तो आप पुनः स्थापित करके इसे सुधारने का प्रयास कर सकते हैं.
overwrite-older-installed = { $app_title } पहले से स्थापित है
overwrite-update-body = वर्तमान में संस्करण { $old_version } स्थापित है. क्या आप संस्करण { $app_version } में अद्यतन करना चाहेंगे?
overwrite-newer-installed = { $app_title } का एक नया संस्करण पहले से स्थापित है
overwrite-downgrade-body = वर्तमान में संस्करण { $old_version } स्थापित है, जो इस इंस्टॉलर से नया है. डाउनग्रेड करने की अनुशंसा नहीं की जाती है और इससे समस्याएँ हो सकती हैं. क्या फिर भी जारी रखें?
overwrite-footer = स्थापित: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = अनइंस्टॉल पूर्ण
uninstall-body = अनुप्रयोग आपके कंप्यूटर से सफलतापूर्वक निकाल दिया गया है.

# Install hook failed (install.rs)
install-hook-header = स्थापना आंशिक रूप से सफल हुई
install-hook-body = स्थापना पूर्ण हो गई है, लेकिन कुछ चरण विफल हो सकते हैं. यदि अनुप्रयोग सही ढंग से कार्य नहीं करता है, तो आप पुनः स्थापित करने का प्रयास कर सकते हैं या अनुप्रयोग लेखक से संपर्क कर सकते हैं.

# Splash fallback (splash.rs)
splash-header = { $app_title } स्थापित कर रहा है
splash-body = { $app_title } { $app_version } सेट कर रहा है, कृपया प्रतीक्षा करें...

# Dependency download (prerequisite.rs)
deps-download-header = आवश्यक घटक डाउनलोड कर रहा है
deps-download-body = { $dep_name } डाउनलोड कर रहा है, कृपया प्रतीक्षा करें...

# Apply progress (apply_*_impl.rs)
apply-header = अद्यतन स्थापित कर रहा है
apply-body = संस्करण { $app_version } में अद्यतन कर रहा है, कृपया प्रतीक्षा करें...
progress-cancelling = रद्द किया जा रहा है...

# Start error (start_windows_impl.rs)
start-corrupt-header = स्थापना क्षतिग्रस्त है
start-corrupt-body = यह अनुप्रयोग प्रारंभ नहीं हो सकता क्योंकि इसकी कुछ फ़ाइलें गुम या क्षतिग्रस्त हैं. इसे ठीक करने के लिए कृपया अनुप्रयोग पुनः स्थापित करें.

# Generic error
error-header = कुछ गलत हुआ

# Setup error (wix msi)
setup-error-header = सेटअप जारी नहीं रह सका

# MSI Installer UI - Common
msi-dlg-title = { $app_title } सेटअप
msi-btn-back = &पीछे
msi-btn-next = &अगला
msi-btn-cancel = रद्द करें
msi-btn-finish = &समाप्त करें
msi-btn-ok = ठीक
msi-btn-yes = &हाँ
msi-btn-no = &नहीं
msi-btn-retry = &पुनर्प्रयास करें
msi-btn-ignore = &ध्यान न दें

# MSI Installer UI - Welcome Dialog
msi-welcome-title = { $app_title } सेटअप विज़ार्ड में स्वागत है
msi-welcome-description = सेटअप विज़ार्ड { $app_title } को आपके कंप्यूटर पर स्थापित करेगा. जारी रखने के लिए अगला क्लिक करें या सेटअप विज़ार्ड से बाहर निकलने के लिए रद्द करें क्लिक करें.
msi-welcome-update-description = सेटअप विज़ार्ड { $app_title } को आपके कंप्यूटर पर अद्यतन करेगा. जारी रखने के लिए अगला क्लिक करें या सेटअप विज़ार्ड से बाहर निकलने के लिए रद्द करें क्लिक करें.

# MSI Installer UI - Exit Dialog
msi-exit-title = { $app_title } सेटअप विज़ार्ड पूरा हुआ
msi-exit-description = सेटअप विज़ार्ड से बाहर निकलने के लिए समाप्ति बटन क्लिक करें.
msi-exit-launch-checkbox = { $app_title } प्रारंभ करें

# MSI Installer UI - Prepare Dialog
msi-prepare-title = { $app_title } सेटअप विज़ार्ड में स्वागत है
msi-prepare-description = सेटअप विज़ार्ड आपको स्थापना में सहायता करे, तब तक कृपया प्रतीक्षा करें.

# MSI Installer UI - License Agreement Dialog
msi-license-title = एंड-यूज़र लाइसेंस एग्रीमेंट
msi-license-description = कृपया निम्न लाइसेंस अनुबंध को ध्यानपूर्वक पढ़ें.
msi-license-checkbox = मुझे &लायसेंस एग्रीमेंट की शर्तें स्वीकार हैं

# MSI Installer UI - Install Scope Dialog
msi-scope-title = स्थापना क्षेत्र
msi-scope-description = स्थापना क्षेत्र चुनें.
msi-scope-per-user = केवल आ&पके लिए स्थापित करें
msi-scope-per-machine = स&भी उपयोगकर्ताओं के लिए स्थापित करें
msi-scope-per-user-description = केवल वर्तमान उपयोगकर्ता के लिए स्थापित होता है
msi-scope-no-per-user-description = व्यवस्थापक विशेषाधिकार आवश्यक हैं
msi-scope-per-machine-description = व्यवस्थापक विशेषाधिकार आवश्यक हैं

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = { $app_title } स्थापित करने के लिए तैयार
msi-ready-install-text = स्थापना प्रारंभ करने के लिए स्थापित करें क्लिक करें. अपनी स्थापना सेटिंग देखने या बदलने के लिए वापस क्लिक करें.
msi-ready-change-title = { $app_title } बदलने के लिए तैयार
msi-ready-change-text = स्थापना बदलना प्रारंभ करने के लिए बदलें क्लिक करें. अपनी स्थापना सेटिंग देखने या बदलने के लिए वापस क्लिक करें.
msi-ready-repair-title = { $app_title } सुधारने के लिए तैयार
msi-ready-repair-text = सुधार प्रारंभ करने के लिए सुधारें क्लिक करें. अपनी स्थापना सेटिंग देखने या बदलने के लिए वापस क्लिक करें.
msi-ready-remove-title = { $app_title } निकालने के लिए तैयार
msi-ready-remove-text = अपने कंप्यूटर से { $app_title } निकालने के लिए निकालें क्लिक करें. अपनी स्थापना सेटिंग देखने या बदलने के लिए वापस क्लिक करें.
msi-ready-update-title = { $app_title } अद्यतन करने के लिए तैयार
msi-ready-update-text = अद्यतन प्रारंभ करने के लिए अद्यतन क्लिक करें. अपनी स्थापना सेटिंग देखने या बदलने के लिए वापस क्लिक करें.
msi-ready-btn-install = &स्थापित करें
msi-ready-btn-change = &बदलें
msi-ready-btn-repair = सु&धारें
msi-ready-btn-remove = &निकालें
msi-ready-btn-update = &अद्यतन

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = { $app_title } स्थापित कर रहा है
msi-progress-installing-text = कृपया प्रतीक्षा करें जबकि सेटअप विज़ार्ड { $app_title } स्थापित कर रहा है.
msi-progress-changing-title = { $app_title } बदल रहा है
msi-progress-changing-text = कृपया प्रतीक्षा करें जबकि सेटअप विज़ार्ड { $app_title } बदल रहा है.
msi-progress-repairing-title = { $app_title } सुधार रहा है
msi-progress-repairing-text = कृपया प्रतीक्षा करें जबकि सेटअप विज़ार्ड { $app_title } सुधार रहा है.
msi-progress-removing-title = { $app_title } निकाल रहा है
msi-progress-removing-text = कृपया प्रतीक्षा करें जबकि सेटअप विज़ार्ड { $app_title } निकाल रहा है.
msi-progress-updating-title = { $app_title } को अद्यतन कर रहा है
msi-progress-updating-text = कृपया प्रतीक्षा करें जबकि सेटअप विज़ार्ड { $app_title } अद्यतन कर रहा है.
msi-progress-status = स्थिति:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = { $app_title } सेटअप विज़ार्ड में स्वागत है
msi-maint-welcome-description = सेटअप विज़ार्ड आपको { $app_title } को सुधारने या निकालने की अनुमति देगा. जारी रखने के लिए अगला क्लिक करें या सेटअप विज़ार्ड से बाहर निकलने के लिए रद्द करें क्लिक करें.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = स्थापना बदलें, सुधारें या निकालें
msi-maint-type-description = आप जो कार्रवाई करना चाहते हैं, उसका चयन करें.
msi-maint-change-button = &बदलें...
msi-maint-change-tooltip = बदलें...
msi-maint-change-text = उपयोगकर्ताओं को यह बदलने की अनुमति देता है कि कौन सी प्रोग्राम सुविधाएँ स्थापित हैं और व्यक्तिगत सुविधाओं को बदलने की.
msi-maint-change-disabled = बदलना वर्तमान में अक्षम है.
msi-maint-repair-button = सु&धारें
msi-maint-repair-tooltip = सुधारें
msi-maint-repair-text = सबसे हाल की स्थापना में त्रुटियों को सुधारता है - गुम या दूषित फ़ाइलें, शॉर्टकट और रजिस्ट्री प्रविष्टियों को ठीक करता है.
msi-maint-repair-disabled = सुधार वर्तमान में अक्षम है.
msi-maint-remove-button = नि&कालें
msi-maint-remove-tooltip = निकालें
msi-maint-remove-text = आपके कंप्यूटर से { $app_title } को निकालता है.
msi-maint-remove-disabled = निकालना वर्तमान में अक्षम है.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = क्या आप वाकई { $app_title } की स्थापना रद्द करना चाहते हैं?

# MSI Installer UI - Browse Dialog
msi-browse-title = वर्तमान गंतव्य फ़ोल्डर बदलें
msi-browse-description = गंतव्य फ़ोल्डर ब्राउज़ करें.
msi-browse-combo-label = &इसमें देखें:
msi-browse-path-label = &फ़ोल्डर नाम:
msi-browse-up-tooltip = एक स्तर ऊपर
msi-browse-new-folder-tooltip = नया फ़ोल्डर बनाएँ

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = निर्दिष्ट गंतव्य निर्देशिका या तो अमान्य है या असमर्थित ड्राइव प्रकार पर है.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = डिस्क स्थान आवश्यकताएँ
msi-disk-cost-description = चयनित सुविधाओं की स्थापना के लिए आवश्यक डिस्क स्थान.
msi-disk-cost-text = हाइलाइट किए गए वॉल्यूम में वर्तमान में चयनित सुविधाओं के लिए पर्याप्त डिस्क स्थान उपलब्ध नहीं है. आप हाइलाइट किए गए वॉल्यूम से कुछ फ़ाइलें निकाल सकते हैं, कम सुविधाएँ स्थापित कर सकते हैं, या भिन्न गंतव्य ड्राइव चुन सकते हैं.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = { $app_title } इंस्टॉलर जानकारी

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = { $app_title } सेटअप विज़ार्ड समय के पहले ही रुक गया
msi-fatal-description1 = { $app_title } सेटअप बाधित हो गया था. आपका सिस्टम संशोधित नहीं किया गया है. यह प्रोग्राम बाद में स्थापित करने के लिए, कृपया सेटअप फिर से चलाएँ.
msi-fatal-description2 = सेटअप विज़ार्ड से बाहर निकलने के लिए समाप्ति बटन क्लिक करें.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = { $app_title } सेटअप विज़ार्ड बाधित हुआ था
msi-user-exit-description1 = { $app_title } सेटअप बाधित हो गया था. आपका सिस्टम संशोधित नहीं किया गया है. यह प्रोग्राम बाद में स्थापित करने के लिए कृपया सेटअप फिर से चलाएँ.
msi-user-exit-description2 = सेटअप विज़ार्ड से बाहर निकलने के लिए समाप्ति बटन क्लिक करें.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = फ़ाइलें उपयोग में
msi-files-in-use-description = कुछ ऐसी फ़ाइलें अभी उपयोग में हैं, जिन्हें अद्यतन करने की आवश्यकता है.
msi-files-in-use-text = निम्न अनुप्रयोगों द्वारा उपयोग की जाने वाली फ़ाइलों का इस सेटअप से अद्यतन होना आवश्यक है. इन अनुप्रयोगों को बंद करें और फिर स्थापना जारी रखने के लिए पुनर्प्रयास करें क्लिक करें या उससे बाहर निकलने के लिए रद्द करें क्लिक करें.
msi-files-in-use-exit = बा&हर जाएँ

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = फ़ाइलें उपयोग में
msi-rm-files-in-use-description = कुछ ऐसी फ़ाइलें अभी उपयोग में हैं, जिन्हें अद्यतन करने की आवश्यकता है.
msi-rm-files-in-use-text = निम्न अनुप्रयोगों द्वारा उपयोग की जाने वाली फ़ाइलों का इस सेटअप से अद्यतन होना आवश्यक है. आप सेटअप विज़ार्ड को स्वतः इन्हें बंद करने और फिर से प्रारंभ करने का प्रयास करने दे सकते हैं या आप उन्हें मैन्युअल रूप से बंद कर सकते हैं और स्थापना जारी रखने के लिए ठीक क्लिक कर सकते हैं.
msi-rm-files-in-use-use-rm = स्वतः अनुप्रयोग &बंद करें और सेटअप पूरा होने के बाद उन्हें फिर से प्रारंभ करने का प्रयास करें.
msi-rm-files-in-use-dont-use-rm = अनुप्रयोग बंद &न करें. (एक रीबूट की आवश्यकता होगी.)

# MSI Installer UI - Resume Dialog
msi-resume-title = { $app_title } सेटअप विज़ार्ड फिर से शुरू कर रहा है
msi-resume-description = सेटअप विज़ार्ड आपके कंप्यूटर पर { $app_title } की स्थापना पूरी करेगा. जारी रखने के लिए स्थापित करें क्लिक करें या सेटअप विज़ार्ड से बाहर निकलने के लिए रद्द करें क्लिक करें.
msi-resume-btn-install = &स्थापित करें

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = { $app_title } के लिए डेस्कटॉप शॉर्टकट
msi-start-menu-shortcut-description = { $app_title } के लिए प्रारंभ मेनू शॉर्टकट
# MSI Installer UI - Readme Dialog
msi-readme-title = महत्वपूर्ण जानकारी
msi-readme-description = कृपया आगे बढ़ने से पहले निम्नलिखित जानकारी पढ़ें।
