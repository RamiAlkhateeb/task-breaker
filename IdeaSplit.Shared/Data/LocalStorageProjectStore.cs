using Blazored.LocalStorage;
using IdeaSplit.Shared.Models;

namespace IdeaSplit.Shared.Data;

public class LocalStorageProjectStore : IProjectStore
{
    private const string ProjectsKey = "ideasplit_projects";
    private readonly ILocalStorageService _storage;
    public LocalStorageProjectStore(ILocalStorageService storage) => _storage = storage;

    public async Task<List<Project>> GetProjectsAsync() =>
        (await _storage.GetItemAsync<List<Project>>(ProjectsKey) ?? []).OrderByDescending(project => project.CreatedAt).ToList();
    public async Task<Project?> GetProjectAsync(int projectId) => (await GetProjectsAsync()).FirstOrDefault(project => project.Id == projectId);

    public async Task DeleteProjectAsync(int projectId)
    {
        var projects = await GetProjectsAsync();
        projects.RemoveAll(project => project.Id == projectId);
        await _storage.SetItemAsync(ProjectsKey, projects);
    }

    public async Task TogglePinAsync(int projectId)
    {
        var projects = await GetProjectsAsync();
        var project = projects.FirstOrDefault(existing => existing.Id == projectId);
        if (project is null) return;

        project.IsPinned = !project.IsPinned;
        await _storage.SetItemAsync(ProjectsKey, projects);
    }

    public async Task SetArchivedAsync(int projectId, bool archived)
    {
        var projects = await GetProjectsAsync();
        var project = projects.FirstOrDefault(existing => existing.Id == projectId);
        if (project is null) return;

        project.IsArchived = archived;
        await _storage.SetItemAsync(ProjectsKey, projects);
    }

    public async Task<Project> GetOrCreateDayProjectAsync(DateTime date)
    {
        var projects = await GetProjectsAsync();
        var existing = projects.FirstOrDefault(project =>
            project.ScheduleType == TaskScheduleType.DayWeek && project.ScheduleDate?.Date == date.Date);
        if (existing is not null)
        {
            if (existing.IsArchived)
            {
                existing.IsArchived = false;
                await _storage.SetItemAsync(ProjectsKey, projects);
            }
            return existing;
        }

        var created = new Project
        {
            Title = $"Tasks — {date:MMM d}",
            ScheduleType = TaskScheduleType.DayWeek,
            ScheduleDate = date.Date
        };
        await SaveProjectAsync(created);
        return created;
    }

    public async Task SaveProjectAsync(Project project)
    {
        var projects = await GetProjectsAsync();
        if (project.Id == 0)
        {
            project.Id = projects.Count == 0 ? 1 : projects.Max(existing => existing.Id) + 1;
            projects.Add(project);
        }
        else
        {
            var index = projects.FindIndex(existing => existing.Id == project.Id);
            if (index >= 0) projects[index] = project;
            else projects.Add(project);
        }
        var nextTaskId = NextTaskId(projects);
        foreach (var task in project.Tasks)
        {
            task.ProjectId = project.Id;
            if (task.Id == 0) task.Id = nextTaskId++;
        }
        await _storage.SetItemAsync(ProjectsKey, projects);
    }

    private static int NextTaskId(IEnumerable<Project> projects) =>
        projects.SelectMany(project => project.Tasks).Select(task => task.Id).DefaultIfEmpty().Max() + 1;
}
