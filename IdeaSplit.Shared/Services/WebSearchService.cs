using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace IdeaSplit.Shared.Services;

public class WebSearchService
{
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly ILogger<WebSearchService> _logger;

    public WebSearchService(HttpClient http, SettingsService settings, ILogger<WebSearchService> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<string>> SearchBookTableOfContentsAsync(string bookTitle)
    {
        // Requires a Bing Web Search API key saved in Settings. Google Custom Search or SerpAPI
        // can be substituted here using a key stored through SettingsService in the same manner.
        var apiKey = await _settings.GetWebSearchApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        try
        {
            var query = Uri.EscapeDataString($"{bookTitle} table of contents chapters");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.bing.microsoft.com/v7.0/search?q={query}&count=5");
            request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return [];

            var search = await response.Content.ReadFromJsonAsync<BingSearchResponse>();
            foreach (var result in search?.WebPages?.Value ?? [])
            {
                var chapters = ExtractChapters(result.Snippet);
                if (chapters.Count >= 2) return chapters;

                if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var pageUrl)) continue;
                try
                {
                    var html = await _http.GetStringAsync(pageUrl);
                    chapters = ExtractChapters(html);
                    if (chapters.Count >= 2) return chapters;
                }
                catch (HttpRequestException) { }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Book table-of-contents search failed for {BookTitle}.", bookTitle);
        }

        return [];
    }

    private static List<string> ExtractChapters(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return [];
        var text = WebUtility.HtmlDecode(Regex.Replace(source, "<[^>]+>", "\n"));
        return Regex.Matches(text,
                @"\b(?:chapter\s+(?:\d+|[ivxlcdm]+)\b[^\r\n]{0,100}|(?:prologue|epilogue|introduction|conclusion)\b[^\r\n]{0,100})",
                RegexOptions.IgnoreCase)
            .Select(match => Regex.Replace(match.Value, "\\s+", " ").Trim(' ', '.', ':', '-'))
            .Where(chapter => chapter.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class BingSearchResponse { public BingWebPages? WebPages { get; set; } }
    private sealed class BingWebPages { public List<BingResult>? Value { get; set; } }
    private sealed class BingResult { public string? Url { get; set; } public string? Snippet { get; set; } }
}
