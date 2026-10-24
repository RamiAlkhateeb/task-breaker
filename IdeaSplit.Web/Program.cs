using Blazored.LocalStorage;
using IdeaSplit.Shared.Data;
using IdeaSplit.Shared.Services;
using IdeaSplit.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nxt.UI;
using Nxt.UI.Ai;
using IdeaSplit.Web.Localization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<IProjectStore, LocalStorageProjectStore>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<GeminiService>();
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddSingleton<AppLocale>();
builder.Services.AddSingleton<INxtLocale>(sp => sp.GetRequiredService<AppLocale>());
builder.Services.AddSingleton<AppText>();
builder.Services.AddNxtUi(o =>
{
    o.App = NxtAppId.NxtTask;
    o.LogoUrl = "icons/logo.png";
    o.Description = new()
    {
        ["en"] = "Turn ideas into clear, manageable tasks, planned across your days.",
        ["ar"] = "حوّل أفكارك إلى مهام واضحة موزّعة على أيامك."
    };
    // Settings written by Blazored.LocalStorage before the move to the shared library.
    o.LegacyStorageKeys = new()
    {
        [AiSettingsService.ApiKeyKey] = "ideasplit_gemini_key",
        [AiSettingsService.ModelKey] = "ideasplit_model"
    };
});

var host = builder.Build();
await host.Services.GetRequiredService<AppLocale>().InitializeAsync();
await host.RunAsync();
