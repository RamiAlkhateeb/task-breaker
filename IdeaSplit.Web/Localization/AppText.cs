using System.Globalization;
using IdeaSplit.Shared.Models;
using Nxt.UI;

namespace IdeaSplit.Web.Localization;

/// <summary>
/// NxtTask's own UI strings in English and Arabic (shared components use Nxt.UI's NxtText).
/// Also owns date labels and count phrases, which differ by language beyond simple word swaps.
/// </summary>
public sealed class AppText
{
    private readonly INxtLocale _locale;

    public AppText(INxtLocale locale) => _locale = locale;

    private bool IsArabic => _locale.Language == "ar";

    public string this[string key] =>
        (Strings.TryGetValue(_locale.Language, out var table) && table.TryGetValue(key, out var value)) ? value
        : Strings["en"].TryGetValue(key, out var en) ? en
        : key;

    public string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

    // ---- Counts ("1 task", "مهمتان", "٥ مهام", "١٢ مهمة") ----

    public string Tasks(int count) => IsArabic
        ? Arabic(count, "مهمة واحدة", "مهمتان", "مهام", "مهمة")
        : count == 1 ? "1 task" : $"{count} tasks";

    public string Days(int count) => IsArabic
        ? Arabic(count, "يوم واحد", "يومان", "أيام", "يومًا")
        : count == 1 ? "1 day" : $"{count} days";

    public string OpenProjects(int count) => IsArabic
        ? Arabic(count, "مشروع مفتوح واحد", "مشروعان مفتوحان", "مشاريع مفتوحة", "مشروعًا مفتوحًا")
        : count == 1 ? "1 open project" : $"{count} open projects";

    private static string Arabic(int count, string one, string two, string few, string many) => count switch
    {
        1 => one,
        2 => two,
        >= 3 and <= 10 => $"{count} {few}",
        _ => $"{count} {many}"
    };

    // ---- Dates ----

    /// <summary>"Today", "Tomorrow", or a short date.</summary>
    public string DayLabel(DateTime date) => (date.Date - DateTime.Today).Days switch
    {
        0 => this["today"],
        1 => this["tomorrow"],
        _ => ShortDate(date)
    };

    public string ShortDate(DateTime date) => date.ToString(IsArabic ? "d MMMM" : "MMM d", CultureInfo.CurrentCulture);

    /// <summary>A day project's name: "Thursday, Oct 3" / "الخميس، 3 تشرين الأول".</summary>
    public string DayName(DateTime date) => date.ToString(IsArabic ? "dddd، d MMMM" : "dddd, MMM d", CultureInfo.CurrentCulture);

    /// <summary>
    /// Day projects are stored with an English name generated from their date; show that name in the
    /// current language. Titles the user (or an older version) set to anything else are shown as-is.
    /// </summary>
    public string ProjectTitle(Project project) =>
        project.ScheduleType == TaskScheduleType.DayWeek && project.ScheduleDate is { } date
            && project.Title == date.ToString("dddd, MMM d", CultureInfo.InvariantCulture)
            ? DayName(date)
            : project.Title;

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new()
        {
            ["back"] = "Back",
            ["save"] = "Save",
            ["edit"] = "Edit",
            ["delete"] = "Delete",
            ["add"] = "Add",
            ["cancel"] = "Cancel",
            ["more_options"] = "More options",
            ["mark_done"] = "Mark done",
            ["today"] = "Today",
            ["tomorrow"] = "Tomorrow",
            ["date"] = "Date",
            ["deadline"] = "Deadline",
            ["overdue"] = "Overdue · {0}",
            ["task_progress"] = "{0}/{1} tasks",
            ["not_found"] = "Page not found.",

            ["assistant"] = "Assistant",
            ["archive"] = "Archive",
            ["settings"] = "Settings",
            ["search_placeholder"] = "Search days and tasks",
            ["clear_search"] = "Clear search",
            ["loading_projects"] = "Loading projects",
            ["empty_art"] = "Lightbulb and empty checklist illustration",
            ["no_projects"] = "No projects yet",
            ["no_projects_hint"] = "Describe a plan in the box below and it gets split into a checklist for you.",
            ["no_matches"] = "No matches for “{0}”.",
            ["pin"] = "Pin project",
            ["unpin"] = "Unpin project",
            ["delete_project"] = "Delete project",
            ["confirm_delete_project"] = "Delete this project and all of its tasks?",

            ["loading_project"] = "Loading project",
            ["edit_project_name"] = "Edit project name",
            ["done_of"] = "{0} of {1} tasks done",
            ["generated_with"] = "Tasks generated with {0}.",
            ["all_done"] = "All tasks done",
            ["completed"] = "Completed ({0})",
            ["move_next_day"] = "Move to next day",
            ["duplicate_next_day"] = "Duplicate to next day",
            ["add_subtask"] = "Add subtask",
            ["add_subtask_placeholder"] = "Add a subtask",
            ["add_task_placeholder"] = "Add a task",

            ["loading_archive"] = "Loading archived projects",
            ["no_archived"] = "No archived projects",
            ["no_archived_hint"] = "Completed projects show up here automatically.",
            ["no_tasks"] = "No tasks.",
            ["view_tasks"] = "View tasks",
            ["hide_tasks"] = "Hide tasks",
            ["unarchive"] = "Unarchive project",

            ["plan_placeholder"] = "Describe your plan...",
            ["submit"] = "Submit",
            ["when_title"] = "When is this plan for?",
            ["plan_length"] = "Plan length",
            ["one_day"] = "One day",
            ["multiple_days"] = "Multiple days",
            ["in_3_days"] = "In 3 days",
            ["in_a_week"] = "In a week",
            ["in_2_weeks"] = "In 2 weeks",
            ["deadline_hint"] = "One project per day from today to the deadline (up to {0} days), with your tasks spread across them.",
            ["create"] = "Create",
            ["planning"] = "Planning...",
            ["pick_date"] = "Pick a date.",
            ["pick_future"] = "Pick today or a future date.",
            ["pick_deadline"] = "Pick a deadline between tomorrow and {0} days from now.",
            ["added_to_days"] = "Added tasks to {0}.",
            ["entry_kind"] = "Type",
            ["kind_task"] = "Task",
            ["kind_project"] = "Project",
            ["task_hint"] = "Added as-is, no AI breakdown.",
            ["added_tasks_to_day"] = "Added {0} to {1}.",

            ["assistant_subtitle"] = "Talk through your week, then add what fits",
            ["assistant_empty"] = "Tell me what you have going on. I'll ask a question or two, suggest tasks for your week, and add them once you say so.",
            ["assistant_placeholder"] = "What do you have going on?",
            ["suggestion_1"] = "I have an exam on Thursday",
            ["suggestion_2"] = "Busy week at work, but I want to exercise",
            ["suggestion_3"] = "Help me plan this week",
            ["added_summary"] = "Added {0} to {1}.",
            ["suggest_default"] = "Here's what I'd suggest. Want me to add these to your plan?",
            ["open_my_days"] = "Open my days",

            ["dictation_title"] = "Dictation",
            ["dictation_desc"] = "The language the microphone listens for in the plan box and the assistant.",
            ["dictation_label"] = "Dictation language",
            ["dictation_saved"] = "Dictation language saved.",
            ["account_title"] = "Account & sync",
            ["account_desc"] = "Sign in to keep your projects in sync across your devices.",
            ["password_label"] = "Password",
            ["password_hint"] = "New here? Choose a password (at least 6 characters) and tap Create account.",
            ["sign_in"] = "Sign in",
            ["create_account"] = "Create account",
            ["error_wrong_password"] = "Wrong email or password.",
            ["error_account_exists"] = "An account with this email already exists. Tap Sign in instead.",
            ["error_weak_password"] = "Choose a password of at least 6 characters.",
            ["email_label"] = "Email",
            ["signed_in_label"] = "Signed in as",
            ["signed_in_as"] = "Signed in as {0}.",
            ["sign_in_failed"] = "Sign-in failed: {0}",
            ["syncing"] = "Syncing…",
            ["last_synced"] = "Synced at {0}.",
            ["not_synced_yet"] = "Not synced yet.",
            ["sync_now"] = "Sync now",
            ["sign_out"] = "Sign out",
            ["confirm_sign_out"] = "Sign out? Your projects stay in your account and are removed from this device.",
            ["confirm_sign_out_unsynced"] = "Some changes couldn't be uploaded and will be lost. Sign out anyway?",
            ["signed_out"] = "Signed out.",
            ["today_empty"] = "Nothing planned for today. Add a task in the box below.",
            ["today_all_done"] = "Today is done 🎉",
            ["open_day"] = "Open day",
            ["undo"] = "Undo",
            ["task_done_toast"] = "Done: {0}",
            ["task_deleted"] = "Task deleted",
            ["project_deleted"] = "Project deleted",
            ["moved_to_tomorrow"] = "Moved to tomorrow",
            ["moved_to_next_day"] = "Moved to the next day",
            ["moved_to_today"] = "Moved {0} to today",
            ["carryover_title"] = "{0} left unfinished on earlier days.",
            ["carryover_move"] = "Move to today",
            ["carryover_dismiss"] = "Not now",
            ["swipe_done"] = "Done",
            ["swipe_reopen"] = "Reopen",
            ["swipe_tomorrow"] = "Tomorrow",
            ["swipe_next_day"] = "Next day",
            ["sync_ok"] = "Synced",
            ["sync_problem"] = "Sync problem",
            ["projects_heading"] = "Projects",
            ["change_date"] = "Change date",
            ["set_date"] = "Set date",
            ["moved_to_day"] = "Moved to {0}",
            ["date_changed"] = "Date changed",
            ["drag_to_reorder"] = "Drag to reorder",
            ["week_strip"] = "Next 7 days",
            ["day_empty"] = "Nothing planned for this day yet. Add the first task below.",
            ["day_invalid"] = "That date isn't valid.",
            ["forgot_password"] = "Forgot password?",
            ["reset_sent"] = "If there's an account for {0}, we've emailed a link to choose a new password.",
            ["choose_new_password"] = "Choose a new password for your account.",
            ["new_password_label"] = "New password",
            ["save_password"] = "Save password",
            ["password_saved"] = "Password saved.",
            ["change_password"] = "Change password",
            ["backup_title"] = "Backup",
            ["backup_desc"] = "Save all your projects to a file, or bring them back from one.",
            ["backup_export"] = "Export",
            ["backup_import"] = "Import",
            ["backup_invalid"] = "That file isn't an NxtTask backup.",
            ["backup_confirm"] = "Import {0} projects from this file? Projects that already exist are replaced by the file's version.",
            ["backup_imported"] = "Imported {0} projects",
            ["onboarding_title"] = "Welcome to NxtTask",
            ["onboarding_plan"] = "Type a plan or a single task in the box below. Choose Task to add it as is, or Project to have it split into steps.",
            ["onboarding_swipe"] = "Swipe a task right to finish it, or left to move it to the next day.",
            ["onboarding_sync"] = "Sign in under Settings to keep your phone and computer in sync.",
            ["onboarding_done"] = "Got it",
            ["repeat"] = "Repeat…",
            ["repeat_days"] = "Repeat on",
            ["every_day"] = "Every day",
            ["repeat_save"] = "Repeat",
            ["repeat_saved"] = "Repeats: {0}",
            ["stop_repeating"] = "Stop repeating",
            ["routine_stopped"] = "“{0}” no longer repeats",
            ["routines_title"] = "Repeating tasks",
            ["routines_desc"] = "Tasks added to your day automatically on the days you choose.",
            ["routines_empty"] = "None yet. Open a task's menu and choose Repeat to make it come back on chosen days.",
            ["list_separator"] = ", ",
            ["delete_account"] = "Delete account",
            ["confirm_delete_account"] = "Delete your account and every project in it, on all your devices? This can't be undone. Export a backup first if you might want them later.",
            ["delete_account_unavailable"] = "Account deletion isn't set up on the server yet.",
            ["account_deleted"] = "Your account and its data were deleted."
        },
        ["ar"] = new()
        {
            ["back"] = "رجوع",
            ["save"] = "حفظ",
            ["edit"] = "تعديل",
            ["delete"] = "حذف",
            ["add"] = "إضافة",
            ["cancel"] = "إلغاء",
            ["more_options"] = "خيارات أخرى",
            ["mark_done"] = "تحديد كمنجزة",
            ["today"] = "اليوم",
            ["tomorrow"] = "غدًا",
            ["date"] = "التاريخ",
            ["deadline"] = "الموعد النهائي",
            ["overdue"] = "متأخر · {0}",
            ["task_progress"] = "المهام {0}/{1}",
            ["not_found"] = "الصفحة غير موجودة.",

            ["assistant"] = "المساعد",
            ["archive"] = "الأرشيف",
            ["settings"] = "الإعدادات",
            ["search_placeholder"] = "ابحث في الأيام والمهام",
            ["clear_search"] = "مسح البحث",
            ["loading_projects"] = "جارٍ تحميل المشاريع",
            ["empty_art"] = "رسم لمصباح وقائمة مهام فارغة",
            ["no_projects"] = "لا توجد مشاريع بعد",
            ["no_projects_hint"] = "اكتب خطتك في المربع بالأسفل وسنحوّلها إلى قائمة مهام.",
            ["no_matches"] = "لا توجد نتائج لـ «{0}».",
            ["pin"] = "تثبيت المشروع",
            ["unpin"] = "إلغاء التثبيت",
            ["delete_project"] = "حذف المشروع",
            ["confirm_delete_project"] = "هل تريد حذف هذا المشروع وكل مهامه؟",

            ["loading_project"] = "جارٍ تحميل المشروع",
            ["edit_project_name"] = "تعديل اسم المشروع",
            ["done_of"] = "أُنجز {0} من {1}",
            ["generated_with"] = "أُنشئت المهام باستخدام {0}.",
            ["all_done"] = "أُنجزت كل المهام",
            ["completed"] = "المنجزة ({0})",
            ["move_next_day"] = "نقل إلى اليوم التالي",
            ["duplicate_next_day"] = "نسخ إلى اليوم التالي",
            ["add_subtask"] = "إضافة مهمة فرعية",
            ["add_subtask_placeholder"] = "أضف مهمة فرعية",
            ["add_task_placeholder"] = "أضف مهمة",

            ["loading_archive"] = "جارٍ تحميل المشاريع المؤرشفة",
            ["no_archived"] = "لا توجد مشاريع مؤرشفة",
            ["no_archived_hint"] = "تظهر المشاريع المكتملة هنا تلقائيًا.",
            ["no_tasks"] = "لا توجد مهام.",
            ["view_tasks"] = "عرض المهام",
            ["hide_tasks"] = "إخفاء المهام",
            ["unarchive"] = "إلغاء الأرشفة",

            ["plan_placeholder"] = "اكتب خطتك...",
            ["submit"] = "إرسال",
            ["when_title"] = "متى ستنفّذ هذه الخطة؟",
            ["plan_length"] = "مدة الخطة",
            ["one_day"] = "يوم واحد",
            ["multiple_days"] = "عدة أيام",
            ["in_3_days"] = "بعد 3 أيام",
            ["in_a_week"] = "بعد أسبوع",
            ["in_2_weeks"] = "بعد أسبوعين",
            ["deadline_hint"] = "مشروع لكل يوم من اليوم حتى الموعد النهائي (حتى {0} يومًا)، مع توزيع مهامك عليها.",
            ["create"] = "إنشاء",
            ["planning"] = "جارٍ التخطيط...",
            ["pick_date"] = "اختر تاريخًا.",
            ["pick_future"] = "اختر اليوم أو تاريخًا لاحقًا.",
            ["pick_deadline"] = "اختر موعدًا نهائيًا بين الغد و{0} يومًا من الآن.",
            ["added_to_days"] = "أُضيفت المهام إلى {0}.",
            ["entry_kind"] = "النوع",
            ["kind_task"] = "مهمة",
            ["kind_project"] = "مشروع",
            ["task_hint"] = "تُضاف كما هي دون تقسيم.",
            ["added_tasks_to_day"] = "أُضيفت {0} إلى {1}.",

            ["assistant_subtitle"] = "تحدّث عن أسبوعك، ثم أضف ما يناسبك",
            ["assistant_empty"] = "أخبرني بما لديك. سأسألك سؤالًا أو اثنين، ثم أقترح مهامًا لأسبوعك وأضيفها عندما توافق.",
            ["assistant_placeholder"] = "ما الذي لديك هذا الأسبوع؟",
            ["suggestion_1"] = "عندي امتحان يوم الخميس",
            ["suggestion_2"] = "أسبوع عمل مزدحم، لكني أريد ممارسة الرياضة",
            ["suggestion_3"] = "ساعدني في تخطيط هذا الأسبوع",
            ["added_summary"] = "أُضيفت {0} إلى {1}.",
            ["suggest_default"] = "هذا ما أقترحه. هل أضيفه إلى خطتك؟",
            ["open_my_days"] = "افتح أيامي",

            ["dictation_title"] = "الإملاء الصوتي",
            ["dictation_desc"] = "اللغة التي يستمع إليها الميكروفون في مربع الخطة وفي المساعد.",
            ["dictation_label"] = "لغة الإملاء",
            ["dictation_saved"] = "تم حفظ لغة الإملاء.",
            ["account_title"] = "الحساب والمزامنة",
            ["account_desc"] = "سجّل الدخول لمزامنة مشاريعك بين أجهزتك.",
            ["password_label"] = "كلمة المرور",
            ["password_hint"] = "جديد هنا؟ اختر كلمة مرور (٦ أحرف على الأقل) واضغط إنشاء حساب.",
            ["sign_in"] = "تسجيل الدخول",
            ["create_account"] = "إنشاء حساب",
            ["error_wrong_password"] = "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
            ["error_account_exists"] = "يوجد حساب بهذا البريد بالفعل. اضغط تسجيل الدخول.",
            ["error_weak_password"] = "اختر كلمة مرور من ٦ أحرف على الأقل.",
            ["email_label"] = "البريد الإلكتروني",
            ["signed_in_label"] = "مسجّل الدخول باسم",
            ["signed_in_as"] = "تم تسجيل الدخول باسم {0}.",
            ["sign_in_failed"] = "تعذّر تسجيل الدخول: {0}",
            ["syncing"] = "جارٍ المزامنة…",
            ["last_synced"] = "تمت المزامنة الساعة {0}.",
            ["not_synced_yet"] = "لم تتم المزامنة بعد.",
            ["sync_now"] = "زامن الآن",
            ["sign_out"] = "تسجيل الخروج",
            ["confirm_sign_out"] = "تسجيل الخروج؟ تبقى مشاريعك في حسابك وتُزال من هذا الجهاز.",
            ["confirm_sign_out_unsynced"] = "تعذّر رفع بعض التغييرات وستُفقد. هل تريد تسجيل الخروج على أي حال؟",
            ["signed_out"] = "تم تسجيل الخروج.",
            ["today_empty"] = "لا شيء مخطط لليوم. أضف مهمة من المربع بالأسفل.",
            ["today_all_done"] = "أنجزت مهام اليوم 🎉",
            ["open_day"] = "فتح اليوم",
            ["undo"] = "تراجع",
            ["task_done_toast"] = "تم: {0}",
            ["task_deleted"] = "حُذفت المهمة",
            ["project_deleted"] = "حُذف المشروع",
            ["moved_to_tomorrow"] = "نُقلت إلى الغد",
            ["moved_to_next_day"] = "نُقلت إلى اليوم التالي",
            ["moved_to_today"] = "نُقلت {0} إلى اليوم",
            ["carryover_title"] = "بقيت {0} دون إنجاز من أيام سابقة.",
            ["carryover_move"] = "نقل إلى اليوم",
            ["carryover_dismiss"] = "ليس الآن",
            ["swipe_done"] = "تم",
            ["swipe_reopen"] = "إعادة فتح",
            ["swipe_tomorrow"] = "غدًا",
            ["swipe_next_day"] = "اليوم التالي",
            ["sync_ok"] = "تمت المزامنة",
            ["sync_problem"] = "مشكلة في المزامنة",
            ["projects_heading"] = "المشاريع",
            ["change_date"] = "تغيير التاريخ",
            ["set_date"] = "تحديد تاريخ",
            ["moved_to_day"] = "نُقلت إلى {0}",
            ["date_changed"] = "تم تغيير التاريخ",
            ["drag_to_reorder"] = "اسحب لإعادة الترتيب",
            ["week_strip"] = "الأيام السبعة القادمة",
            ["day_empty"] = "لا شيء مخطط لهذا اليوم بعد. أضف أول مهمة بالأسفل.",
            ["day_invalid"] = "هذا التاريخ غير صالح.",
            ["forgot_password"] = "نسيت كلمة المرور؟",
            ["reset_sent"] = "إذا كان هناك حساب للبريد {0} فقد أرسلنا رابطًا لاختيار كلمة مرور جديدة.",
            ["choose_new_password"] = "اختر كلمة مرور جديدة لحسابك.",
            ["new_password_label"] = "كلمة المرور الجديدة",
            ["save_password"] = "حفظ كلمة المرور",
            ["password_saved"] = "تم حفظ كلمة المرور.",
            ["change_password"] = "تغيير كلمة المرور",
            ["backup_title"] = "النسخ الاحتياطي",
            ["backup_desc"] = "احفظ كل مشاريعك في ملف، أو استرجعها منه.",
            ["backup_export"] = "تصدير",
            ["backup_import"] = "استيراد",
            ["backup_invalid"] = "هذا الملف ليس نسخة احتياطية من NxtTask.",
            ["backup_confirm"] = "استيراد {0} من المشاريع من هذا الملف؟ تُستبدل المشاريع الموجودة بنسختها في الملف.",
            ["backup_imported"] = "تم استيراد {0} من المشاريع",
            ["onboarding_title"] = "أهلًا بك في NxtTask",
            ["onboarding_plan"] = "اكتب خطة أو مهمة واحدة في المربع بالأسفل. اختر «مهمة» لإضافتها كما هي، أو «مشروع» لتقسيمها إلى خطوات.",
            ["onboarding_swipe"] = "اسحب المهمة يمينًا لإنجازها، أو يسارًا لنقلها إلى اليوم التالي.",
            ["onboarding_sync"] = "سجّل الدخول من الإعدادات لمزامنة هاتفك وحاسوبك.",
            ["onboarding_done"] = "فهمت",
            ["repeat"] = "تكرار…",
            ["repeat_days"] = "التكرار في",
            ["every_day"] = "كل يوم",
            ["repeat_save"] = "تكرار",
            ["repeat_saved"] = "تتكرر: {0}",
            ["stop_repeating"] = "إيقاف التكرار",
            ["routine_stopped"] = "لم تعد «{0}» تتكرر",
            ["routines_title"] = "المهام المتكررة",
            ["routines_desc"] = "مهام تُضاف إلى يومك تلقائيًا في الأيام التي تختارها.",
            ["routines_empty"] = "لا شيء بعد. افتح قائمة مهمة واختر «تكرار» لتعود في أيام تختارها.",
            ["list_separator"] = "، ",
            ["delete_account"] = "حذف الحساب",
            ["confirm_delete_account"] = "حذف حسابك وكل مشاريعه على جميع أجهزتك؟ لا يمكن التراجع عن ذلك. صدّر نسخة احتياطية أولًا إن كنت قد تحتاجها لاحقًا.",
            ["delete_account_unavailable"] = "حذف الحساب غير مُعدّ على الخادم بعد.",
            ["account_deleted"] = "حُذف حسابك وبياناته."
        }
    };
}
