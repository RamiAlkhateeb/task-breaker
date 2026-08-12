# IdeaSplit

Complete, ready-to-run solution. Two projects: `IdeaSplit.Shared` (models, EF Core, Gemini/Settings services — reusable later by a MAUI project) and `IdeaSplit.Web` (Blazor Server app).

## Run it

You need the .NET 8 SDK (`dotnet --version` should print `8.x`).

```bash
cd IdeaSplit
dotnet restore
dotnet run --project IdeaSplit.Web
```

Open the URL shown in the console (defaults to `http://localhost:5080`). First screen is empty — go to **Settings**, paste a free Gemini key from https://aistudio.google.com/apikey, then **New project**.

The SQLite database is created automatically on first run at your OS's local app data folder (`ideasplit.db`).

## Why a class library, not a Razor Class Library
Nothing here is UI — models, EF Core, and the Gemini HTTP call are plain C#. A MAUI Blazor Hybrid project later references `IdeaSplit.Shared` the same way `IdeaSplit.Web` does; only the `.razor` pages get rebuilt for touch/mobile layout, and even those you can mostly copy over as-is.

`SettingsService` here uses a local JSON file for the API key (web has no MAUI SecureStorage). When you add the MAUI project, swap its implementation for `SecureStorage` — same interface, so nothing else changes.

Get a free Gemini key: https://aistudio.google.com/apikey
