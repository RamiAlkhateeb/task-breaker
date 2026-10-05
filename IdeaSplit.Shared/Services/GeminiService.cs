using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace IdeaSplit.Shared.Services;

/// <summary>One message in the planning conversation. Assistant turns hold the raw JSON the model returned.</summary>
public sealed record ChatTurn(bool FromUser, string Text);

public sealed record ProposedTask(string Title, DateTime Date, string? ProjectName);

public sealed record AssistantReply(string Reply, List<ProposedTask> Tasks, string RawJson);

public class GeminiService
{
    private const int MaxDaysAhead = 30;

    private static readonly string[] FallbackModels =
    [
        "gemini-3.5-flash-lite",
        "gemini-2.0-flash",
        "gemini-2.0-flash-lite",
        "gemini-1.5-flash",
        "gemini-1.5-pro"
    ];

    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(HttpClient http, SettingsService settings, ILogger<GeminiService> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<string>> ListAvailableModelsAsync()
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return [];

            var result = await response.Content.ReadFromJsonAsync<ModelListResponse>();
            return result?.Models?
                .Where(model => model.SupportedGenerationMethods?.Contains("generateContent", StringComparer.OrdinalIgnoreCase) == true)
                .Select(model => model.Name?.Replace("models/", "", StringComparison.OrdinalIgnoreCase))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Unable to retrieve available Gemini models.");
            return [];
        }
    }

    /// <summary>
    /// Continues the planning conversation. The assistant either asks/answers (empty task list)
    /// or proposes dated tasks for the user to review; it never adds anything itself.
    /// </summary>
    public async Task<AssistantReply> ChatAsync(IReadOnlyList<ChatTurn> history, string existingPlan)
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key set. Add one in Settings.");

        Exception? lastError = null;
        foreach (var model in await GetModelsToTryAsync())
        {
            try
            {
                return await ChatCoreAsync(history, existingPlan, model, apiKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Gemini model {Model} failed to reply; trying the next fallback.", model);
            }
        }

        throw new InvalidOperationException(
            "The assistant couldn't reply. Check your API key, model access, and connection.",
            lastError);
    }

    private async Task<IEnumerable<string>> GetModelsToTryAsync()
    {
        var selectedModel = await _settings.GetGeminiModelAsync();
        var availableModels = await ListAvailableModelsAsync();
        var knownFallbacks = availableModels.Count == 0
            ? FallbackModels
            : FallbackModels.Where(model => availableModels.Contains(model, StringComparer.OrdinalIgnoreCase));
        return new[] { selectedModel }.Concat(knownFallbacks).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<AssistantReply> ChatCoreAsync(IReadOnlyList<ChatTurn> history, string existingPlan, string model, string apiKey)
    {
        var today = DateTime.Today;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var system = $"""
            You are the planning assistant inside NxtTask, a to-do app. Today is {today.ToString("dddd yyyy-MM-dd", CultureInfo.InvariantCulture)}.
            The user tells you what they have going on. Have a short, friendly conversation before suggesting anything:
            ask one or two brief questions when something important is unclear (deadlines, how much time they have,
            which days are busy). Don't ask about things you can reasonably assume, and don't drag it out.

            When you understand enough, or the user asks for suggestions, propose concrete, small, actionable tasks in
            "tasks", each with a date (yyyy-MM-dd) from {today:yyyy-MM-dd} to {today.AddDays(6):yyyy-MM-dd}. Only go later,
            up to {today.AddDays(MaxDaysAhead):yyyy-MM-dd}, if the user mentions a later deadline. Use the existing plan below
            to balance the load and don't duplicate tasks that are already planned. When several tasks serve one larger
            goal spread over different days, set "project" to a 2-3 word name for that goal; otherwise leave it empty.

            While you are still discussing, return an empty "tasks" list. The user reviews any proposal and picks what to
            add; nothing is added without their confirmation. If they ask for changes, return the full revised proposal.
            Keep "reply" short (a few sentences), conversational, and in the same language the user writes in.
            Don't repeat the task list inside "reply"; the app shows it separately.

            Existing plan:
            {existingPlan}
            """;

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents = history.Select(turn => new
            {
                role = turn.FromUser ? "user" : "model",
                parts = new[] { new { text = turn.Text } }
            }).ToArray(),
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        reply = new { type = "STRING" },
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
                    required = new[] { "reply", "tasks" }
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini returned no content.");
        var parsed = JsonSerializer.Deserialize<ChatResult>(text)
            ?? throw new InvalidOperationException("Gemini returned an invalid reply.");
        if (string.IsNullOrWhiteSpace(parsed.Reply) && parsed.Tasks is not { Count: > 0 })
            throw new InvalidOperationException("Gemini returned an empty reply.");

        var tasks = new List<ProposedTask>();
        foreach (var task in parsed.Tasks ?? [])
        {
            if (string.IsNullOrWhiteSpace(task.Title)) continue;
            if (!DateTime.TryParseExact(task.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                date = today;
            if (date < today) date = today;
            if (date > today.AddDays(MaxDaysAhead)) date = today.AddDays(MaxDaysAhead);
            tasks.Add(new ProposedTask(task.Title.Trim(), date, string.IsNullOrWhiteSpace(task.Project) ? null : task.Project.Trim()));
        }

        return new AssistantReply(parsed.Reply?.Trim() ?? "", tasks.OrderBy(task => task.Date).ToList(), text);
    }

    private sealed class ModelListResponse { [JsonPropertyName("models")] public List<GeminiModel>? Models { get; set; } }
    private sealed class GeminiModel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("supportedGenerationMethods")] public List<string>? SupportedGenerationMethods { get; set; }
    }
    private sealed class ChatResult
    {
        [JsonPropertyName("reply")] public string? Reply { get; set; }
        [JsonPropertyName("tasks")] public List<ChatTask>? Tasks { get; set; }
    }
    private sealed class ChatTask
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("project")] public string? Project { get; set; }
    }
    private sealed class GeminiResponse { [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; } }
    private sealed class Candidate { [JsonPropertyName("content")] public Content? Content { get; set; } }
    private sealed class Content { [JsonPropertyName("parts")] public List<Part>? Parts { get; set; } }
    private sealed class Part { [JsonPropertyName("text")] public string? Text { get; set; } }
}
