using IdeaSplit.Shared.Models;

namespace IdeaSplit.Shared.Data;

public static class ProjectStoreExtensions
{
    /// <summary>
    /// Moves top-level <paramref name="tasks"/> (with their sub-tasks) from <paramref name="source"/> to the day
    /// project for <paramref name="date"/>, creating that day if needed. The source is archived once it has no
    /// open tasks left. Returns the day project the tasks now belong to.
    /// </summary>
    public static async Task<Project> MoveTasksToDayAsync(this IProjectStore store, Project source, IEnumerable<TaskItem> tasks, DateTime date)
    {
        var target = await store.GetOrCreateDayProjectAsync(date.Date);
        if (target.Id == source.Id) return target;

        var ids = tasks.Select(task => task.Id).ToHashSet();
        var moving = source.Tasks
            .Where(task => ids.Contains(task.Id) || (task.ParentTaskId is { } parent && ids.Contains(parent)))
            .ToList();
        if (moving.Count == 0) return target;

        source.Tasks.RemoveAll(moving.Contains);
        var next = target.Tasks.Count(task => task.ParentTaskId is null);
        foreach (var task in moving.Where(task => task.ParentTaskId is null).OrderBy(task => task.SortOrder))
            task.SortOrder = next++;
        target.Tasks.AddRange(moving);
        target.IsArchived = false;
        // The target is saved first so the store sees the tasks as moved, not deleted.
        await store.SaveProjectAsync(target);

        source.IsArchived = !source.Tasks.Any(task => task.ParentTaskId is null && !task.IsDone);
        await store.SaveProjectAsync(source);
        return target;
    }

    /// <summary>Archives a project when all its top-level tasks are done, and brings it back when one is reopened.</summary>
    public static void UpdateArchivedState(this Project project) =>
        project.IsArchived = project.TotalCount > 0 && project.DoneCount == project.TotalCount;
}
