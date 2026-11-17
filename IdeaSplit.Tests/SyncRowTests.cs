using System.Text.Json;
using IdeaSplit.Shared.Models;
using IdeaSplit.Shared.Sync;

namespace IdeaSplit.Tests;

/// <summary>Supabase rejects an upload that names a column the table doesn't have.</summary>
public class SyncRowTests
{
    private static readonly string[] ProjectColumns = ["id", "user_id", "title", "original_idea", "is_pinned", "is_archived", "schedule_type", "schedule_date", "created_at", "updated_at", "deleted"];
    private static readonly string[] TaskColumns = ["id", "user_id", "project_id", "parent_task_id", "title", "project_name", "is_done", "sort_order", "updated_at", "deleted"];

    private static IEnumerable<string> SentNames<T>(T row) =>
        JsonDocument.Parse(JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement.EnumerateObject().Select(property => property.Name);

    [Fact]
    public void Project_rows_send_only_table_columns()
    {
        var row = ProjectRow.From(new Project { Title = "T", ScheduleDate = DateTime.Today }, Guid.NewGuid());
        Assert.Equal(ProjectColumns.Order(), SentNames(row).Order());
    }

    [Fact]
    public void Task_rows_send_only_table_columns()
    {
        var row = TaskRow.From(new TaskItem { Title = "T" }, Guid.NewGuid());
        Assert.Equal(TaskColumns.Order(), SentNames(row).Order());
    }

    [Fact]
    public void Schedule_dates_are_sent_as_plain_dates()
    {
        var row = ProjectRow.From(new Project { ScheduleDate = new DateTime(2026, 11, 17) }, Guid.NewGuid());
        Assert.Equal("2026-11-17", row.ScheduleDate);
        Assert.Equal(new DateTime(2026, 11, 17), row.ParsedScheduleDate);
    }
}
