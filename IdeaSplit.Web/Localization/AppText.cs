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
            ["account_desc"] = "Sign in with your email to keep your projects in sync across your devices.",
            ["email_label"] = "Email",
            ["send_sign_in_email"] = "Send sign-in email",
            ["code_sent"] = "We sent a sign-in email to {0}. Enter the code from it, or open its link on this device.",
            ["code_label"] = "Code",
            ["verify"] = "Sign in",
            ["different_email"] = "Use a different email",
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
            ["signed_out"] = "Signed out."
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
            ["account_desc"] = "سجّل الدخول ببريدك الإلكتروني لمزامنة مشاريعك بين أجهزتك.",
            ["email_label"] = "البريد الإلكتروني",
            ["send_sign_in_email"] = "إرسال بريد تسجيل الدخول",
            ["code_sent"] = "أرسلنا بريد تسجيل الدخول إلى {0}. أدخل الرمز الموجود فيه، أو افتح رابطه على هذا الجهاز.",
            ["code_label"] = "الرمز",
            ["verify"] = "تسجيل الدخول",
            ["different_email"] = "استخدام بريد آخر",
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
            ["signed_out"] = "تم تسجيل الخروج."
        }
    };
}
