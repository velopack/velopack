# Shared titles
title-update = עדכון { $app_title }
title-setup = התקנת { $app_title }
title-uninstall = הסרת { $app_title }
error-title = שגיאת { $program_name }

# Shared buttons
btn-cancel = ביטול
btn-install-update = התקן עדכון
btn-install = התקן
btn-update = עדכן
btn-downgrade = הורד לגרסה קודמת
btn-repair = תקן
btn-open-log = פתח יומן
btn-open-install-dir = פתח את ספריית ההתקנה

# Elevation (dialogs_common.rs)
elevate-header = נדרשת הרשאת מנהל מערכת
elevate-body = { $app_title } זקוק להרשאת מנהל מערכת כדי להתקין את גרסה { $app_version }. האם לאפשר את המשך העדכון?

# Restart required (prerequisite.rs)
restart-header = נדרשת הפעלה מחדש
restart-body = יש להפעיל מחדש את המחשב לפני שניתן להמשיך בהתקנה. נא להפעיל מחדש את המחשב ולהריץ שוב את ההתקנה.

# Missing dependencies (prerequisite.rs)
missing-deps-header = נדרשים רכיבים נוספים
missing-deps-body = { $app_title } זקוק להתקנת הרכיבים הבאים תחילה: { $deps }. האם ברצונך להוריד ולהתקין אותם כעת?

# Uninstall with errors (uninstall)
uninstall-errors-header = ההסרה הסתיימה עם בעיות
uninstall-errors-body = { $app_title } הוסר, אך לא ניתן היה להסיר חלק מהקבצים או התיקיות. באפשרותך למחוק אותם באופן ידני, או להתקין מחדש את היישום ולנסות להסיר שוב.
uninstall-errors-log = הפרטים נשמרו ב: { $path }

# Overwrite/repair dialog (install.rs)
overwrite-already-installed = { $app_title } כבר מותקן
overwrite-repair-body = יישום זה כבר מותקן במחשב שלך. אם הוא אינו פועל כהלכה, באפשרותך לנסות לתקן אותו על-ידי התקנה מחדש.
overwrite-older-installed = { $app_title } כבר מותקן
overwrite-update-body = גרסה { $old_version } מותקנת כעת. האם ברצונך לעדכן לגרסה { $app_version }?
overwrite-newer-installed = גרסה חדשה יותר של { $app_title } כבר מותקנת
overwrite-downgrade-body = גרסה { $old_version } מותקנת כעת, שהיא חדשה יותר מתוכנית התקנה זו. הורדה לגרסה קודמת אינה מומלצת ועלולה לגרום לבעיות. האם להמשיך בכל זאת?
overwrite-footer = הותקן ב: { $path }

# Uninstall complete (uninstall.rs)
uninstall-header = ההסרה הושלמה
uninstall-body = היישום הוסר בהצלחה מהמחשב שלך.

# Install hook failed (install.rs)
install-hook-header = ההתקנה הצליחה חלקית
install-hook-body = ההתקנה הושלמה, אך ייתכן שחלק מהשלבים נכשלו. אם היישום אינו פועל כהלכה, באפשרותך לנסות להתקין מחדש או ליצור קשר עם מחבר היישום.

# Splash fallback (splash.rs)
splash-header = מתקין את { $app_title }
splash-body = מגדיר את { $app_title } { $app_version }, נא להמתין...

# Dependency download (prerequisite.rs)
deps-download-header = הורדת רכיב נדרש
deps-download-body = הורדת { $dep_name }, נא להמתין...

# Apply progress (apply_*_impl.rs)
apply-header = התקנת עדכון
apply-body = מעדכן לגרסה { $app_version }, נא להמתין...

# Start error (start_windows_impl.rs)
start-corrupt-header = ההתקנה פגומה
start-corrupt-body = יישום זה אינו יכול להתחיל מאחר שחלק מהקבצים שלו חסרים או פגומים. נא להתקין מחדש את היישום כדי לפתור בעיה זו.

# Generic error
error-header = משהו השתבש

# Setup error (wix msi)
setup-error-header = ההתקנה לא יכלה להמשיך

# MSI Installer UI - Common
msi-dlg-title = תוכנית ההתקנה של { $app_title }
msi-btn-back = ה&קודם
msi-btn-next = ה&בא
msi-btn-cancel = ביטול
msi-btn-finish = &סיום
msi-btn-ok = אישור
msi-btn-yes = &כן
msi-btn-no = &לא
msi-btn-retry = &נסה שוב
msi-btn-ignore = ה&תעלם

# MSI Installer UI - Welcome Dialog
msi-welcome-title = ברוך הבא אל אשף ההתקנה של { $app_title }
msi-welcome-description = אשף ההתקנה יתקין את { $app_title } במחשב שלך. לחץ על 'הבא' כדי להמשיך או על 'ביטול' כדי לצאת מאשף ההתקנה.
msi-welcome-update-description = אשף ההתקנה יעדכן את { $app_title } במחשב. לחץ על 'הבא' כדי להמשיך או על 'ביטול' כדי לצאת מאשף ההתקנה.

# MSI Installer UI - Exit Dialog
msi-exit-title = פעולתו של אשף ההתקנה של { $app_title } הושלמה
msi-exit-description = לחץ על לחצן 'סיום' כדי לצאת מאשף ההתקנה.
msi-exit-launch-checkbox = הפעל את { $app_title }

# MSI Installer UI - Prepare Dialog
msi-prepare-title = ברוך הבא אל אשף ההתקנה של { $app_title }
msi-prepare-description = נא המתן בעת שאשף ההתקנה מתכונן להנחות אותך בתהליך ההתקנה.

# MSI Installer UI - License Agreement Dialog
msi-license-title = הסכם רשיון למשתמש קצה
msi-license-description = קרא בעיון את הסכם הרשיון שלהלן.
msi-license-checkbox = אני &מקבל את תנאי הסכם הרשיון

# MSI Installer UI - Install Scope Dialog
msi-scope-title = טווח התקנה
msi-scope-description = בחר את טווח ההתקנה.
msi-scope-per-user = התקן &רק עבורך
msi-scope-per-machine = התקן עבור &כל המשתמשים
msi-scope-per-user-description = מתקין עבור המשתמש הנוכחי בלבד
msi-scope-no-per-user-description = נדרשות הרשאות מנהל מערכת
msi-scope-per-machine-description = נדרשות הרשאות מנהל מערכת

# MSI Installer UI - Verify Ready Dialog
msi-ready-install-title = מוכן להתקנת { $app_title }
msi-ready-install-text = לחץ על 'התקן' כדי להתחיל בהתקנה. לחץ על 'הקודם' כדי לסקור או לשנות הגדרה כלשהי מהגדרות ההתקנה.
msi-ready-change-title = מוכן לשינוי { $app_title }
msi-ready-change-text = לחץ על 'שנה' כדי להתחיל בשינוי ההתקנה. לחץ על 'הקודם' כדי לסקור או לשנות הגדרה כלשהי מהגדרות ההתקנה.
msi-ready-repair-title = מוכן לתיקון { $app_title }
msi-ready-repair-text = לחץ על 'תקן' כדי להתחיל בתיקון. לחץ על 'הקודם' כדי לסקור או לשנות הגדרה כלשהי מהגדרות ההתקנה.
msi-ready-remove-title = מוכן להסרת { $app_title }
msi-ready-remove-text = לחץ על 'הסר' כדי להסיר את { $app_title } מהמחשב. לחץ על 'הקודם' כדי לסקור או לשנות הגדרה כלשהי מהגדרות ההתקנה.
msi-ready-update-title = מוכן לעדכון { $app_title }
msi-ready-update-text = לחץ על 'עדכן' כדי להתחיל בעדכון. לחץ על 'הקודם' כדי לסקור או לשנות הגדרה כלשהי מהגדרות ההתקנה.
msi-ready-btn-install = ה&תקן
msi-ready-btn-change = &שנה
msi-ready-btn-repair = &תקן
msi-ready-btn-remove = ה&סר
msi-ready-btn-update = &עדכן

# MSI Installer UI - Progress Dialog
msi-progress-installing-title = התקנת { $app_title }
msi-progress-installing-text = נא המתן בזמן שאשף ההתקנה מתקין את { $app_title }.
msi-progress-changing-title = שינוי { $app_title }
msi-progress-changing-text = נא המתן בזמן שאשף ההתקנה משנה את { $app_title }.
msi-progress-repairing-title = תיקון { $app_title }
msi-progress-repairing-text = נא המתן בזמן שאשף ההתקנה מתקן את { $app_title }.
msi-progress-removing-title = הסרת { $app_title }
msi-progress-removing-text = נא המתן בזמן שאשף ההתקנה מסיר את { $app_title }.
msi-progress-updating-title = עדכון { $app_title }
msi-progress-updating-text = נא המתן בזמן שאשף ההתקנה מעדכן את { $app_title }.
msi-progress-status = מצב:

# MSI Installer UI - Maintenance Welcome Dialog
msi-maint-welcome-title = ברוך הבא אל אשף ההתקנה של { $app_title }
msi-maint-welcome-description = אשף ההתקנה יאפשר לך לתקן או להסיר את { $app_title }. לחץ על 'הבא' כדי להמשיך או על 'ביטול' כדי לצאת מאשף ההתקנה.

# MSI Installer UI - Maintenance Type Dialog
msi-maint-type-title = שינוי, תיקון או הסרה של ההתקנה
msi-maint-type-description = בחר את הפעולה שברצונך לבצע.
msi-maint-change-button = &שנה...
msi-maint-change-tooltip = שנה...
msi-maint-change-text = מאפשר למשתמשים לשנות אילו תכונות תוכנית מותקנות ולשנות תכונות בודדות.
msi-maint-change-disabled = שינוי כעת לא זמין.
msi-maint-repair-button = &תקן
msi-maint-repair-tooltip = תקן
msi-maint-repair-text = תיקון שגיאות בהתקנה האחרונה - תיקון קבצים, קיצורי דרך וערכי רישום חסרים או פגומים.
msi-maint-repair-disabled = תיקון כעת לא זמין.
msi-maint-remove-button = ה&סר
msi-maint-remove-tooltip = הסר
msi-maint-remove-text = הסרת { $app_title } מהמחשב.
msi-maint-remove-disabled = הסרה כעת לא זמינה.

# MSI Installer UI - Cancel Dialog
msi-cancel-text = האם אתה בטוח שברצונך לבטל את ההתקנה של { $app_title }?

# MSI Installer UI - Browse Dialog
msi-browse-title = שינוי תיקיית היעד הנוכחית
msi-browse-description = עבור אל תיקיית היעד.
msi-browse-combo-label = &חפש ב:
msi-browse-path-label = &שם תיקיה:
msi-browse-up-tooltip = רמה אחת למעלה
msi-browse-new-folder-tooltip = צור תיקיה חדשה

# MSI Installer UI - Invalid Directory Dialog
msi-invalid-dir-text = ספריית היעד שצוינה אינה חוקית או נמצאת בסוג כונן שאינו נתמך.

# MSI Installer UI - Disk Cost Dialog
msi-disk-cost-title = דרישות שטח דיסק
msi-disk-cost-description = שטח הדיסק הנדרש להתקנת התכונות שנבחרו.
msi-disk-cost-text = באמצעי האחסון המסומנים אין די שטח דיסק זמין עבור התכונות הנוכחיות שנבחרו. באפשרותך להסיר קבצים מסוימים מאמצעי האחסון המסומנים, להתקין פחות תכונות או לבחור כונן יעד אחר.

# MSI Installer UI - Error Dialog
msi-error-dlg-title = מידע על תוכנית ההתקנה של { $app_title }

# MSI Installer UI - Fatal Error Dialog
msi-fatal-title = פעולתו של אשף ההתקנה של { $app_title } הסתיימה מוקדם מהצפוי
msi-fatal-description1 = התקנת { $app_title } הופסקה. המערכת שלך לא השתנתה. כדי להתקין תוכנית זו במועד מאוחר יותר, נא להפעיל שוב את ההתקנה.
msi-fatal-description2 = לחץ על לחצן 'סיום' כדי לצאת מאשף ההתקנה.

# MSI Installer UI - User Exit Dialog
msi-user-exit-title = פעולתו של אשף ההתקנה של { $app_title } הופסקה
msi-user-exit-description1 = התקנת { $app_title } הופסקה. המערכת שלך לא השתנתה. כדי להתקין תוכנית זו במועד מאוחר יותר, נא להפעיל שוב את ההתקנה.
msi-user-exit-description2 = לחץ על לחצן 'סיום' כדי לצאת מאשף ההתקנה.

# MSI Installer UI - Files In Use Dialog
msi-files-in-use-title = קבצים בשימוש
msi-files-in-use-description = חלק מהקבצים שיש לעדכן נמצאים כעת בשימוש.
msi-files-in-use-text = היישומים הבאים עושים שימוש בקבצים שתוכנית התקנה זו חייבת לעדכן. סגור יישומים אלה ולאחר מכן לחץ על 'נסה שוב' כדי להמשיך בהתקנה, או על 'ביטול' כדי לצאת ממנה.
msi-files-in-use-exit = י&ציאה

# MSI Installer UI - Restart Manager Files In Use Dialog
msi-rm-files-in-use-title = קבצים בשימוש
msi-rm-files-in-use-description = חלק מהקבצים שיש לעדכן נמצאים כעת בשימוש.
msi-rm-files-in-use-text = היישומים הבאים משתמשים בקבצים שתוכנית התקנה זו צריכה לעדכן. באפשרותך לאפשר לאשף ההתקנה לסגור ולנסות להפעיל מחדש יישומים אלה באופן אוטומטי, או שתוכל לסגור אותם באופן ידני וללחוץ על 'אישור' כדי להמשיך בהתקנה.
msi-rm-files-in-use-use-rm = &סגור באופן אוטומטי את היישומים ונסה להפעיל אותם מחדש לאחר השלמת ההתקנה.
msi-rm-files-in-use-dont-use-rm = &אל תסגור את היישומים. (יידרש אתחול מחדש.)

# MSI Installer UI - Resume Dialog
msi-resume-title = חידוש פעולתו של אשף ההתקנה של { $app_title }
msi-resume-description = אשף ההתקנה ישלים את התקנת { $app_title } במחשב שלך. לחץ על 'התקן' כדי להמשיך, או על 'ביטול' כדי לצאת מאשף ההתקנה.
msi-resume-btn-install = ה&תקן

# MSI Installer UI - Shortcut Descriptions
msi-desktop-shortcut-description = קיצור דרך לשולחן העבודה עבור { $app_title }
msi-start-menu-shortcut-description = קיצור דרך לתפריט התחלה עבור { $app_title }
# MSI Installer UI - Readme Dialog
msi-readme-title = מידע חשוב
msi-readme-description = אנא קראו את המידע הבא לפני שתמשיכו.
