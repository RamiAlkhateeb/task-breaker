using System.Globalization;
using Blazored.LocalStorage;
using IdeaSplit.Shared.Models;
using IdeaSplit.Shared.Sync;

namespace IdeaSplit.Shared.Data;

/// <summary>
/// The device's copy of all projects and tasks (browser localStorage), and the source of truth for the UI.
/// Pages save whole projects; the store works out which rows changed, stamps them for sync and records
/// deletions, so pages don't need to know about sync at all.
/// </summary>
public class LocalStorageProjectStore : IProjectStore, ISyncStore
{
    private const string ProjectsKey = "nxt.task.projects";
    private const string TombstonesKey = "nxt.task.tombstones";
    /// <summary>Before sync, data lived here with per-device numeric ids. Migrated once, then left as a backup.</summary>
    private const string LegacyProjectsKey = "ideasplit_projects";

    private readonly ILocalStorageService _storage;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocalStorageProjectStore(ILocalStorageService storage) => _storage = storage;

    public event Action? LocalChanged;

    // ---- IProjectStore (pages) ----

    public async Task<List<Project>> GetProjectsAsync()
    {
        await _lock.WaitAsync();
        try { return (await LoadAsync()).OrderByDescending(project => project.CreatedAt).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<Project?> GetProjectAsync(Guid projectId) =>
        (await GetProjectsAsync()).FirstOrDefault(project => project.Id == projectId);

    public Task SaveProjectAsync(Project project) => MutateAsync((projects, tombstones) => Upsert(projects, tombstones, project));

    public Task DeleteProjectAsync(Guid projectId) => MutateAsync((projects, tombstones) =>
    {
        var project = projects.FirstOrDefault(existing => existing.Id == projectId);
        if (project is null) return false;
        var now = Now();
        projects.Remove(project);
        tombstones.Add(new Tombstone(Tombstone.Projects, project.Id, now));
        tombstones.AddRange(project.Tasks.Select(task => new Tombstone(Tombstone.Tasks, task.Id, now)));
        return true;
    });

    public Task TogglePinAsync(Guid projectId) => MutateAsync((projects, _) =>
    {
        var project = projects.FirstOrDefault(existing => existing.Id == projectId);
        if (project is null) return false;
        project.IsPinned = !project.IsPinned;
        Touch(project);
        return true;
    });

    public Task SetArchivedAsync(Guid projectId, bool archived) => MutateAsync((projects, _) =>
    {
        var project = projects.FirstOrDefault(existing => existing.Id == projectId);
        if (project is null || project.IsArchived == archived) return false;
        project.IsArchived = archived;
        Touch(project);
        return true;
    });

    public async Task<Project> GetOrCreateDayProjectAsync(DateTime date)
    {
        Project? result = null;
        await MutateAsync((projects, tombstones) =>
        {
            var existing = projects
                .Where(project => project.ScheduleType == TaskScheduleType.DayWeek && project.ScheduleDate?.Date == date.Date)
                .OrderBy(project => project.IsArchived)
                .ThenBy(project => project.CreatedAt)
                .FirstOrDefault();
            if (existing is not null)
            {
                result = existing;
                if (!existing.IsArchived) return false;
                existing.IsArchived = false;
                Touch(existing);
                return true;
            }

            result = new Project
            {
                Title = date.ToString("dddd, MMM d", CultureInfo.InvariantCulture),
                ScheduleType = TaskScheduleType.DayWeek,
                ScheduleDate = date.Date
            };
            return Upsert(projects, tombstones, result);
        });
        return result!;
    }

    // ---- ISyncStore (sync engine) ----

    public async Task<SyncSnapshot> GetPendingAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            var tombstones = await LoadTombstonesAsync();
            return new SyncSnapshot(
                projects.Where(project => project.IsDirty).Select(CopyProject).ToList(),
                projects.SelectMany(project => project.Tasks).Where(task => task.IsDirty).Select(CopyTask).ToList(),
                tombstones.ToList());
        }
        finally { _lock.Release(); }
    }

    public async Task MarkPushedAsync(SyncSnapshot pushed)
    {
        await _lock.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            var pushedProjects = pushed.Projects.ToDictionary(project => project.Id, project => project.UpdatedAt);
            var pushedTasks = pushed.Tasks.ToDictionary(task => task.Id, task => task.UpdatedAt);
            foreach (var project in projects)
            {
                // Rows edited again while the upload was in flight keep their pending flag.
                if (pushedProjects.TryGetValue(project.Id, out var stamp) && stamp == project.UpdatedAt) project.IsDirty = false;
                foreach (var task in project.Tasks)
                    if (pushedTasks.TryGetValue(task.Id, out var taskStamp) && taskStamp == task.UpdatedAt) task.IsDirty = false;
            }
            var tombstones = await LoadTombstonesAsync();
            tombstones.RemoveAll(tombstone => pushed.Tombstones.Contains(tombstone));
            await SaveAsync(projects, tombstones);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> ApplyRemoteAsync(IReadOnlyList<ProjectRow> remoteProjects, IReadOnlyList<TaskRow> remoteTasks)
    {
        await _lock.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            var tombstones = await LoadTombstonesAsync();
            var pendingDeletes = tombstones.Select(tombstone => tombstone.Id).ToHashSet();
            var visibleChange = false;
            var anyChange = false;

            // Projects: the newer side wins; local edits not yet uploaded are kept if they are newer.
            var localProjects = projects.ToDictionary(project => project.Id);
            var remoteProjectIds = new HashSet<Guid>();
            foreach (var row in remoteProjects)
            {
                remoteProjectIds.Add(row.Id);
                if (pendingDeletes.Contains(row.Id)) continue;
                localProjects.TryGetValue(row.Id, out var local);
                var remoteStamp = row.UpdatedAt.UtcDateTime;

                if (row.Deleted)
                {
                    if (local is null || (local.IsDirty && local.UpdatedAt > remoteStamp)) continue;
                    projects.Remove(local);
                    localProjects.Remove(row.Id);
                    visibleChange = anyChange = true;
                    continue;
                }

                if (local is null)
                {
                    local = new Project { Id = row.Id, CreatedAt = row.CreatedAt.LocalDateTime, Tasks = [] };
                    projects.Add(local);
                    localProjects[row.Id] = local;
                    ApplyProject(local, row);
                    visibleChange = anyChange = true;
                    continue;
                }

                if (local.IsDirty && local.UpdatedAt >= remoteStamp) continue;
                if (!SameProject(local, row)) { ApplyProject(local, row); visibleChange = anyChange = true; }
                if (local.UpdatedAt != remoteStamp || local.IsDirty) { local.UpdatedAt = remoteStamp; local.IsDirty = false; anyChange = true; }
            }

            // Tasks: same rule; a changed project id moves the task.
            var localTasks = projects.SelectMany(project => project.Tasks.Select(task => (project, task)))
                .GroupBy(entry => entry.task.Id).ToDictionary(group => group.Key, group => group.Last());
            var remoteTaskIds = new HashSet<Guid>();
            foreach (var row in remoteTasks)
            {
                remoteTaskIds.Add(row.Id);
                if (pendingDeletes.Contains(row.Id)) continue;
                var found = localTasks.TryGetValue(row.Id, out var local);
                var remoteStamp = row.UpdatedAt.UtcDateTime;

                if (row.Deleted)
                {
                    if (!found || (local.task.IsDirty && local.task.UpdatedAt > remoteStamp)) continue;
                    local.project.Tasks.Remove(local.task);
                    localTasks.Remove(row.Id);
                    visibleChange = anyChange = true;
                    continue;
                }

                if (found && local.task.IsDirty && local.task.UpdatedAt >= remoteStamp) continue;
                if (!localProjects.TryGetValue(row.ProjectId, out var target)) continue;

                if (!found)
                {
                    var task = new TaskItem { Id = row.Id };
                    ApplyTask(task, row);
                    target.Tasks.Add(task);
                    localTasks[row.Id] = (target, task);
                    visibleChange = anyChange = true;
                    continue;
                }

                if (local.project.Id != target.Id)
                {
                    local.project.Tasks.Remove(local.task);
                    target.Tasks.Add(local.task);
                    localTasks[row.Id] = (target, local.task);
                    visibleChange = anyChange = true;
                }
                if (!SameTask(local.task, row)) { ApplyTask(local.task, row); visibleChange = anyChange = true; }
                if (local.task.UpdatedAt != remoteStamp || local.task.IsDirty) { local.task.UpdatedAt = remoteStamp; local.task.IsDirty = false; anyChange = true; }
            }

            // Rows the server no longer has, and that aren't waiting to be uploaded, were removed elsewhere.
            foreach (var project in projects.ToList())
            {
                if (!project.IsDirty && !remoteProjectIds.Contains(project.Id)) { projects.Remove(project); visibleChange = anyChange = true; continue; }
                if (project.Tasks.RemoveAll(task => !task.IsDirty && !remoteTaskIds.Contains(task.Id)) > 0) visibleChange = anyChange = true;
            }

            if (MergeDuplicateDays(projects, tombstones)) visibleChange = anyChange = true;

            if (anyChange) await SaveAsync(projects, tombstones);
            return visibleChange;
        }
        finally { _lock.Release(); }
    }

    public async Task ClearAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await _storage.RemoveItemAsync(ProjectsKey);
            await _storage.RemoveItemAsync(TombstonesKey);
            // Don't resurrect pre-sync data on the next load once the device is signed out.
            await _storage.RemoveItemAsync(LegacyProjectsKey);
        }
        finally { _lock.Release(); }
    }

    // ---- change tracking ----

    private async Task MutateAsync(Func<List<Project>, List<Tombstone>, bool> change)
    {
        bool changed;
        await _lock.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            var tombstones = await LoadTombstonesAsync();
            changed = change(projects, tombstones);
            if (changed) await SaveAsync(projects, tombstones);
        }
        finally { _lock.Release(); }
        if (changed) LocalChanged?.Invoke();
    }

    /// <summary>Replaces or adds <paramref name="project"/>, stamping the rows that differ from what was stored.</summary>
    private static bool Upsert(List<Project> projects, List<Tombstone> tombstones, Project project)
    {
        var now = Now();
        var index = projects.FindIndex(existing => existing.Id == project.Id);
        var previous = index >= 0 ? projects[index] : null;
        foreach (var task in project.Tasks) task.ProjectId = project.Id;

        var changed = false;
        if (previous is null || !SameProject(previous, project)) { project.UpdatedAt = now; project.IsDirty = true; changed = true; }
        else { project.UpdatedAt = previous.UpdatedAt; project.IsDirty = previous.IsDirty; }

        var before = (previous?.Tasks ?? []).ToDictionary(task => task.Id);
        var elsewhere = projects.Where(other => other.Id != project.Id).SelectMany(other => other.Tasks)
            .GroupBy(task => task.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var task in project.Tasks)
        {
            var prior = before.GetValueOrDefault(task.Id) ?? elsewhere.GetValueOrDefault(task.Id);
            if (prior is null || !SameTask(prior, task)) { task.UpdatedAt = now; task.IsDirty = true; changed = true; }
            else { task.UpdatedAt = prior.UpdatedAt; task.IsDirty = prior.IsDirty; }
        }

        var keptIds = project.Tasks.Select(task => task.Id).ToHashSet();
        foreach (var removed in before.Values.Where(task => !keptIds.Contains(task.Id)))
        {
            changed = true;
            // A task that now lives in another project was moved there, not deleted.
            if (!elsewhere.ContainsKey(removed.Id)) tombstones.Add(new Tombstone(Tombstone.Tasks, removed.Id, now));
        }
        tombstones.RemoveAll(tombstone => tombstone.Id == project.Id || keptIds.Contains(tombstone.Id));

        if (previous is null) projects.Add(project); else projects[index] = project;
        return changed;
    }

    /// <summary>
    /// Two devices can each create the project for the same day while offline. After a pull, fold the
    /// duplicates into the oldest one so the day shows once with all its tasks.
    /// </summary>
    private static bool MergeDuplicateDays(List<Project> projects, List<Tombstone> tombstones)
    {
        var changed = false;
        var duplicates = projects
            .Where(project => project.ScheduleType == TaskScheduleType.DayWeek && project.ScheduleDate is not null)
            .GroupBy(project => project.ScheduleDate!.Value.Date)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var group in duplicates)
        {
            var ordered = group.OrderBy(project => project.CreatedAt).ThenBy(project => project.Id).ToList();
            var keeper = ordered[0];
            var now = Now();
            foreach (var extra in ordered.Skip(1))
            {
                var next = keeper.Tasks.Count(task => task.ParentTaskId is null);
                foreach (var task in extra.Tasks.OrderBy(task => task.ParentTaskId is null ? 0 : 1).ThenBy(task => task.SortOrder))
                {
                    task.ProjectId = keeper.Id;
                    if (task.ParentTaskId is null) task.SortOrder = next++;
                    task.UpdatedAt = now;
                    task.IsDirty = true;
                    keeper.Tasks.Add(task);
                }
                if (keeper.IsArchived && extra.Tasks.Any(task => !task.IsDone)) { keeper.IsArchived = false; Touch(keeper); }
                projects.Remove(extra);
                tombstones.Add(new Tombstone(Tombstone.Projects, extra.Id, now));
                changed = true;
            }
        }
        return changed;
    }

    private static void Touch(Project project)
    {
        project.UpdatedAt = Now();
        project.IsDirty = true;
    }

    /// <summary>UTC now at millisecond precision, so timestamps round-trip through Postgres unchanged.</summary>
    private static DateTime Now()
    {
        var ticks = DateTime.UtcNow.Ticks;
        return new DateTime(ticks - ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private static bool SameProject(Project a, Project b) =>
        a.Title == b.Title && a.OriginalIdea == b.OriginalIdea && a.IsPinned == b.IsPinned && a.IsArchived == b.IsArchived
        && a.ScheduleType == b.ScheduleType && a.ScheduleDate?.Date == b.ScheduleDate?.Date;

    private static bool SameProject(Project local, ProjectRow row) =>
        local.Title == row.Title && local.OriginalIdea == row.OriginalIdea && local.IsPinned == row.IsPinned
        && local.IsArchived == row.IsArchived && (int)local.ScheduleType == row.ScheduleType
        && local.ScheduleDate?.Date == row.ParsedScheduleDate;

    private static bool SameTask(TaskItem a, TaskItem b) =>
        a.ProjectId == b.ProjectId && a.ParentTaskId == b.ParentTaskId && a.Title == b.Title
        && a.ProjectName == b.ProjectName && a.IsDone == b.IsDone && a.SortOrder == b.SortOrder;

    private static bool SameTask(TaskItem local, TaskRow row) =>
        local.ParentTaskId == row.ParentTaskId && local.Title == row.Title && local.ProjectName == row.ProjectName
        && local.IsDone == row.IsDone && local.SortOrder == row.SortOrder;

    private static void ApplyProject(Project local, ProjectRow row)
    {
        local.Title = row.Title;
        local.OriginalIdea = row.OriginalIdea;
        local.IsPinned = row.IsPinned;
        local.IsArchived = row.IsArchived;
        local.ScheduleType = (TaskScheduleType)row.ScheduleType;
        local.ScheduleDate = row.ParsedScheduleDate;
        local.UpdatedAt = row.UpdatedAt.UtcDateTime;
        local.IsDirty = false;
    }

    private static void ApplyTask(TaskItem local, TaskRow row)
    {
        local.ProjectId = row.ProjectId;
        local.ParentTaskId = row.ParentTaskId;
        local.Title = row.Title;
        local.ProjectName = row.ProjectName;
        local.IsDone = row.IsDone;
        local.SortOrder = row.SortOrder;
        local.UpdatedAt = row.UpdatedAt.UtcDateTime;
        local.IsDirty = false;
    }

    private static Project CopyProject(Project project) => new()
    {
        Id = project.Id, Title = project.Title, OriginalIdea = project.OriginalIdea, IsPinned = project.IsPinned,
        IsArchived = project.IsArchived, CreatedAt = project.CreatedAt, ScheduleType = project.ScheduleType,
        ScheduleDate = project.ScheduleDate, UpdatedAt = project.UpdatedAt, IsDirty = project.IsDirty, Tasks = []
    };

    private static TaskItem CopyTask(TaskItem task) => new()
    {
        Id = task.Id, ProjectId = task.ProjectId, ParentTaskId = task.ParentTaskId, Title = task.Title,
        ProjectName = task.ProjectName, IsDone = task.IsDone, SortOrder = task.SortOrder,
        UpdatedAt = task.UpdatedAt, IsDirty = task.IsDirty
    };

    // ---- persistence ----

    private async Task<List<Project>> LoadAsync()
    {
        var projects = await _storage.GetItemAsync<List<Project>>(ProjectsKey);
        if (projects is not null) return projects;

        var migrated = await MigrateLegacyAsync();
        if (migrated.Count > 0) await _storage.SetItemAsync(ProjectsKey, migrated);
        return migrated;
    }

    private async Task<List<Tombstone>> LoadTombstonesAsync() =>
        await _storage.GetItemAsync<List<Tombstone>>(TombstonesKey) ?? [];

    private async Task SaveAsync(List<Project> projects, List<Tombstone> tombstones)
    {
        await _storage.SetItemAsync(ProjectsKey, projects);
        await _storage.SetItemAsync(TombstonesKey, tombstones);
    }

    /// <summary>Converts pre-sync data (numeric ids) to globally unique ids, keeping sub-task links.</summary>
    private async Task<List<Project>> MigrateLegacyAsync()
    {
        List<LegacyProject>? legacy;
        try { legacy = await _storage.GetItemAsync<List<LegacyProject>>(LegacyProjectsKey); }
        catch (System.Text.Json.JsonException) { legacy = null; }
        if (legacy is null || legacy.Count == 0) return [];

        var now = Now();
        var result = new List<Project>();
        foreach (var old in legacy)
        {
            var project = new Project
            {
                Title = old.Title, OriginalIdea = old.OriginalIdea, IsPinned = old.IsPinned, IsArchived = old.IsArchived,
                CreatedAt = old.CreatedAt, ScheduleType = old.ScheduleType, ScheduleDate = old.ScheduleDate,
                UpdatedAt = now, IsDirty = true
            };
            var ids = old.Tasks.Select(task => task.Id).Distinct().ToDictionary(id => id, _ => Guid.NewGuid());
            foreach (var oldTask in old.Tasks)
            {
                project.Tasks.Add(new TaskItem
                {
                    Id = ids[oldTask.Id], ProjectId = project.Id,
                    ParentTaskId = oldTask.ParentTaskId is { } parent && ids.TryGetValue(parent, out var parentId) ? parentId : null,
                    Title = oldTask.Title, ProjectName = oldTask.ProjectName, IsDone = oldTask.IsDone, SortOrder = oldTask.SortOrder,
                    UpdatedAt = now, IsDirty = true
                });
            }
            // Ids were only unique per device; if two old tasks shared one, keep them both as separate tasks.
            foreach (var group in project.Tasks.GroupBy(task => task.Id).Where(group => group.Count() > 1))
                foreach (var duplicate in group.Skip(1)) duplicate.Id = Guid.NewGuid();
            result.Add(project);
        }
        return result;
    }

    private sealed class LegacyProject
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string OriginalIdea { get; set; } = "";
        public bool IsPinned { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public TaskScheduleType ScheduleType { get; set; }
        public DateTime? ScheduleDate { get; set; }
        public List<LegacyTask> Tasks { get; set; } = [];
    }

    private sealed class LegacyTask
    {
        public int Id { get; set; }
        public int? ParentTaskId { get; set; }
        public string Title { get; set; } = "";
        public string? ProjectName { get; set; }
        public bool IsDone { get; set; }
        public int SortOrder { get; set; }
    }
}
