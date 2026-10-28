namespace IdeaSplit.Shared.Models;

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public string Title { get; set; } = "";
    public string? ProjectName { get; set; }
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }

    /// <summary>When this row last changed (UTC); the newer side wins when two devices disagree.</summary>
    public DateTime UpdatedAt { get; set; }
    /// <summary>Changed locally and not yet uploaded. Maintained by the store, not by pages.</summary>
    public bool IsDirty { get; set; } = true;
}
