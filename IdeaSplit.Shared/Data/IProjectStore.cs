using IdeaSplit.Shared.Models;

namespace IdeaSplit.Shared.Data;

public interface IProjectStore
{
    Task<List<Project>> GetProjectsAsync();
    Task<Project?> GetProjectAsync(int projectId);
    Task SaveProjectAsync(Project project);
    Task DeleteProjectAsync(int projectId);
    Task TogglePinAsync(int projectId);
}
