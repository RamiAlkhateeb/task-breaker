namespace IdeaSplit.Shared.Models;

public class Project
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string OriginalIdea { get; set; } = "";
    public bool IsBook { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<TaskItem> Tasks { get; set; } = new();

    public int TotalCount => Tasks.Count;
    public int DoneCount => Tasks.Count(t => t.IsDone);
    public double ProgressPercent => TotalCount == 0 ? 0 : (double)DoneCount / TotalCount * 100;
}
