using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdeaSplit.Shared.Services;

public class GeminiService
{
    private readonly HttpClient _http;
    private readonly SettingsService _settings;

    public GeminiService(HttpClient http, SettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<List<string>> BreakDownIdeaAsync(string idea)
    {
        var apiKey = await _settings.GetGeminiApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key set. Add one in Settings.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";

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
                    properties = new
                    {
                        tasks = new { type = "ARRAY", items = new { type = "STRING" } }
                    },
                    required = new[] { "tasks" }
                }
            }
        };

        var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini returned no content.");

        var parsed = JsonSerializer.Deserialize<TaskListResult>(text);
        return parsed?.Tasks ?? new List<string>();
    }

    private class TaskListResult
    {
        [JsonPropertyName("tasks")]
        public List<string> Tasks { get; set; } = new();
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")]
        public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonPropertyName("parts")]
        public List<Part>? Parts { get; set; }
    }

    private class Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
