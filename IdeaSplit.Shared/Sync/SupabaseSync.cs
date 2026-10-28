using System.Net.Http.Json;
using System.Text.Json;
using Blazored.LocalStorage;

namespace IdeaSplit.Shared.Sync;

/// <summary>
/// Keeps this device's data and the user's Supabase rows in step: upload local changes, download
/// everything, merge (latest edit wins). Runs on start, after sign-in, shortly after each edit,
/// and when the app comes back to the foreground or online.
/// </summary>
public sealed class SupabaseSync : IDisposable
{
    private const string LastSyncedKey = "nxt.task.last_synced";
    private const int PageSize = 1000;
    private const int ChunkSize = 200;
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.5);

    private readonly SupabaseConfig _config;
    private readonly SupabaseAuth _auth;
    private readonly ISyncStore _store;
    private readonly HttpClient _http;
    private readonly ILocalStorageService _storage;
    private CancellationTokenSource? _pending;
    private bool _running;
    private bool _again;

    public SupabaseSync(SupabaseConfig config, SupabaseAuth auth, ISyncStore store, HttpClient http, ILocalStorageService storage)
    {
        _config = config;
        _auth = auth;
        _store = store;
        _http = http;
        _storage = storage;
        _store.LocalChanged += ScheduleSoon;
    }

    public bool IsSyncing => _running;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised when syncing starts or stops, or its result changes.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised when downloaded changes altered what pages show.</summary>
    public event Action? DataChanged;

    public async Task InitializeAsync()
    {
        try { LastSyncedAt = await _storage.GetItemAsync<DateTimeOffset?>(LastSyncedKey); }
        catch (JsonException) { LastSyncedAt = null; }
    }

    /// <summary>Syncs after a short pause, so a burst of edits goes up together.</summary>
    public void ScheduleSoon()
    {
        if (!_auth.IsSignedIn) return;
        _pending?.Cancel();
        var cancel = _pending = new CancellationTokenSource();
        _ = Task.Delay(Debounce, cancel.Token).ContinueWith(async delay =>
        {
            if (!delay.IsCanceled) await SyncNowAsync();
        }, TaskScheduler.Default);
    }

    public async Task SyncNowAsync()
    {
        if (!_auth.IsSignedIn) return;
        // A request while a run is in progress makes that run go round once more.
        _again = true;
        if (_running) return;

        _running = true;
        StatusChanged?.Invoke();
        try
        {
            while (_again)
            {
                _again = false;
                await RunOnceAsync();
            }
            LastError = null;
            LastSyncedAt = DateTimeOffset.UtcNow;
            await _storage.SetItemAsync(LastSyncedKey, LastSyncedAt);
        }
        catch (Exception exception) when (exception is HttpRequestException or SyncException or JsonException or TaskCanceledException)
        {
            LastError = exception is SyncException ? exception.Message : "Can't reach the server. Changes are saved on this device.";
        }
        finally
        {
            _running = false;
            _again = false;
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Signs out and, if asked, removes this device's copy of the data.</summary>
    public async Task SignOutAsync(bool clearLocalData)
    {
        _pending?.Cancel();
        await _auth.SignOutAsync();
        LastSyncedAt = null;
        LastError = null;
        await _storage.RemoveItemAsync(LastSyncedKey);
        if (clearLocalData)
        {
            await _store.ClearAsync();
            DataChanged?.Invoke();
        }
        StatusChanged?.Invoke();
    }

    private async Task RunOnceAsync()
    {
        var token = await _auth.GetAccessTokenAsync() ?? throw new SyncException(_auth.IsSignedIn
            ? "Can't reach the server. Changes are saved on this device."
            : "Signed out. Sign in again to keep syncing.");
        var userId = _auth.Session!.UserId;

        await PushAsync(token, userId);
        var projects = await PullAsync<ProjectRow>(token, "projects");
        var tasks = await PullAsync<TaskRow>(token, "tasks");
        if (await _store.ApplyRemoteAsync(projects, tasks)) DataChanged?.Invoke();
        // Merging can itself create changes (e.g. two devices made the same day); upload those now.
        await PushAsync(token, userId);
    }

    private async Task PushAsync(string token, Guid userId)
    {
        var pending = await _store.GetPendingAsync();
        if (pending.IsEmpty) return;

        // Projects first: tasks reference them. Parents before sub-tasks for the same reason.
        foreach (var chunk in pending.Projects.Chunk(ChunkSize))
            await UpsertAsync(token, "projects", chunk.Select(project => ProjectRow.From(project, userId)));
        var orderedTasks = pending.Tasks.OrderBy(task => task.ParentTaskId is null ? 0 : 1);
        foreach (var chunk in orderedTasks.Chunk(ChunkSize))
            await UpsertAsync(token, "tasks", chunk.Select(task => TaskRow.From(task, userId)));

        // Deletions are kept as rows marked deleted, so other devices hear about them.
        foreach (var table in pending.Tombstones.GroupBy(tombstone => tombstone.Table))
            foreach (var chunk in table.Chunk(ChunkSize))
            {
                var ids = string.Join(',', chunk.Select(tombstone => tombstone.Id));
                var stamp = chunk.Max(tombstone => tombstone.DeletedAt);
                using var request = _auth.CreateRequest(HttpMethod.Patch, $"{_config.BaseUrl}/rest/v1/{table.Key}?id=in.({ids})", token);
                request.Headers.Add("Prefer", "return=minimal");
                request.Content = JsonContent.Create(new { deleted = true, updated_at = new DateTimeOffset(DateTime.SpecifyKind(stamp, DateTimeKind.Utc)) });
                using var response = await _http.SendAsync(request);
                await EnsureSuccessAsync(response);
            }

        await _store.MarkPushedAsync(pending);
    }

    private async Task UpsertAsync<T>(string token, string table, IEnumerable<T> rows)
    {
        using var request = _auth.CreateRequest(HttpMethod.Post, $"{_config.BaseUrl}/rest/v1/{table}?on_conflict=id", token);
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        request.Content = JsonContent.Create(rows.ToList());
        using var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private async Task<List<T>> PullAsync<T>(string token, string table)
    {
        var all = new List<T>();
        for (var offset = 0; ; offset += PageSize)
        {
            using var request = _auth.CreateRequest(HttpMethod.Get,
                $"{_config.BaseUrl}/rest/v1/{table}?select=*&order=id&limit={PageSize}&offset={offset}", token);
            using var response = await _http.SendAsync(request);
            await EnsureSuccessAsync(response);
            var page = await response.Content.ReadFromJsonAsync<List<T>>() ?? [];
            all.AddRange(page);
            if (page.Count < PageSize) return all;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        var message = body;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("message", out var value)) message = value.GetString() ?? body;
        }
        catch (JsonException) { }
        throw new SyncException($"Sync failed ({(int)response.StatusCode}): {message}");
    }

    public void Dispose()
    {
        _store.LocalChanged -= ScheduleSoon;
        _pending?.Cancel();
    }

    private sealed class SyncException(string message) : Exception(message);
}
