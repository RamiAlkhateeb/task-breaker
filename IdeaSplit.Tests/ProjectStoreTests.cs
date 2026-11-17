using System.Text.Json;
using IdeaSplit.Shared.Data;
using IdeaSplit.Shared.Models;
using IdeaSplit.Shared.Sync;

namespace IdeaSplit.Tests;

public class ProjectStoreTests
{
    private readonly FakeLocalStorage _storage = new();
    private readonly LocalStorageProjectStore _store;
    private static readonly Guid User = Guid.NewGuid();

    public ProjectStoreTests() => _store = new LocalStorageProjectStore(_storage);

    private static Project Day(DateTime date, params string[] tasks)
    {
        var project = new Project { Title = date.ToString("dddd, MMM d", System.Globalization.CultureInfo.InvariantCulture), ScheduleType = TaskScheduleType.DayWeek, ScheduleDate = date.Date };
        project.Tasks.AddRange(tasks.Select((title, index) => new TaskItem { ProjectId = project.Id, Title = title, SortOrder = index }));
        return project;
    }

    private async Task<Project> SavedAsync(Project project)
    {
        await _store.SaveProjectAsync(project);
        return (await _store.GetProjectAsync(project.Id))!;
    }

    /// <summary>Simulates a successful upload so every row is clean.</summary>
    private async Task PushAllAsync() => await _store.MarkPushedAsync(await _store.GetPendingAsync());

    [Fact]
    public async Task Saving_marks_only_changed_rows_for_upload()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A", "B"));
        await PushAllAsync();

        project.Tasks.First(task => task.Title == "A").IsDone = true;
        await _store.SaveProjectAsync(project);

        var pending = await _store.GetPendingAsync();
        Assert.Empty(pending.Projects);
        Assert.Equal(["A"], pending.Tasks.Select(task => task.Title));
    }

    [Fact]
    public async Task Deleting_a_project_records_it_and_its_tasks_for_upload()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A", "B"));
        await PushAllAsync();

        await _store.DeleteProjectAsync(project.Id);

        var pending = await _store.GetPendingAsync();
        Assert.Contains(pending.Tombstones, tombstone => tombstone.Table == Tombstone.Projects && tombstone.Id == project.Id);
        Assert.Equal(2, pending.Tombstones.Count(tombstone => tombstone.Table == Tombstone.Tasks));
    }

    [Fact]
    public async Task Moving_a_task_to_another_day_is_not_a_deletion()
    {
        var today = await SavedAsync(Day(DateTime.Today, "A", "B"));
        await PushAllAsync();

        var target = await _store.MoveTasksToDayAsync(today, today.Tasks.Where(task => task.Title == "A"), DateTime.Today.AddDays(1));

        var pending = await _store.GetPendingAsync();
        Assert.Empty(pending.Tombstones);
        var moved = Assert.Single(pending.Tasks, task => task.Title == "A");
        Assert.Equal(target.Id, moved.ProjectId);
        Assert.Equal(["B"], (await _store.GetProjectAsync(today.Id))!.Tasks.Select(task => task.Title));
    }

    [Fact]
    public async Task Moving_the_last_open_task_away_archives_the_day()
    {
        var day = Day(DateTime.Today.AddDays(-1), "A", "Done");
        day.Tasks[1].IsDone = true;
        day = await SavedAsync(day);

        await _store.MoveTasksToDayAsync(day, day.Tasks.Where(task => !task.IsDone), DateTime.Today);

        Assert.True((await _store.GetProjectAsync(day.Id))!.IsArchived);
    }

    [Fact]
    public async Task Moving_a_task_takes_its_subtasks_along()
    {
        var day = Day(DateTime.Today, "Parent");
        day.Tasks.Add(new TaskItem { ProjectId = day.Id, ParentTaskId = day.Tasks[0].Id, Title = "Child" });
        day = await SavedAsync(day);

        var target = await _store.MoveTasksToDayAsync(day, [day.Tasks.First(task => task.Title == "Parent")], DateTime.Today.AddDays(1));

        Assert.Equal(["Child", "Parent"], (await _store.GetProjectAsync(target.Id))!.Tasks.Select(task => task.Title).Order());
    }

    [Fact]
    public async Task A_newer_server_edit_replaces_a_clean_local_row()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A"));
        await PushAllAsync();
        var task = TaskRow.From(project.Tasks[0], User);
        task.IsDone = true;
        task.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        var changed = await _store.ApplyRemoteAsync([ProjectRow.From(project, User)], [task]);

        Assert.True(changed);
        Assert.True((await _store.GetProjectAsync(project.Id))!.Tasks[0].IsDone);
    }

    [Fact]
    public async Task A_newer_local_edit_not_yet_uploaded_wins_over_the_server()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A"));
        var stale = TaskRow.From(project.Tasks[0], User);
        stale.Title = "Old";
        stale.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await _store.ApplyRemoteAsync([ProjectRow.From(project, User)], [stale]);

        Assert.Equal("A", (await _store.GetProjectAsync(project.Id))!.Tasks[0].Title);
    }

    [Fact]
    public async Task Rows_deleted_on_the_server_are_removed_here()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A"));
        await PushAllAsync();
        var row = ProjectRow.From(project, User);
        row.Deleted = true;
        row.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        await _store.ApplyRemoteAsync([row], []);

        Assert.Null(await _store.GetProjectAsync(project.Id));
    }

    [Fact]
    public async Task Only_a_complete_download_removes_rows_the_server_did_not_send()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A"));
        await PushAllAsync();

        await _store.ApplyRemoteAsync([], [], complete: false);
        Assert.NotNull(await _store.GetProjectAsync(project.Id));

        await _store.ApplyRemoteAsync([], [], complete: true);
        Assert.Null(await _store.GetProjectAsync(project.Id));
    }

    [Fact]
    public async Task Two_devices_creating_the_same_day_end_up_with_one()
    {
        var mine = await SavedAsync(Day(DateTime.Today, "Mine"));
        await PushAllAsync();
        var theirs = Day(DateTime.Today, "Theirs");
        theirs.CreatedAt = mine.CreatedAt.AddMinutes(-1);
        var stamp = DateTimeOffset.UtcNow;
        var projectRow = ProjectRow.From(theirs, User); projectRow.UpdatedAt = stamp;
        var taskRow = TaskRow.From(theirs.Tasks[0], User); taskRow.UpdatedAt = stamp;

        await _store.ApplyRemoteAsync([ProjectRow.From(mine, User), projectRow], [TaskRow.From(mine.Tasks[0], User), taskRow]);

        var day = Assert.Single(await _store.GetProjectsAsync());
        Assert.Equal(theirs.Id, day.Id);
        Assert.Equal(["Mine", "Theirs"], day.Tasks.Select(task => task.Title).Order());
        Assert.Contains((await _store.GetPendingAsync()).Tombstones, tombstone => tombstone.Id == mine.Id);
    }

    [Fact]
    public async Task An_edit_made_during_an_upload_stays_pending()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A"));
        var uploading = await _store.GetPendingAsync();
        await Task.Delay(5);
        project.Tasks[0].Title = "Edited";
        await _store.SaveProjectAsync(project);

        await _store.MarkPushedAsync(uploading);

        Assert.Equal(["Edited"], (await _store.GetPendingAsync()).Tasks.Select(task => task.Title));
    }

    [Fact]
    public async Task Data_from_before_sync_is_converted_once_with_subtasks_kept()
    {
        _storage.Items["ideasplit_projects"] = JsonSerializer.Serialize(new[]
        {
            new { Id = 1, Title = "Garden", ScheduleType = 1, CreatedAt = DateTime.Now, Tasks = new object[]
            {
                new { Id = 1, Title = "Seeds", SortOrder = 0 },
                new { Id = 2, ParentTaskId = (int?)1, Title = "Tomato", SortOrder = 0 },
                new { Id = 2, ParentTaskId = (int?)null, Title = "Same id", SortOrder = 1 }
            } }
        });

        var project = Assert.Single(await _store.GetProjectsAsync());

        Assert.Equal(3, project.Tasks.Select(task => task.Id).Distinct().Count());
        var seeds = project.Tasks.Single(task => task.Title == "Seeds");
        Assert.Equal(seeds.Id, project.Tasks.Single(task => task.Title == "Tomato").ParentTaskId);
        Assert.Equal(3, (await _store.GetPendingAsync()).Tasks.Count);
    }

    [Fact]
    public async Task Undoing_a_delete_brings_the_project_back_for_upload()
    {
        var project = await SavedAsync(Day(DateTime.Today, "A"));
        await PushAllAsync();
        var copy = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project))!;

        await _store.DeleteProjectAsync(project.Id);
        await _store.SaveProjectAsync(copy);

        var pending = await _store.GetPendingAsync();
        Assert.Empty(pending.Tombstones);
        Assert.Single(pending.Projects);
        Assert.Single(pending.Tasks);
    }
}
