using Blazored.LocalStorage;

namespace IdeaSplit.Shared.Services;

/// <summary>
/// NxtTask-only settings. The Gemini key, model and theme are shared family settings stored by Nxt.UI.
/// </summary>
public class SettingsService
{
    private const string SpeechLanguageKey = "ideasplit_speech_language";
    private readonly ILocalStorageService _storage;

    public const string DefaultSpeechLanguage = "en-US";
    public SettingsService(ILocalStorageService storage) => _storage = storage;
    public async Task<string> GetSpeechLanguageAsync() => await _storage.GetItemAsync<string?>(SpeechLanguageKey) ?? DefaultSpeechLanguage;
    public Task SaveSpeechLanguageAsync(string language) => _storage.SetItemAsync(SpeechLanguageKey, string.IsNullOrWhiteSpace(language) ? DefaultSpeechLanguage : language.Trim()).AsTask();
}
