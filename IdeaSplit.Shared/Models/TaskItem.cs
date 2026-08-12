namespace IdeaSplit.Shared.Models;

public class TaskItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}
