using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdeaSplit.Shared.Sync;
using Nxt.UI.Ai;

namespace IdeaSplit.Web.Services;

/// <summary>
/// AI for signed-in users without a Gemini key: the site's own <c>/api/ai</c> function (functions/api/ai.js),
/// which runs Cloudflare Workers AI with a daily limit per user.
/// </summary>
public sealed class WorkersAiFallback : IAiFallback
{
    private readonly SupabaseAuth _auth;
    private readonly HttpClient _http;

    public WorkersAiFallback(SupabaseAuth auth, HttpClient http)
    {
        _auth = auth;
        _http = http;
    }

    public string Name => "Workers AI";

    public bool IsAvailable => _auth.IsConfigured && _auth.IsSignedIn;

    public async Task<string> GenerateJsonAsync(string prompt, object schema)
    {
        var token = await _auth.GetAccessTokenAsync()
            ?? throw new AiFallbackException(AiFallbackErrorKind.SignInRequired, "Not signed in.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ai") { Content = JsonContent.Create(new { prompt, schema }) };
        request.Headers.Authorization = new("Bearer", token);

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request); }
        catch (HttpRequestException ex) { throw new AiFallbackException(AiFallbackErrorKind.Other, "Can't reach the server.", ex); }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new AiFallbackException(AiFallbackErrorKind.SignInRequired, ErrorOf(body));
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new AiFallbackException(AiFallbackErrorKind.LimitReached, ErrorOf(body));
            if (!response.IsSuccessStatusCode) throw new AiFallbackException(AiFallbackErrorKind.Other, $"{(int)response.StatusCode}: {ErrorOf(body)}");

            try
            {
                using var json = JsonDocument.Parse(body);
                return json.RootElement.TryGetProperty("result", out var result)
                    ? result.GetRawText()
                    : throw new AiFallbackException(AiFallbackErrorKind.Other, "No result in the reply.");
            }
            catch (JsonException ex) { throw new AiFallbackException(AiFallbackErrorKind.Other, "Unreadable reply.", ex); }
        }
    }

    private static string ErrorOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) return error.GetString()!;
        }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] : body;
    }
}
