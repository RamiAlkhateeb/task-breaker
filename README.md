# IdeaSplit

IdeaSplit is a fully client-side Blazor WebAssembly app. Projects, settings, and API keys are stored in the browser's local storage; no backend or database is required.

`IdeaSplit.Shared` contains reusable models, the browser project-store contract, and Gemini/search services. A future MAUI Blazor Hybrid app can reference it and provide platform-specific storage implementations.

## Run locally

Install the .NET 8 SDK, then run:

```bash
dotnet restore
dotnet run --project IdeaSplit.Web
```

Open the URL shown in the console. Add a Gemini API key in Settings before creating a project. The optional Bing Web Search key enables the book chapter lookup fallback.

## Static deployment

Publishing produces static files under `IdeaSplit.Web/bin/Release/net8.0/publish/wwwroot`:

```bash
dotnet publish IdeaSplit.Web -c Release
```

Deploy that `wwwroot` folder to GitHub Pages, Cloudflare Pages, Netlify, or any other static host. For GitHub Pages project sites, update the `<base href="/">` value in `IdeaSplit.Web/wwwroot/index.html` to include the repository path (for example, `/IdeaSplit/`).
