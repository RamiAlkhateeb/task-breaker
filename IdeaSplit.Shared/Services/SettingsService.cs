using Blazored.LocalStorage;

namespace IdeaSplit.Shared.Services;

/// <summary>Browser-backed settings. A MAUI implementation can replace this service with SecureStorage.</summary>
public class SettingsService
{
    private const string GeminiKey = "ideasplit_gemini_key";
    private const string ModelKey = "ideasplit_model";
    private const string SpeechLanguageKey = "ideasplit_speech_language";
    private readonly ILocalStorageService _storage;

    public const string DefaultGeminiModel = "gemini-2.0-flash";
    public const string DefaultSpeechLanguage = "en-US";
    public SettingsService(ILocalStorageService storage) => _storage = storage;
    public Task<string?> GetGeminiApiKeyAsync() => _storage.GetItemAsync<string?>(GeminiKey).AsTask();
    public Task SaveGeminiApiKeyAsync(string apiKey) => _storage.SetItemAsync(GeminiKey, apiKey.Trim()).AsTask();
    public async Task<string> GetGeminiModelAsync() => await _storage.GetItemAsync<string?>(ModelKey) ?? DefaultGeminiModel;
    public Task SaveGeminiModelAsync(string model) => _storage.SetItemAsync(ModelKey, string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim()).AsTask();
    public async Task<string> GetSpeechLanguageAsync() => await _storage.GetItemAsync<string?>(SpeechLanguageKey) ?? DefaultSpeechLanguage;
    public Task SaveSpeechLanguageAsync(string language) => _storage.SetItemAsync(SpeechLanguageKey, string.IsNullOrWhiteSpace(language) ? DefaultSpeechLanguage : language.Trim()).AsTask();
}
