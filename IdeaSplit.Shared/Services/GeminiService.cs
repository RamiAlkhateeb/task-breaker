using System.Text.Json.Serialization;
using Nxt.UI.Ai;

namespace IdeaSplit.Shared.Services;

public sealed record IdeaBreakdownResult(List<string> Tasks);

/// <summary>A day of work proposed by the assistant.</summary>
public sealed record PlannedDay(DateTime Date, List<string> Tasks);

/// <summary>What the chat assistant proposes: an optional short project name and a dated plan.</summary>
public sealed record ChatPlan(string? Title, List<PlannedDay> Days);

/// <summary>
/// NxtTask's prompts. Transport, model fallback and structured JSON come from the family's shared
/// <see cref="GeminiClient"/>.
/// </summary>
public class GeminiService
{
    private const int MaxPlanDays = 30;
    private readonly GeminiClient _gemini;

    public GeminiService(GeminiClient gemini) => _gemini = gemini;

    public async Task<IdeaBreakdownResult> BreakDownIdeaAsync(string idea)
    {
        var prompt = $"""
            Break the following idea into a short, ordered list of clear, actionable tasks
            a single person could check off one by one. Return 4 to 10 tasks.
            Write the tasks in the same language as the idea.

            Idea: {idea}
            """;
        var schema = new
        {
            type = "OBJECT",
            properties = new { tasks = new { type = "ARRAY", items = new { type = "STRING" } } },
            required = new[] { "tasks" }
        };

        var result = await _gemini.GenerateJsonAsync<TaskListResult>(prompt, schema, parsed =>
            parsed.Tasks is { Count: > 0 } ? parsed : throw new InvalidOperationException("Gemini returned an invalid task list."));
        return new IdeaBreakdownResult(result.Tasks);
    }

    public async Task<string> GenerateTitleAsync(string idea)
    {
        var prompt = $"""
            Give a concise 2 to 3 word title that summarizes this idea, in the same language as the idea.
            No punctuation besides spaces.

            Idea: {idea}
            """;
        var schema = new
        {
            type = "OBJECT",
            properties = new { title = new { type = "STRING" } },
            required = new[] { "title" }
        };

        var result = await _gemini.GenerateJsonAsync<TitleResult>(prompt, schema, parsed =>
            string.IsNullOrWhiteSpace(parsed.Title) ? throw new InvalidOperationException("Gemini returned an empty title.") : parsed);
        return result.Title.Trim();
    }

    public async Task<List<List<string>>> PlanDaysAsync(string idea, int dayCount)
    {
        var prompt = $"""
            Plan the following idea across exactly {dayCount} consecutive days. Order the work so earlier
            days lay the groundwork for later ones. Give 1 to 5 small, clear, actionable tasks per day;
            a day may be empty only if truly needed. Return exactly {dayCount} days.
            Write the tasks in the same language as the idea.

            Idea: {idea}
            """;
        var schema = new
        {
            type = "OBJECT",
            properties = new
            {
                days = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new { tasks = new { type = "ARRAY", items = new { type = "STRING" } } },
                        required = new[] { "tasks" }
                    }
                }
            },
            required = new[] { "days" }
        };

        var parsed = await _gemini.GenerateJsonAsync<DayPlanResult>(prompt, schema, result =>
            result.Days?.Any(day => day.Tasks.Any(task => !string.IsNullOrWhiteSpace(task))) == true
                ? result
                : throw new InvalidOperationException("Gemini returned an empty plan."));

        var days = parsed.Days!.Select(day => day.Tasks.Where(task => !string.IsNullOrWhiteSpace(task)).ToList()).ToList();
        return days.Count == dayCount ? days : SplitEvenly(days.SelectMany(day => day).ToList(), dayCount);
    }

    /// <summary>
    /// The chat assistant: the user describes what they want done and when, in their own words
    /// ("prepare for the exam on Friday", "clean the flat tomorrow"). The model picks the days.
    /// </summary>
    public async Task<ChatPlan> PlanFromChatAsync(string request)
    {
        var today = DateTime.Today;
        var prompt = $"""
            Today's date is {today:yyyy-MM-dd} ({today.DayOfWeek}). The user describes something they want to get done.
            Turn it into a plan of small, clear, actionable tasks spread over the right days.
            - Resolve relative dates ("tomorrow", "Friday", "next week", "by the 20th") against today and return
              explicit ISO dates (yyyy-MM-dd). If no timing is given, put everything on today.
            - Never use a date before today or more than {MaxPlanDays} days ahead.
            - 1 to 6 tasks per day, ordered so earlier days lay the groundwork for later ones.
            - "title": a 2 to 3 word name for the whole plan.
            Write the title and tasks in the same language as the user's message.

            User message: {request}
            """;
        var schema = new
        {
            type = "OBJECT",
            properties = new
            {
                title = new { type = "STRING" },
                days = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            date = new { type = "STRING" },
                            tasks = new { type = "ARRAY", items = new { type = "STRING" } }
                        },
                        required = new[] { "date", "tasks" }
                    }
                }
            },
            required = new[] { "title", "days" }
        };

        ChatPlan? plan = null;
        await _gemini.GenerateJsonAsync<ChatPlanResult>(prompt, schema, result =>
        {
            var days = (result.Days ?? [])
                .Select(day => (Ok: DateTime.TryParse(day.Date, out var date), Date: date.Date,
                    Tasks: day.Tasks.Select(t => t.Trim()).Where(t => t.Length > 0).ToList()))
                .Where(day => day.Ok && day.Tasks.Count > 0 && day.Date >= today && day.Date <= today.AddDays(MaxPlanDays))
                .GroupBy(day => day.Date)
                .OrderBy(group => group.Key)
                .Select(group => new PlannedDay(group.Key, group.SelectMany(day => day.Tasks).ToList()))
                .ToList();
            if (days.Count == 0) throw new InvalidOperationException("Gemini couldn't turn that into tasks. Try adding a little more detail.");
            plan = new ChatPlan(string.IsNullOrWhiteSpace(result.Title) ? null : result.Title.Trim(), days);
            return result;
        });
        return plan!;
    }

    private static List<List<string>> SplitEvenly(List<string> tasks, int dayCount)
    {
        var perDay = (int)Math.Ceiling(tasks.Count / (double)dayCount);
        return Enumerable.Range(0, dayCount)
            .Select(index => tasks.Skip(index * perDay).Take(perDay).ToList())
            .ToList();
    }

    private sealed class TaskListResult { [JsonPropertyName("tasks")] public List<string> Tasks { get; set; } = []; }
    private sealed class DayPlanResult { [JsonPropertyName("days")] public List<DayPlan>? Days { get; set; } }
    private sealed class DayPlan { [JsonPropertyName("tasks")] public List<string> Tasks { get; set; } = []; }
    private sealed class TitleResult { [JsonPropertyName("title")] public string Title { get; set; } = ""; }
    private sealed class ChatPlanResult
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("days")] public List<ChatDay>? Days { get; set; }
    }
    private sealed class ChatDay
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("tasks")] public List<string> Tasks { get; set; } = [];
    }
}
