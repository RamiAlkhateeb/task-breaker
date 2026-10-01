using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace IdeaSplit.Shared.Services;

public sealed record IdeaBreakdownResult(List<string> Tasks, string Model);

public class GeminiService
{
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

    public async Task<IdeaBreakdownResult> BreakDownIdeaAsync(string idea)
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key set. Add one in Settings.");

        var modelsToTry = await GetModelsToTryAsync();
        Exception? lastError = null;

        foreach (var model in modelsToTry)
        {
            try
            {
                var tasks = await GenerateTasksAsync(idea, model, apiKey);
                return new IdeaBreakdownResult(tasks, model);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Gemini model {Model} failed; trying the next fallback.", model);
            }
        }

        throw new InvalidOperationException(
            "None of the configured Gemini models could generate tasks. Check your API key, model access, and connection.",
            lastError);
    }

    public async Task<string> GenerateTitleAsync(string idea)
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key set. Add one in Settings.");

        var modelsToTry = await GetModelsToTryAsync();
        Exception? lastError = null;

        foreach (var model in modelsToTry)
        {
            try
            {
                return await GenerateTitleCoreAsync(idea, model, apiKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Gemini model {Model} failed to generate a title; trying the next fallback.", model);
            }
        }

        throw new InvalidOperationException("None of the configured Gemini models could generate a title.", lastError);
    }

    public async Task<List<List<string>>> PlanDaysAsync(string idea, int dayCount)
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key set. Add one in Settings.");

        var modelsToTry = await GetModelsToTryAsync();
        Exception? lastError = null;

        foreach (var model in modelsToTry)
        {
            try
            {
                return await PlanDaysCoreAsync(idea, model, apiKey, dayCount);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Gemini model {Model} failed to plan days; trying the next fallback.", model);
            }
        }

        throw new InvalidOperationException(
            "None of the configured Gemini models could plan this. Check your API key, model access, and connection.",
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

    private async Task<List<string>> GenerateTasksAsync(string idea, string model, string apiKey)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var prompt = $"""
            Break the following idea into a short, ordered list of clear, actionable tasks
            a single person could check off one by one. Return 4 to 10 tasks.

            Idea: {idea}
            """;
        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new { tasks = new { type = "ARRAY", items = new { type = "STRING" } } },
                    required = new[] { "tasks" }
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini returned no content.");
        var parsed = JsonSerializer.Deserialize<TaskListResult>(text);
        if (parsed?.Tasks is not { Count: > 0 })
            throw new InvalidOperationException("Gemini returned an invalid task list.");

        return parsed.Tasks;
    }

    private async Task<string> GenerateTitleCoreAsync(string idea, string model, string apiKey)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var prompt = $"""
            Give a concise 2 to 3 word title that summarizes this idea. No punctuation besides spaces.

            Idea: {idea}
            """;
        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new { title = new { type = "STRING" } },
                    required = new[] { "title" }
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini returned no content.");
        var title = JsonSerializer.Deserialize<TitleResult>(text)?.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Gemini returned an empty title.");

        return title;
    }

    private async Task<List<List<string>>> PlanDaysCoreAsync(string idea, string model, string apiKey, int dayCount)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var prompt = $"""
            Plan the following idea across exactly {dayCount} consecutive days. Order the work so earlier
            days lay the groundwork for later ones. Give 1 to 5 small, clear, actionable tasks per day;
            a day may be empty only if truly needed. Return exactly {dayCount} days.

            Idea: {idea}
            """;
        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
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
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini returned no content.");
        var parsed = JsonSerializer.Deserialize<DayPlanResult>(text);
        var days = parsed?.Days?.Select(day => day.Tasks.Where(task => !string.IsNullOrWhiteSpace(task)).ToList()).ToList() ?? [];
        var allTasks = days.SelectMany(day => day).ToList();
        if (allTasks.Count == 0)
            throw new InvalidOperationException("Gemini returned an empty plan.");

        return days.Count == dayCount ? days : SplitEvenly(allTasks, dayCount);
    }

    private static List<List<string>> SplitEvenly(List<string> tasks, int dayCount)
    {
        var perDay = (int)Math.Ceiling(tasks.Count / (double)dayCount);
        return Enumerable.Range(0, dayCount)
            .Select(index => tasks.Skip(index * perDay).Take(perDay).ToList())
            .ToList();
    }

    private sealed class ModelListResponse { [JsonPropertyName("models")] public List<GeminiModel>? Models { get; set; } }
    private sealed class GeminiModel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("supportedGenerationMethods")] public List<string>? SupportedGenerationMethods { get; set; }
    }
    private sealed class TaskListResult { [JsonPropertyName("tasks")] public List<string> Tasks { get; set; } = []; }
    private sealed class DayPlanResult { [JsonPropertyName("days")] public List<DayPlan>? Days { get; set; } }
    private sealed class DayPlan { [JsonPropertyName("tasks")] public List<string> Tasks { get; set; } = []; }
    private sealed class TitleResult { [JsonPropertyName("title")] public string Title { get; set; } = ""; }
    private sealed class GeminiResponse { [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; } }
    private sealed class Candidate { [JsonPropertyName("content")] public Content? Content { get; set; } }
    private sealed class Content { [JsonPropertyName("parts")] public List<Part>? Parts { get; set; } }
    private sealed class Part { [JsonPropertyName("text")] public string? Text { get; set; } }
}
