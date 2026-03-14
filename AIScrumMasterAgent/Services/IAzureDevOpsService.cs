using AIScrumMasterAgent.Models;

namespace AIScrumMasterAgent.Services;

public interface IAzureDevOpsService
{
    Task<WorkItem> GetWorkItemAsync(int id);
    Task<WorkItem> CreateWorkItemAsync(string type, CreateWorkItemRequest request);
    Task UpdateWorkItemDescriptionAsync(int id, string newDescription);
    Task AddChildLinkAsync(int parentId, int childId);
    Task<List<RepoInfo>> ListReposAsync();
    Task<List<string>> GetFolderTreeAsync(string repoId, string folderPath, int depth = 3);
    Task<string?> GetFileContentAsync(string repoId, string filePath);
    Task<List<WorkItem>> GetCurrentSprintItemsAsync();
}
