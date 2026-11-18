using System.Text.Json;
using Blazored.LocalStorage;

namespace IdeaSplit.Tests;

/// <summary>In-memory localStorage that stores JSON like the browser one, so values round-trip the same way.</summary>
public sealed class FakeLocalStorage : ILocalStorageService
{
    public Dictionary<string, string> Items { get; } = new();

#pragma warning disable CS0067 // Not raised: nothing in the app listens to them.
    public event EventHandler<ChangingEventArgs>? Changing;
    public event EventHandler<ChangedEventArgs>? Changed;
#pragma warning restore CS0067

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) { Items.Clear(); return ValueTask.CompletedTask; }

    public ValueTask<bool> ContainKeyAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(Items.ContainsKey(key));

    public ValueTask<string?> GetItemAsStringAsync(string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Items.TryGetValue(key, out var value) ? value : null);

    public ValueTask<T?> GetItemAsync<T>(string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Items.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value) : default);

    public ValueTask<string?> KeyAsync(int index, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Items.Keys.ElementAtOrDefault(index));

    public ValueTask<IEnumerable<string>> KeysAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IEnumerable<string>>(Items.Keys.ToList());

    public ValueTask<int> LengthAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Items.Count);

    public ValueTask RemoveItemAsync(string key, CancellationToken cancellationToken = default) { Items.Remove(key); return ValueTask.CompletedTask; }

    public ValueTask RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys) Items.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetItemAsStringAsync(string key, string data, CancellationToken cancellationToken = default) { Items[key] = data; return ValueTask.CompletedTask; }

    public ValueTask SetItemAsync<T>(string key, T data, CancellationToken cancellationToken = default) { Items[key] = JsonSerializer.Serialize(data); return ValueTask.CompletedTask; }
}
