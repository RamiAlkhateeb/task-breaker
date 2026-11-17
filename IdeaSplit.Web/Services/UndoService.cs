using System.Text.Json;
using IdeaSplit.Shared.Data;
using IdeaSplit.Shared.Models;
using IdeaSplit.Web.Localization;
using Nxt.UI;

namespace IdeaSplit.Web.Services;

/// <summary>
/// One-step undo for changes made from the UI: take a checkpoint, make the change, then offer "Undo" on a toast.
/// Undo puts back the projects the change touched, as they were at the checkpoint.
/// </summary>
public sealed class UndoService
{
    private readonly IProjectStore _store;
    private readonly ToastService _toasts;
    private readonly AppText _text;

    public UndoService(IProjectStore store, ToastService toasts, AppText text)
    {
        _store = store;
        _toasts = toasts;
        _text = text;
    }

    /// <summary>Raised after an undo, so open pages reload.</summary>
    public event Action? Restored;

    public async Task<Checkpoint> CheckpointAsync() =>
        new(this, (await _store.GetProjectsAsync()).ToDictionary(project => project.Id, Serialize));

    public sealed class Checkpoint
    {
        private readonly UndoService _owner;
        private readonly Dictionary<Guid, string> _before;

        internal Checkpoint(UndoService owner, Dictionary<Guid, string> before)
        {
            _owner = owner;
            _before = before;
        }

        /// <summary>Shows <paramref name="message"/> with an Undo button for everything changed since the checkpoint.</summary>
        public async Task OfferAsync(string message)
        {
            var now = (await _owner._store.GetProjectsAsync()).ToDictionary(project => project.Id, Serialize);
            // Compare right away, before a sync can bring in unrelated changes.
            var changed = _before.Where(entry => !now.TryGetValue(entry.Key, out var json) || json != entry.Value)
                .ToDictionary(entry => entry.Key, entry => (string?)entry.Value);
            foreach (var id in now.Keys.Where(id => !_before.ContainsKey(id))) changed[id] = null;
            if (changed.Count == 0) return;

            _owner._toasts.ShowWithAction(message, _owner._text["undo"], () => _owner.RestoreAsync(changed));
        }
    }

    private async Task RestoreAsync(Dictionary<Guid, string?> projects)
    {
        foreach (var (id, json) in projects)
        {
            if (json is null) await _store.DeleteProjectAsync(id);
            else await _store.SaveProjectAsync(JsonSerializer.Deserialize<Project>(json)!);
        }
        Restored?.Invoke();
    }

    // Sync bookkeeping is left out, so a sync finishing in between doesn't count as a change.
    private static string Serialize(Project project)
    {
        var copy = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project))!;
        copy.UpdatedAt = default;
        copy.IsDirty = false;
        foreach (var task in copy.Tasks) { task.UpdatedAt = default; task.IsDirty = false; }
        copy.Tasks = copy.Tasks.OrderBy(task => task.Id).ToList();
        return JsonSerializer.Serialize(copy);
    }
}
