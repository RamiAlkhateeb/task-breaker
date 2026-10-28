using System.Globalization;
using System.Text.Json.Serialization;
using IdeaSplit.Shared.Models;

namespace IdeaSplit.Shared.Sync;

/// <summary>A row deleted on this device that the server still has to hear about.</summary>
public sealed record Tombstone(string Table, Guid Id, DateTime DeletedAt)
{
    public const string Projects = "projects";
    public const string Tasks = "tasks";
}

/// <summary>Everything that changed locally since the last successful upload.</summary>
public sealed record SyncSnapshot(List<Project> Projects, List<TaskItem> Tasks, List<Tombstone> Tombstones)
{
    public bool IsEmpty => Projects.Count == 0 && Tasks.Count == 0 && Tombstones.Count == 0;
}

/// <summary>The local data the sync engine reads from and writes back to.</summary>
public interface ISyncStore
{
    /// <summary>Raised after the user changes data on this device (not after sync writes).</summary>
    event Action? LocalChanged;

    Task<SyncSnapshot> GetPendingAsync();

    /// <summary>Clears the pending state of rows that were uploaded, unless they changed again meanwhile.</summary>
    Task MarkPushedAsync(SyncSnapshot pushed);

    /// <summary>Merges the server's full copy into local data; returns true if anything visible changed.</summary>
    Task<bool> ApplyRemoteAsync(IReadOnlyList<ProjectRow> projects, IReadOnlyList<TaskRow> tasks);

    /// <summary>Removes all local data (used on sign-out).</summary>
    Task ClearAsync();
}

/// <summary><c>public.projects</c> as stored in Supabase.</summary>
public sealed class ProjectRow
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("user_id")] public Guid UserId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("original_idea")] public string OriginalIdea { get; set; } = "";
    [JsonPropertyName("is_pinned")] public bool IsPinned { get; set; }
    [JsonPropertyName("is_archived")] public bool IsArchived { get; set; }
    [JsonPropertyName("schedule_type")] public int ScheduleType { get; set; }
    [JsonPropertyName("schedule_date")] public string? ScheduleDate { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }

    public static ProjectRow From(Project project, Guid userId) => new()
    {
        Id = project.Id,
        UserId = userId,
        Title = project.Title,
        OriginalIdea = project.OriginalIdea,
        IsPinned = project.IsPinned,
        IsArchived = project.IsArchived,
        ScheduleType = (int)project.ScheduleType,
        ScheduleDate = project.ScheduleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        CreatedAt = new DateTimeOffset(project.CreatedAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(project.CreatedAt, DateTimeKind.Local) : project.CreatedAt).ToUniversalTime(),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(project.UpdatedAt, DateTimeKind.Utc)),
        Deleted = false
    };

    public DateTime? ParsedScheduleDate =>
        DateTime.TryParseExact(ScheduleDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : null;
}

/// <summary><c>public.tasks</c> as stored in Supabase.</summary>
public sealed class TaskRow
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("user_id")] public Guid UserId { get; set; }
    [JsonPropertyName("project_id")] public Guid ProjectId { get; set; }
    [JsonPropertyName("parent_task_id")] public Guid? ParentTaskId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("project_name")] public string? ProjectName { get; set; }
    [JsonPropertyName("is_done")] public bool IsDone { get; set; }
    [JsonPropertyName("sort_order")] public int SortOrder { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }

    public static TaskRow From(TaskItem task, Guid userId) => new()
    {
        Id = task.Id,
        UserId = userId,
        ProjectId = task.ProjectId,
        ParentTaskId = task.ParentTaskId,
        Title = task.Title,
        ProjectName = task.ProjectName,
        IsDone = task.IsDone,
        SortOrder = task.SortOrder,
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(task.UpdatedAt, DateTimeKind.Utc)),
        Deleted = false
    };
}
