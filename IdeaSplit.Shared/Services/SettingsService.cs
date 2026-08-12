namespace IdeaSplit.Shared.Services;

/// <summary>
/// Stores the Gemini API key. This file-based implementation works on Blazor Web.
/// When the MAUI project is added, replace the body of these two methods with
/// SecureStorage.Default.SetAsync/GetAsync — nothing else in the app needs to change.
/// </summary>
public class SettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ideasplit_settings.txt");
    private readonly string _modelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ideasplit_model.txt");

    public const string DefaultGeminiModel = "gemini-2.0-flash";

    public Task<string?> GetGeminiApiKeyAsync()
    {
        if (!File.Exists(_path)) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(File.ReadAllText(_path).Trim());
    }

    public async Task SaveGeminiApiKeyAsync(string apiKey)
    {
        await File.WriteAllTextAsync(_path, apiKey.Trim());
    }

    public Task<string> GetGeminiModelAsync()
    {
        if (!File.Exists(_modelPath)) return Task.FromResult(DefaultGeminiModel);

        var model = File.ReadAllText(_modelPath).Trim();
        return Task.FromResult(string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model);
    }

    public async Task SaveGeminiModelAsync(string model)
    {
        await File.WriteAllTextAsync(_modelPath,
            string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim());
    }
}
