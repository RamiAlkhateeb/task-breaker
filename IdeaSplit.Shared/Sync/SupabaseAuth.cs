using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blazored.LocalStorage;

namespace IdeaSplit.Shared.Sync;

/// <summary>The public Supabase project values, written into appsettings.json by the deploy workflow.</summary>
public sealed record SupabaseConfig(string Url, string PublishableKey)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(PublishableKey);
    public string BaseUrl => Url.TrimEnd('/');
}

public sealed record AuthSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, Guid UserId, string Email);

/// <summary>Email and password sign-in against Supabase Auth, with the session kept in localStorage.</summary>
public sealed class SupabaseAuth
{
    private const string SessionKey = "nxt.task.session";

    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;
    private readonly ILocalStorageService _storage;
    private Task<string?>? _refreshing;

    public SupabaseAuth(SupabaseConfig config, HttpClient http, ILocalStorageService storage)
    {
        _config = config;
        _http = http;
        _storage = storage;
    }

    public AuthSession? Session { get; private set; }
    public bool IsSignedIn => Session is not null;
    public bool IsConfigured => _config.IsConfigured;

    /// <summary>Raised when the user signs in or out.</summary>
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (!IsConfigured) return;
        try { Session = await _storage.GetItemAsync<AuthSession>(SessionKey); }
        catch (JsonException) { Session = null; }
    }

    /// <summary>Signs in with email and password. Returns an error message, or null on success.</summary>
    public Task<string?> SignInAsync(string email, string password) =>
        StartSessionAsync($"{_config.BaseUrl}/auth/v1/token?grant_type=password", email, password);

    /// <summary>
    /// Creates an account. With email confirmation turned off in Supabase the user is signed in straight away.
    /// Returns an error message, or null on success.
    /// </summary>
    public Task<string?> SignUpAsync(string email, string password) =>
        StartSessionAsync($"{_config.BaseUrl}/auth/v1/signup", email, password);

    private async Task<string?> StartSessionAsync(string url, string email, string password)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, url, new { email = email.Trim(), password }, bearer: null);
            if (!response.IsSuccessStatusCode) return await ErrorMessageAsync(response);
            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
            // Sign-up without a session means the project still requires confirming the email first.
            if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken)) return "Check your email to confirm your account, then sign in.";
            await SetSessionAsync(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresIn);
            return null;
        }
        catch (HttpRequestException) { return NetworkError; }
    }

    /// <summary>
    /// Handles the URL fragment Supabase adds when the user opens the magic link
    /// (<c>#access_token=…&amp;refresh_token=…</c> or <c>#error=…</c>). Returns null if the fragment wasn't ours.
    /// </summary>
    public async Task<RedirectResult?> HandleRedirectAsync(string fragment)
    {
        var values = ParseQuery(fragment.TrimStart('#'));
        if (values.TryGetValue("access_token", out var access) && values.TryGetValue("refresh_token", out var refresh))
        {
            var expiresIn = values.TryGetValue("expires_in", out var seconds) && int.TryParse(seconds, out var parsed) ? parsed : 3600;
            await SetSessionAsync(access, refresh, expiresIn);
            return new RedirectResult(true, null);
        }
        if (values.TryGetValue("error", out var error))
            return new RedirectResult(false, values.GetValueOrDefault("error_description") ?? error);
        return null;
    }

    /// <summary>A valid access token, refreshed when it is about to expire; null when signed out or offline with an expired token.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        var session = Session;
        if (session is null) return null;
        if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return session.AccessToken;
        // Several callers at once share one refresh (refresh tokens are single-use).
        _refreshing ??= RefreshAsync(session);
        try { return await _refreshing; }
        finally { _refreshing = null; }
    }

    public async Task SignOutAsync()
    {
        var token = Session?.AccessToken;
        if (token is not null)
        {
            try { using var _ = await SendAsync(HttpMethod.Post, $"{_config.BaseUrl}/auth/v1/logout", new { }, token); }
            catch (HttpRequestException) { /* Offline: the session still ends on this device. */ }
        }
        Session = null;
        await _storage.RemoveItemAsync(SessionKey);
        Changed?.Invoke();
    }

    /// <summary>Builds a request with the project key and, when signed in, the user's token (used by sync too).</summary>
    internal HttpRequestMessage CreateRequest(HttpMethod method, string url, string? bearer)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("apikey", _config.PublishableKey);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        return request;
    }

    private async Task<string?> RefreshAsync(AuthSession session)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, $"{_config.BaseUrl}/auth/v1/token?grant_type=refresh_token",
                new { refresh_token = session.RefreshToken }, bearer: null);
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                // The refresh token was revoked or already used: the user has to sign in again.
                Session = null;
                await _storage.RemoveItemAsync(SessionKey);
                Changed?.Invoke();
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;
            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokens is null) return null;
            await SetSessionAsync(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresIn, notify: false);
            return tokens.AccessToken;
        }
        catch (HttpRequestException) { return null; }
    }

    private async Task SetSessionAsync(string accessToken, string refreshToken, int expiresIn, bool notify = true)
    {
        var claims = ReadJwtPayload(accessToken);
        if (claims.ValueKind != JsonValueKind.Object) claims = JsonDocument.Parse("{}").RootElement;
        var userId = claims.TryGetProperty("sub", out var sub) && Guid.TryParse(sub.GetString(), out var id) ? id : Guid.Empty;
        var email = claims.TryGetProperty("email", out var mail) ? mail.GetString() ?? "" : "";
        Session = new AuthSession(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), userId, email);
        await _storage.SetItemAsync(SessionKey, Session);
        if (notify) Changed?.Invoke();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object body, string? bearer)
    {
        using var request = CreateRequest(method, url, bearer);
        request.Content = JsonContent.Create(body);
        return await _http.SendAsync(request);
    }

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return "Too many attempts. Please wait a minute and try again.";
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var name in new[] { "msg", "error_description", "message", "error" })
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString()!;
        }
        catch (JsonException) { }
        return $"Sign-in failed ({(int)response.StatusCode}).";
    }

    private const string NetworkError = "Can't reach the server. Check your connection.";

    private static JsonElement ReadJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return default;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        try { return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement.Clone(); }
        catch (Exception exception) when (exception is FormatException or JsonException) { return default; }
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]))
            .ToDictionary(group => group.Key,
                group => Uri.UnescapeDataString((group.First().ElementAtOrDefault(1) ?? "").Replace('+', ' ')));

    public sealed record RedirectResult(bool SignedIn, string? Error);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; } = 3600;
    }
}
