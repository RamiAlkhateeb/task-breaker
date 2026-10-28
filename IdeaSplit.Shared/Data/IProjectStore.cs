using IdeaSplit.Shared.Models;

namespace IdeaSplit.Shared.Data;

public interface IProjectStore
{
    Task<List<Project>> GetProjectsAsync();
    Task<Project?> GetProjectAsync(Guid projectId);
    Task SaveProjectAsync(Project project);
    Task DeleteProjectAsync(Guid projectId);
    Task TogglePinAsync(Guid projectId);
    Task SetArchivedAsync(Guid projectId, bool archived);
    Task<Project> GetOrCreateDayProjectAsync(DateTime date);
}
