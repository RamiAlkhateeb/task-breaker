using Blazored.LocalStorage;

namespace IdeaSplit.Shared.Services;

/// <summary>Browser-backed settings. A MAUI implementation can replace this service with SecureStorage.</summary>
public class SettingsService
{
    private const string GeminiKey = "ideasplit_gemini_key";
    private const string ModelKey = "ideasplit_model";
    private const string SearchKey = "ideasplit_search_key";
    private readonly ILocalStorageService _storage;

    public const string DefaultGeminiModel = "gemini-2.0-flash";
    public SettingsService(ILocalStorageService storage) => _storage = storage;
    public Task<string?> GetGeminiApiKeyAsync() => _storage.GetItemAsync<string?>(GeminiKey).AsTask();
    public Task SaveGeminiApiKeyAsync(string apiKey) => _storage.SetItemAsync(GeminiKey, apiKey.Trim()).AsTask();
    public async Task<string> GetGeminiModelAsync() => await _storage.GetItemAsync<string?>(ModelKey) ?? DefaultGeminiModel;
    public Task SaveGeminiModelAsync(string model) => _storage.SetItemAsync(ModelKey, string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim()).AsTask();
    public Task<string?> GetWebSearchApiKeyAsync() => _storage.GetItemAsync<string?>(SearchKey).AsTask();
    public Task SaveWebSearchApiKeyAsync(string apiKey) => _storage.SetItemAsync(SearchKey, apiKey.Trim()).AsTask();
}
