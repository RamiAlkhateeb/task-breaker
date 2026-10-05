using System.Text.Json.Serialization;
using Nxt.UI.Ai;

namespace IdeaSplit.Shared.Services;

public sealed record IdeaBreakdownResult(List<string> Tasks);

/// <summary>One message in the assistant conversation, as plain text (proposals included).</summary>
public sealed record AssistantTurn(bool FromUser, string Text);

/// <summary>A task the assistant suggests for a given day.</summary>
public sealed record SuggestedTask(string Title, DateTime Date, string? ProjectName);

/// <summary>What the assistant wants to do with its reply.</summary>
public enum AssistantAction
{
    /// <summary>Still talking: a question or an answer, nothing proposed.</summary>
    Discuss,
    /// <summary>Suggesting tasks and asking whether to add them.</summary>
    Propose,
    /// <summary>The user agreed; add the tasks.</summary>
    Add
}

public sealed record AssistantResult(string Reply, AssistantAction Action, List<SuggestedTask> Tasks);

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
    /// The planning assistant. It talks first (a question or two when something important is unclear),
    /// then proposes dated tasks and asks before adding. Tasks are only returned with
    /// <see cref="AssistantAction.Add"/> after the user has agreed to a proposal.
    /// </summary>
    public async Task<AssistantResult> ConverseAsync(IReadOnlyList<AssistantTurn> conversation, string existingPlan)
    {
        var today = DateTime.Today;
        var transcript = string.Join("\n\n", conversation.Select(turn => $"{(turn.FromUser ? "User" : "Assistant")}: {turn.Text}"));
        var prompt = $"""
            You are the planning assistant inside NxtTask, a to-do app. Today is {today:yyyy-MM-dd} ({today.DayOfWeek}).
            The user tells you what they have going on. Behave like a thoughtful friend helping them plan the week:

            1. Discuss first. If something important is unclear (a deadline, how much time they have, which days are
               busy), ask one or two short questions. Don't ask about things you can reasonably assume and don't drag it out.
               Use action "discuss" and an empty task list.
            2. Recommend. When you understand enough, or they ask, suggest small, clear, actionable tasks with dates from
               {today:yyyy-MM-dd} to {today.AddDays(6):yyyy-MM-dd} (only later, up to {today.AddDays(MaxPlanDays):yyyy-MM-dd},
               if they mention a later deadline). Balance the load using their existing plan below and don't duplicate
               tasks already planned. Use action "propose", put every suggested task in "tasks", and end the reply by
               asking whether to add them to their plan. Don't list the tasks in the reply text; the app shows them.
            3. Add only with consent. Use action "add" only when the user clearly agrees to your latest proposal
               (e.g. "yes", "add them", "go ahead", or "add all except the gym"). Then "tasks" must be exactly the tasks
               to add, applying any changes they asked for. If they ask for changes without agreeing, "propose" again
               with the full revised list.

            For tasks that serve one larger goal across different days, set "project" to a 2 to 3 word name for that
            goal; otherwise leave it empty. Keep "reply" short and friendly. Write everything in the user's language.

            Existing plan:
            {existingPlan}

            Conversation so far:
            {transcript}
            """;
        var schema = new
        {
            type = "OBJECT",
            properties = new
            {
                reply = new { type = "STRING" },
                action = new { type = "STRING", @enum = new[] { "discuss", "propose", "add" } },
                tasks = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            title = new { type = "STRING" },
                            date = new { type = "STRING" },
                            project = new { type = "STRING" }
                        },
                        required = new[] { "title", "date" }
                    }
                }
            },
            required = new[] { "reply", "action", "tasks" }
        };

        AssistantResult? answer = null;
        await _gemini.GenerateJsonAsync<ConversationResult>(prompt, schema, result =>
        {
            var tasks = (result.Tasks ?? [])
                .Where(task => !string.IsNullOrWhiteSpace(task.Title))
                .Select(task => new SuggestedTask(
                    task.Title!.Trim(),
                    ClampDate(DateTime.TryParse(task.Date, out var date) ? date.Date : today, today),
                    string.IsNullOrWhiteSpace(task.Project) ? null : task.Project.Trim()))
                .OrderBy(task => task.Date)
                .ToList();
            var action = result.Action?.Trim().ToLowerInvariant() switch
            {
                "add" => AssistantAction.Add,
                "propose" when tasks.Count > 0 => AssistantAction.Propose,
                _ => AssistantAction.Discuss
            };
            if (string.IsNullOrWhiteSpace(result.Reply) && action == AssistantAction.Discuss)
                throw new InvalidOperationException("The assistant returned an empty reply.");
            answer = new AssistantResult(result.Reply?.Trim() ?? "", action, tasks);
            return result;
        });
        return answer!;
    }

    private static DateTime ClampDate(DateTime date, DateTime today) =>
        date < today ? today : date > today.AddDays(MaxPlanDays) ? today.AddDays(MaxPlanDays) : date;

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
    private sealed class ConversationResult
    {
        [JsonPropertyName("reply")] public string? Reply { get; set; }
        [JsonPropertyName("action")] public string? Action { get; set; }
        [JsonPropertyName("tasks")] public List<ConversationTask>? Tasks { get; set; }
    }
    private sealed class ConversationTask
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("project")] public string? Project { get; set; }
    }
}
