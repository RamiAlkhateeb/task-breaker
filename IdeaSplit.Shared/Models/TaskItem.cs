namespace IdeaSplit.Shared.Models;

public enum TaskScheduleType
{
    DayWeek,
    ProjectDeadline
}

public class TaskItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
    public TaskScheduleType ScheduleType { get; set; } = TaskScheduleType.DayWeek;
    public DateTime? DueDate { get; set; }
}
