# NxtTask (IdeaSplit)

IdeaSplit is a client-side Blazor WebAssembly app. Projects, settings, and API keys are stored in the browser's local storage, so the app works offline and without an account.

Optional sign-in (Settings → Account & sync) syncs projects between devices through Supabase: email sign-in, plus `projects` and `tasks` tables protected by row-level security. The deploy workflow writes the project's public URL and publishable key into `wwwroot/appsettings.json` from the `SUPABASE_URL` and `SUPABASE_PUBLISHABLE_KEY` environment variables. Without that file the Account section is hidden. Sync is in `IdeaSplit.Shared/Sync`: local changes are uploaded, then everything is downloaded and merged, with the latest edit winning.

`IdeaSplit.Shared` contains reusable models, the browser project-store contract, and the Gemini service. A future MAUI Blazor Hybrid app can reference it and provide platform-specific storage implementations.

## Run locally

Install the .NET 8 SDK, then run:

```bash
dotnet restore
dotnet run --project IdeaSplit.Web
```

Open the URL shown in the console. Add a Gemini API key in Settings before creating a project.

## Static deployment

Publishing produces static files under `IdeaSplit.Web/bin/Release/net10.0/publish/wwwroot`:

```bash
dotnet publish IdeaSplit.Web -c Release
```

Deploy that `wwwroot` folder to GitHub Pages, Cloudflare Pages, Netlify, or any other static host. For GitHub Pages project sites, update the `<base href="/">` value in `IdeaSplit.Web/wwwroot/index.html` to include the repository path (for example, `/IdeaSplit/`).

Shared UI (settings, AI chat, theme, logos) comes from the `Nxt.UI` submodule in `lib/Nxt.UI`. Clone with `git clone --recurse-submodules`, or run `git submodule update --init` after cloning.

## Built-in AI (no Gemini key)

Signed-in users without a Gemini key get AI through `functions/api/ai.js`, a Cloudflare Pages Function deployed with the site. It runs Cloudflare Workers AI and allows each user 20 requests a day. In the Pages project (Settings → Bindings) add:

- **Workers AI**, variable name `AI`
- **KV namespace**, variable name `AI_LIMITS` (create one first under Storage & Databases → KV)

Optional environment variables: `AI_MODEL` (default `@cf/meta/llama-3.3-70b-instruct-fp8-fast`) and `AI_DAILY_LIMIT` (default 20). Bindings apply from the next deployment.

