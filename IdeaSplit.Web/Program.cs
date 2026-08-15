using Blazored.LocalStorage;
using IdeaSplit.Shared.Data;
using IdeaSplit.Shared.Services;
using IdeaSplit.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<IProjectStore, LocalStorageProjectStore>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<GeminiService>();
builder.Services.AddScoped<WebSearchService>();
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
});

await builder.Build().RunAsync();
