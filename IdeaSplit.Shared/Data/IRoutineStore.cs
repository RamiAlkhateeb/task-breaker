using IdeaSplit.Shared.Models;

namespace IdeaSplit.Shared.Data;

/// <summary>Repeating tasks: rules that add a task to chosen days of the week.</summary>
public interface IRoutineStore
{
    Task<List<Routine>> GetRoutinesAsync();
    Task SaveRoutineAsync(Routine routine);
    Task DeleteRoutineAsync(Guid routineId);

    /// <summary>
    /// Makes a task repeat on <paramref name="days"/>. The task becomes the routine's task for its own day,
    /// so that day doesn't get a second copy.
    /// </summary>
    Task<Routine?> RepeatTaskAsync(Guid projectId, Guid taskId, int days);

    /// <summary>Adds today's task for each routine due today that hasn't added it yet. Returns true if any were added.</summary>
    Task<bool> AddDueRoutineTasksAsync(DateTime today);
}
