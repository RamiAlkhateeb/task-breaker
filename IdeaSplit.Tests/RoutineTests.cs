using IdeaSplit.Shared.Data;
using IdeaSplit.Shared.Models;
using IdeaSplit.Shared.Sync;

namespace IdeaSplit.Tests;

public class RoutineTests
{
    private readonly LocalStorageProjectStore _store = new(new FakeLocalStorage());
    private static readonly DateTime Monday = new(2026, 11, 16);
    private const int MonWedFri = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Wednesday) | (1 << (int)DayOfWeek.Friday);

    private Task<List<TaskItem>> TasksOnAsync(DateTime date) => _store.GetProjectsAsync().ContinueWith(projects => projects.Result
        .Where(project => project.ScheduleType == TaskScheduleType.DayWeek && project.ScheduleDate == date)
        .SelectMany(project => project.Tasks).ToList());

    [Fact]
    public void Every_device_works_out_the_same_task_id_for_a_day()
    {
        var routine = new Routine();
        Assert.Equal(routine.TaskIdFor(Monday), new Routine { Id = routine.Id }.TaskIdFor(Monday));
        Assert.NotEqual(routine.TaskIdFor(Monday), routine.TaskIdFor(Monday.AddDays(1)));
    }

    [Fact]
    public async Task A_due_routine_adds_its_task_once()
    {
        await _store.SaveRoutineAsync(new Routine { Title = "Gym", Days = MonWedFri, GeneratedThrough = Monday.AddDays(-1) });

        Assert.True(await _store.AddDueRoutineTasksAsync(Monday));
        Assert.False(await _store.AddDueRoutineTasksAsync(Monday));

        Assert.Equal(["Gym"], (await TasksOnAsync(Monday)).Select(task => task.Title));
    }

    [Fact]
    public async Task Days_it_does_not_repeat_on_get_nothing()
    {
        await _store.SaveRoutineAsync(new Routine { Title = "Gym", Days = MonWedFri, GeneratedThrough = Monday });

        Assert.False(await _store.AddDueRoutineTasksAsync(Monday.AddDays(1)));
        Assert.Empty(await TasksOnAsync(Monday.AddDays(1)));
    }

    [Fact]
    public async Task A_deleted_task_is_not_added_again_the_same_day()
    {
        await _store.SaveRoutineAsync(new Routine { Title = "Gym", Days = Routine.EveryDay, GeneratedThrough = Monday.AddDays(-1) });
        await _store.AddDueRoutineTasksAsync(Monday);
        var day = (await _store.GetProjectsAsync()).Single();
        day.Tasks.Clear();
        await _store.SaveProjectAsync(day);

        Assert.False(await _store.AddDueRoutineTasksAsync(Monday));
        Assert.Empty(await TasksOnAsync(Monday));
    }

    [Fact]
    public async Task Missed_days_are_not_filled_in()
    {
        await _store.SaveRoutineAsync(new Routine { Title = "Gym", Days = Routine.EveryDay, GeneratedThrough = Monday.AddDays(-10) });

        await _store.AddDueRoutineTasksAsync(Monday);

        Assert.Single(await _store.GetProjectsAsync());
    }

    [Fact]
    public async Task Repeating_a_task_makes_it_that_days_instance_and_keeps_its_subtasks()
    {
        var day = new Project { ScheduleType = TaskScheduleType.DayWeek, ScheduleDate = Monday };
        var task = new TaskItem { ProjectId = day.Id, Title = "Gym" };
        day.Tasks.Add(task);
        day.Tasks.Add(new TaskItem { ProjectId = day.Id, ParentTaskId = task.Id, Title = "Stretch" });
        await _store.SaveProjectAsync(day);

        var routine = (await _store.RepeatTaskAsync(day.Id, task.Id, MonWedFri))!;

        var tasks = await TasksOnAsync(Monday);
        var instance = Assert.Single(tasks, candidate => candidate.ParentTaskId is null);
        Assert.Equal(routine.TaskIdFor(Monday), instance.Id);
        Assert.Equal(instance.Id, tasks.Single(candidate => candidate.Title == "Stretch").ParentTaskId);
        Assert.False(await _store.AddDueRoutineTasksAsync(Monday));
        Assert.Contains((await _store.GetPendingAsync()).Routines, pending => pending.Id == routine.Id);
    }

    [Fact]
    public async Task A_routine_from_the_server_never_moves_its_progress_backwards()
    {
        var routine = new Routine { Title = "Gym", Days = Routine.EveryDay, GeneratedThrough = Monday };
        await _store.SaveRoutineAsync(routine);
        var row = RoutineRow.From(routine, Guid.NewGuid());
        row.GeneratedThrough = "2026-11-01";
        row.Title = "Gym (renamed)";
        row.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        await _store.ApplyRemoteRoutinesAsync([row], complete: true);

        var merged = Assert.Single(await _store.GetRoutinesAsync());
        Assert.Equal("Gym (renamed)", merged.Title);
        Assert.Equal(Monday, merged.GeneratedThrough);
    }

    [Fact]
    public async Task Deleting_a_routine_is_recorded_for_upload()
    {
        var routine = new Routine { Title = "Gym", Days = Routine.EveryDay };
        await _store.SaveRoutineAsync(routine);

        await _store.DeleteRoutineAsync(routine.Id);

        Assert.Empty(await _store.GetRoutinesAsync());
        Assert.Contains((await _store.GetPendingAsync()).Tombstones, tombstone => tombstone.Table == Tombstone.Routines && tombstone.Id == routine.Id);
    }
}
