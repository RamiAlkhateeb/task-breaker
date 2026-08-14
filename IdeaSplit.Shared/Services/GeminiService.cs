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

    public async Task<List<string>> GetBookChaptersAsync(string bookTitle)
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key set. Add one in Settings.");

        foreach (var model in await GetModelsToTryAsync())
        {
            try
            {
                return await GenerateBookChaptersAsync(bookTitle, model, apiKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Gemini model {Model} could not retrieve chapters for {BookTitle}.", model, bookTitle);
            }
        }

        return [];
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

    private async Task<List<string>> GenerateBookChaptersAsync(string bookTitle, string model, string apiKey)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var prompt = $"""
            Give the actual table of contents or chapter titles for this specific book: "{bookTitle}".
            Only return chapters you reliably know are real. Never invent chapter names. If you do not
            have reliable knowledge of this exact book, return an empty chapters array.
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
                    properties = new { chapters = new { type = "ARRAY", items = new { type = "STRING" } } },
                    required = new[] { "chapters" }
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini returned no content.");
        return JsonSerializer.Deserialize<ChapterListResult>(text)?.Chapters ?? [];
    }

    private sealed class ModelListResponse { [JsonPropertyName("models")] public List<GeminiModel>? Models { get; set; } }
    private sealed class GeminiModel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("supportedGenerationMethods")] public List<string>? SupportedGenerationMethods { get; set; }
    }
    private sealed class TaskListResult { [JsonPropertyName("tasks")] public List<string> Tasks { get; set; } = []; }
    private sealed class ChapterListResult { [JsonPropertyName("chapters")] public List<string> Chapters { get; set; } = []; }
    private sealed class GeminiResponse { [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; } }
    private sealed class Candidate { [JsonPropertyName("content")] public Content? Content { get; set; } }
    private sealed class Content { [JsonPropertyName("parts")] public List<Part>? Parts { get; set; } }
    private sealed class Part { [JsonPropertyName("text")] public string? Text { get; set; } }
}
