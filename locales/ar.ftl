# Shared titles
title-update = تحديث { $app_title }
title-setup = إعداد { $app_title }
title-uninstall = إلغاء تثبيت { $app_title }
error-title = خطأ { $program_name }

# Shared buttons
btn-cancel = إلغاء الأمر
btn-install-update = تثبيت التحديث
btn-install = تثبيت
btn-update = تحديث
btn-downgrade = الرجوع إلى إصدار أقدم
btn-repair = إصلاح
btn-open-log = فتح السجل
btn-open-install-dir = فتح دليل التثبيت
btn-ok = موافق
btn-hide = إخفاء
# Elevation (dialogs_common.rs)
elevate-header = أذونات المسؤول مطلوبة
elevate-body = يحتاج { $app_title } إلى أذونات المسؤول لتثبيت الإصدار { $app_version }. هل تسمح بمتابعة هذا التحديث؟

# Restart required (prerequisite.rs)
restart-header = إعادة التشغيل مطلوبة
restart-body = يجب إعادة تشغيل الكمبيوتر قبل أن يتمكن الإعداد من المتابعة. الرجاء إعادة تشغيل الكمبيوتر وتشغيل الإعداد مرة أخرى.

# Missing dependencies (prerequisite.rs)
missing-deps-header = مكونات إضافية مطلوبة
missing-deps-body = يحتاج { $app_title } إلى تثبيت ما يلي أولاً: { $deps }. هل ترغب في تنزيلها وتثبيتها الآن؟

# Uninstall with errors (uninstall)
uninstall-errors-header = اكتمل إلغاء التثبيت مع مشاكل
uninstall-errors-body = تم إلغاء تثبيت { $app_title }، لكن تعذر إزالة بعض الملفات أو المجلدات. يمكنك حذفها يدويًا، أو إعادة تثبيت التطبيق والمحاولة مرة أخرى لإلغاء التثبيت.
uninstall-errors-log = تم حفظ التفاصيل في: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } مثبت بالفعل
overwrite-repair-body = تم تثبيت هذا التطبيق بالفعل على الكمبيوتر. إذا لم يكن يعمل بشكل صحيح، يمكنك محاولة إصلاحه عن طريق إعادة التثبيت.
overwrite-older-installed = { $app_title } مثبت بالفعل
overwrite-update-body = الإصدار { $old_version } مثبت حاليًا. هل ترغب في التحديث إلى الإصدار { $app_version }؟
overwrite-newer-installed = إصدار أحدث من { $app_title } مثبت بالفعل
overwrite-downgrade-body = الإصدار { $old_version } مثبت حاليًا، وهو أحدث من هذا المثبت. لا يُنصح بالرجوع إلى إصدار أقدم وقد يسبب مشاكل. هل تريد المتابعة على أي حال؟
overwrite-footer = مثبت في: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = اكتمل إلغاء التثبيت
uninstall-body = تمت إزالة التطبيق بنجاح من الكمبيوتر الخاص بك.

# Install hook failed (install.rs)
install-hook-header = نجح التثبيت جزئيًا
install-hook-body = اكتمل التثبيت، ولكن قد تكون بعض الخطوات قد فشلت. إذا لم يعمل التطبيق بشكل صحيح، يمكنك محاولة إعادة التثبيت أو الاتصال بمؤلف التطبيق.

# Splash fallback (splash.rs)
splash-header = جاري تثبيت { $app_title }
splash-body = جاري إعداد { $app_title } { $app_version }، الرجاء الانتظار...

# Dependency download (prerequisite.rs)
deps-download-header = جاري تنزيل المكون المطلوب
deps-download-body = جاري تنزيل { $dep_name }، الرجاء الانتظار...

# Apply progress (apply_*_impl.rs)
apply-header = جاري تثبيت التحديث
apply-body = جاري التحديث إلى الإصدار { $app_version }، الرجاء الانتظار...

# Start error (start_windows_impl.rs)
start-corrupt-header = التثبيت تالف
start-corrupt-body = لا يمكن لهذا التطبيق البدء لأن بعض ملفاته مفقودة أو تالفة. الرجاء إعادة تثبيت التطبيق لإصلاح ذلك.

# Generic error
error-header = حدث خطأ ما

# Setup error (wix msi)
setup-error-header = تعذرت متابعة الإعداد

# MSI Installer UI - Common
msi-dlg-title = إعداد { $app_title }
msi-btn-back = ال&سابق
msi-btn-next = التا&لي
msi-btn-cancel = إلغاء الأمر
msi-btn-finish = إ&نهاء
msi-btn-ok = موافق
msi-btn-yes = ن&عم
msi-btn-no = &لا
msi-btn-retry = إ&عادة المحاولة
msi-btn-ignore = تجا&هل

# MSI Installer UI - Welcome Dialog
msi-welcome-title = مرحبًا بك في معالج إعداد { $app_title }
msi-welcome-description = سيقوم معالج الإعداد بتثبيت { $app_title } على الكمبيوتر. انقر فوق التالي للمتابعة، أو انقر فوق إلغاء الأمر لإنهاء معالج الإعداد.
msi-welcome-update-description = سيقوم معالج الإعداد بتحديث { $app_title } على الكمبيوتر. انقر فوق التالي للمتابعة، أو انقر فوق إلغاء الأمر لإنهاء معالج الإعداد.

# MSI Installer UI - Exit Dialog
msi-exit-title = اكتمل معالج إعداد { $app_title }
msi-exit-description = انقر فوق الزر إنهاء لإنهاء معالج الإعداد.
msi-exit-launch-checkbox = تشغيل { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = مرحبًا بك في معالج إعداد { $app_title }
msi-prepare-description = الرجاء الانتظار أثناء قيام معالج الإعداد بالتمهيد لإرشادك خلال عملية التثبيت.

# MSI Installer UI - License Agreement Dialog
msi-license-title = اتفاقية ترخيص المستخدم
msi-license-description = الرجاء قراءة اتفاقية الترخيص التالية بعناية.
msi-license-checkbox = أوافق &على الشروط الواردة في اتفاقية الترخيص

# MSI Installer UI - Install Scope Dialog
msi-scope-title = نطاق التثبيت
msi-scope-description = اختر نطاق التثبيت.
msi-scope-per-user = التثبيت لك &فقط
msi-scope-per-machine = التثبيت لجميع &المستخدمين
msi-scope-per-user-description = يتم التثبيت للمستخدم الحالي فقط
msi-scope-no-per-user-description = تتطلب امتيازات المسؤول
msi-scope-per-machine-description = تتطلب امتيازات المسؤول

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = جاهز لتثبيت { $app_title }
msi-ready-install-text = انقر فوق تثبيت لبدء التثبيت. انقر فوق السابق لمراجعة أي من إعدادات التثبيت أو تغييرها.
msi-ready-change-title = جاهز لتغيير { $app_title }
msi-ready-change-text = انقر فوق تغيير لبدء تغيير التثبيت. انقر فوق السابق لمراجعة أي من إعدادات التثبيت أو تغييرها.
msi-ready-repair-title = جاهز لإصلاح { $app_title }
msi-ready-repair-text = انقر فوق إصلاح لبدء الإصلاح. انقر فوق السابق لمراجعة أي من إعدادات التثبيت أو تغييرها.
msi-ready-remove-title = جاهز لإزالة { $app_title }
msi-ready-remove-text = انقر فوق إزالة لإزالة { $app_title } من الكمبيوتر. انقر فوق السابق لمراجعة أي من إعدادات التثبيت أو تغييرها.
msi-ready-update-title = جاهز لتحديث { $app_title }
msi-ready-update-text = انقر فوق تحديث لبدء التحديث. انقر فوق السابق لمراجعة أي من إعدادات التثبيت أو تغييرها.
msi-ready-btn-install = ت&ثبيت
msi-ready-btn-change = &تغيير
msi-ready-btn-repair = إ&صلاح
msi-ready-btn-remove = إزا&لة
msi-ready-btn-update = ت&حديث

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = تثبيت { $app_title }
msi-progress-installing-text = الرجاء الانتظار أثناء قيام معالج الإعداد بتثبيت { $app_title }.
msi-progress-changing-title = تغيير { $app_title }
msi-progress-changing-text = الرجاء الانتظار أثناء قيام معالج الإعداد بتغيير { $app_title }.
msi-progress-repairing-title = إصلاح { $app_title }
msi-progress-repairing-text = الرجاء الانتظار أثناء قيام معالج الإعداد بإصلاح { $app_title }.
msi-progress-removing-title = إزالة { $app_title }
msi-progress-removing-text = الرجاء الانتظار أثناء قيام معالج الإعداد بإزالة { $app_title }.
msi-progress-updating-title = تحديث { $app_title }
msi-progress-updating-text = الرجاء الانتظار أثناء قيام معالج الإعداد بتحديث { $app_title }.
msi-progress-status = الحالة:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = مرحبًا بك في معالج إعداد { $app_title }
msi-maint-welcome-description = سيسمح لك معالج الإعداد بإصلاح أو إزالة { $app_title }. انقر فوق التالي للمتابعة، أو انقر فوق إلغاء الأمر لإنهاء معالج الإعداد.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = تغيير التثبيت أو إصلاحه أو إزالته
msi-maint-type-description = حدد العملية التي ترغب في تنفيذها.
msi-maint-change-button = &تغيير...
msi-maint-change-tooltip = تغيير...
msi-maint-change-text = يسمح للمستخدمين بتغيير ميزات البرنامج المثبتة وتغيير ميزات فردية.
msi-maint-change-disabled = التغيير معطل حاليًا.
msi-maint-repair-button = إ&صلاح
msi-maint-repair-tooltip = إصلاح
msi-maint-repair-text = إصلاح الأخطاء في آخر تثبيت - إصلاح الملفات والاختصارات وإدخالات السجل المفقودة أو التالفة.
msi-maint-repair-disabled = الإصلاح معطل حاليًا.
msi-maint-remove-button = إزا&لة
msi-maint-remove-tooltip = إزالة
msi-maint-remove-text = إزالة { $app_title } من الكمبيوتر.
msi-maint-remove-disabled = الإزالة معطلة حاليًا.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = هل تريد بالتأكيد إلغاء تثبيت { $app_title }؟

# MSI Installer UI - Browse Dialog
msi-browse-title = تغيير المجلد الوجهة الحالي
msi-browse-description = استعراض للوصول إلى المجلد الوجهة.
msi-browse-combo-label = &البحث في:
msi-browse-path-label = &اسم المجلد:
msi-browse-up-tooltip = مستوى واحد لأعلى
msi-browse-new-folder-tooltip = إنشاء مجلد جديد

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = دليل الوجهة المحدد إما غير صالح أو موجود على نوع محرك أقراص غير معتمد.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = متطلبات مساحة القرص
msi-disk-cost-description = مساحة القرص المطلوبة لتثبيت الميزات المحددة.
msi-disk-cost-text = لا يتوفر في وحدات التخزين المميزة مساحة قرص كافية للميزات المحددة حاليًا. يمكنك إما إزالة بعض الملفات من وحدات التخزين المميزة، أو تثبيت ميزات أقل، أو تحديد محركات أقراص وجهة مختلفة.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = معلومات مثبت { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = انتهى معالج إعداد { $app_title } قبل الأوان
msi-fatal-description1 = تمت مقاطعة إعداد { $app_title }. لم يتم تعديل النظام الخاص بك. لتثبيت هذا البرنامج في وقت لاحق، الرجاء تشغيل الإعداد مرة أخرى.
msi-fatal-description2 = انقر فوق الزر إنهاء لإنهاء معالج الإعداد.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = تمت مقاطعة معالج إعداد { $app_title }
msi-user-exit-description1 = تمت مقاطعة إعداد { $app_title }. لم يتم تعديل النظام الخاص بك. لتثبيت هذا البرنامج في وقت لاحق، الرجاء تشغيل الإعداد مرة أخرى.
msi-user-exit-description2 = انقر فوق الزر إنهاء لإنهاء معالج الإعداد.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = الملفات قيد الاستخدام
msi-files-in-use-description = بعض الملفات التي يلزم تحديثها قيد الاستخدام حاليًا.
msi-files-in-use-text = تستخدم التطبيقات التالية ملفات يلزم تحديثها من خلال هذا الإعداد. أغلق هذه التطبيقات، ثم انقر فوق إعادة المحاولة لمتابعة التثبيت، أو انقر فوق إلغاء الأمر لإنهائه.
msi-files-in-use-exit = إ&نهاء

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = الملفات قيد الاستخدام
msi-rm-files-in-use-description = بعض الملفات التي يلزم تحديثها قيد الاستخدام حاليًا.
msi-rm-files-in-use-text = تستخدم التطبيقات التالية ملفات يلزم تحديثها من خلال هذا الإعداد. يمكنك السماح لمعالج الإعداد بإغلاق هذه التطبيقات تلقائيًا ومحاولة إعادة تشغيلها، أو يمكنك إغلاقها يدويًا والنقر فوق موافق لمتابعة التثبيت.
msi-rm-files-in-use-use-rm = إ&غلاق التطبيقات تلقائيًا ومحاولة إعادة تشغيلها بعد اكتمال الإعداد.
msi-rm-files-in-use-dont-use-rm = &عدم إغلاق التطبيقات. (ستكون إعادة التشغيل مطلوبة.)

# MSI Installer UI - Resume Dialog
msi-resume-title = استئناف معالج إعداد { $app_title }
msi-resume-description = سيقوم معالج الإعداد بإكمال تثبيت { $app_title } على الكمبيوتر. انقر فوق تثبيت للمتابعة، أو انقر فوق إلغاء الأمر لإنهاء معالج الإعداد.
msi-resume-btn-install = ت&ثبيت

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = اختصار سطح المكتب لـ { $app_title }
msi-start-menu-shortcut-description = اختصار قائمة ابدأ لـ { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = معلومات الملف التمهيدي
msi-readme-description = يرجى قراءة المعلومات التالية قبل المتابعة.
