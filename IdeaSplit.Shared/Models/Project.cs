namespace IdeaSplit.Shared.Models;

public enum TaskScheduleType
{
    DayWeek,
    ProjectDeadline
}

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string OriginalIdea { get; set; } = "";
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public TaskScheduleType ScheduleType { get; set; } = TaskScheduleType.DayWeek;
    public DateTime? ScheduleDate { get; set; }

    /// <summary>When this row last changed (UTC); the newer side wins when two devices disagree.</summary>
    public DateTime UpdatedAt { get; set; }
    /// <summary>Changed locally and not yet uploaded. Maintained by the store, not by pages.</summary>
    public bool IsDirty { get; set; } = true;

    public List<TaskItem> Tasks { get; set; } = new();

    public int TotalCount => Tasks.Count(t => t.ParentTaskId is null);
    public int DoneCount => Tasks.Count(t => t.IsDone && t.ParentTaskId is null);
    public double ProgressPercent => TotalCount == 0 ? 0 : (double)DoneCount / TotalCount * 100;
}
