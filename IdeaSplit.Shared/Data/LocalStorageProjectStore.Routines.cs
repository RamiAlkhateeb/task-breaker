using System.Globalization;
using IdeaSplit.Shared.Models;
using IdeaSplit.Shared.Sync;

namespace IdeaSplit.Shared.Data;

public partial class LocalStorageProjectStore : IRoutineStore
{
    private const string RoutinesKey = "nxt.task.routines";

    public async Task<List<Routine>> GetRoutinesAsync()
    {
        await _lock.WaitAsync();
        try { return (await LoadRoutinesAsync()).OrderBy(routine => routine.Title, StringComparer.CurrentCultureIgnoreCase).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task SaveRoutineAsync(Routine routine)
    {
        await _lock.WaitAsync();
        try
        {
            var routines = await LoadRoutinesAsync();
            routines.RemoveAll(existing => existing.Id == routine.Id);
            routine.UpdatedAt = Now();
            routine.IsDirty = true;
            routines.Add(routine);
            await SaveRoutinesAsync(routines);
            var tombstones = await LoadTombstonesAsync();
            if (tombstones.RemoveAll(tombstone => tombstone.Id == routine.Id) > 0)
                await _storage.SetItemAsync(TombstonesKey, tombstones);
        }
        finally { _lock.Release(); }
        LocalChanged?.Invoke();
    }

    public async Task DeleteRoutineAsync(Guid routineId)
    {
        await _lock.WaitAsync();
        try
        {
            var routines = await LoadRoutinesAsync();
            if (routines.RemoveAll(routine => routine.Id == routineId) == 0) return;
            await SaveRoutinesAsync(routines);
            var tombstones = await LoadTombstonesAsync();
            tombstones.Add(new Tombstone(Tombstone.Routines, routineId, Now()));
            await _storage.SetItemAsync(TombstonesKey, tombstones);
        }
        finally { _lock.Release(); }
        LocalChanged?.Invoke();
    }

    public async Task<Routine?> RepeatTaskAsync(Guid projectId, Guid taskId, int days)
    {
        Routine? routine = null;
        await MutateAsync((projects, tombstones) =>
        {
            var project = projects.FirstOrDefault(candidate => candidate.Id == projectId);
            var task = project?.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
            if (project is null || task is null) return false;
            var date = (project.ScheduleDate ?? DateTime.Today).Date;
            routine = new Routine { Title = task.Title, ProjectName = task.ProjectName, Days = days, GeneratedThrough = date };

            // Give the task the id the routine would have used for that day (sub-tasks follow it).
            var copy = CopyTask(task);
            copy.Id = routine.TaskIdFor(date);
            var updated = CopyProjectWithTasks(project);
            updated.Tasks.RemoveAll(candidate => candidate.Id == task.Id);
            updated.Tasks.Add(copy);
            foreach (var sub in updated.Tasks.Where(candidate => candidate.ParentTaskId == task.Id)) sub.ParentTaskId = copy.Id;
            return Upsert(projects, tombstones, updated);
        });
        if (routine is null) return null;
        await SaveRoutineAsync(routine);
        return routine;
    }

    public async Task<bool> AddDueRoutineTasksAsync(DateTime today)
    {
        today = today.Date;
        await _lock.WaitAsync();
        bool added = false, routinesChanged = false;
        try
        {
            var routines = await LoadRoutinesAsync();
            var due = routines.Where(routine => routine.GeneratedThrough < today).ToList();
            if (due.Count == 0) return false;

            var projects = await LoadAsync();
            var tombstones = await LoadTombstonesAsync();
            var existingIds = projects.SelectMany(project => project.Tasks).Select(task => task.Id).ToHashSet();
            var deletedIds = tombstones.Select(tombstone => tombstone.Id).ToHashSet();

            foreach (var routine in due)
            {
                // Only today: days the app wasn't opened don't fill up with overdue copies.
                var id = routine.TaskIdFor(today);
                if (routine.RepeatsOn(today) && !existingIds.Contains(id) && !deletedIds.Contains(id))
                {
                    var day = projects
                        .Where(project => project.ScheduleType == TaskScheduleType.DayWeek && project.ScheduleDate?.Date == today)
                        .OrderBy(project => project.CreatedAt).FirstOrDefault();
                    var updated = day is null
                        ? new Project { Title = today.ToString("dddd, MMM d", CultureInfo.InvariantCulture), ScheduleType = TaskScheduleType.DayWeek, ScheduleDate = today }
                        : CopyProjectWithTasks(day);
                    updated.IsArchived = false;
                    updated.Tasks.Add(new TaskItem
                    {
                        Id = id, ProjectId = updated.Id, Title = routine.Title, ProjectName = routine.ProjectName,
                        SortOrder = updated.Tasks.Count(task => task.ParentTaskId is null)
                    });
                    Upsert(projects, tombstones, updated);
                    existingIds.Add(id);
                    added = true;
                }
                routine.GeneratedThrough = today;
                routine.UpdatedAt = Now();
                routine.IsDirty = true;
                routinesChanged = true;
            }

            if (added) await SaveAsync(projects, tombstones);
            if (routinesChanged) await SaveRoutinesAsync(routines);
        }
        finally { _lock.Release(); }
        if (added || routinesChanged) LocalChanged?.Invoke();
        return added;
    }

    public async Task<bool> ApplyRemoteRoutinesAsync(IReadOnlyList<RoutineRow> remote, bool complete)
    {
        await _lock.WaitAsync();
        try
        {
            var routines = await LoadRoutinesAsync();
            var tombstones = await LoadTombstonesAsync();
            var pendingDeletes = tombstones.Select(tombstone => tombstone.Id).ToHashSet();
            var local = routines.ToDictionary(routine => routine.Id);
            var changed = false;

            foreach (var row in remote)
            {
                if (pendingDeletes.Contains(row.Id)) continue;
                local.TryGetValue(row.Id, out var mine);
                var stamp = row.UpdatedAt.UtcDateTime;
                if (row.Deleted)
                {
                    if (mine is null || (mine.IsDirty && mine.UpdatedAt > stamp)) continue;
                    routines.Remove(mine);
                    local.Remove(row.Id);
                    changed = true;
                    continue;
                }
                if (mine is not null && mine.IsDirty && mine.UpdatedAt >= stamp) continue;
                if (mine is null) { mine = new Routine { Id = row.Id }; routines.Add(mine); local[row.Id] = mine; }
                var through = row.ParsedGeneratedThrough;
                if (mine.Title == row.Title && mine.ProjectName == row.ProjectName && mine.Days == row.Days
                    && mine.GeneratedThrough >= through && mine.UpdatedAt == stamp && !mine.IsDirty) continue;
                mine.Title = row.Title;
                mine.ProjectName = row.ProjectName;
                mine.Days = row.Days;
                // Never move backwards: that would add a task the user already deleted.
                if (through > mine.GeneratedThrough) mine.GeneratedThrough = through;
                mine.UpdatedAt = stamp;
                mine.IsDirty = false;
                changed = true;
            }

            if (complete)
            {
                var remoteIds = remote.Select(row => row.Id).ToHashSet();
                if (routines.RemoveAll(routine => !routine.IsDirty && !remoteIds.Contains(routine.Id)) > 0) changed = true;
            }

            if (changed) await SaveRoutinesAsync(routines);
            return changed;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<Routine>> LoadRoutinesAsync()
    {
        try { return await _storage.GetItemAsync<List<Routine>>(RoutinesKey) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private Task SaveRoutinesAsync(List<Routine> routines) => _storage.SetItemAsync(RoutinesKey, routines).AsTask();

    private static Routine CopyRoutine(Routine routine) => new()
    {
        Id = routine.Id, Title = routine.Title, ProjectName = routine.ProjectName, Days = routine.Days,
        GeneratedThrough = routine.GeneratedThrough, UpdatedAt = routine.UpdatedAt, IsDirty = routine.IsDirty
    };

    private static Project CopyProjectWithTasks(Project project)
    {
        var copy = CopyProject(project);
        copy.Tasks = project.Tasks.Select(CopyTask).ToList();
        return copy;
    }
}
