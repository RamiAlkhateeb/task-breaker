using System.Globalization;
using Microsoft.JSInterop;
using Nxt.UI;

namespace IdeaSplit.Web.Localization;

/// <summary>
/// NxtTask's switchable English / Arabic locale. The shared <see cref="FixedLocale"/> is single-language
/// (its SetLanguageAsync does nothing and Settings hides the picker), so the app provides its own.
/// The choice is remembered on this device and applied to .NET formatting and the page direction.
/// </summary>
public sealed class AppLocale : INxtLocale
{
    private const string StorageKey = "nxt.lang";
    private readonly IJSRuntime _js;
    private NxtLanguage _current = FixedLocale.English;

    public AppLocale(IJSRuntime js) => _js = js;

    public string Language => _current.Code;
    public bool IsRtl => _current.IsRtl;
    public IReadOnlyList<NxtLanguage> Languages { get; } = [FixedLocale.English, FixedLocale.Arabic];
    public event Action? Changed;

    /// <summary>Loads the saved language before the first render.</summary>
    public async Task InitializeAsync()
    {
        string? saved = null;
        try { saved = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey); }
        catch (JSException) { /* storage unavailable: stay in English */ }
        await ApplyAsync(saved ?? FixedLocale.English.Code);
    }

    public async Task SetLanguageAsync(string code)
    {
        if (code == Language) return;
        await ApplyAsync(code);
        try { await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, Language); }
        catch (JSException) { /* not remembered, still switched for this visit */ }
        Changed?.Invoke();
    }

    private async Task ApplyAsync(string code)
    {
        _current = Languages.FirstOrDefault(language => language.Code == code) ?? FixedLocale.English;

        // ar-SY: Arabic month and day names on the Gregorian calendar (ar-SA would switch to Hijri dates).
        var culture = CultureInfo.GetCultureInfo(IsRtl ? "ar-SY" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        try
        {
            await _js.InvokeVoidAsync("document.documentElement.setAttribute", "lang", Language);
            await _js.InvokeVoidAsync("document.documentElement.setAttribute", "dir", IsRtl ? "rtl" : "ltr");
        }
        catch (JSException) { /* cosmetic */ }
    }
}
