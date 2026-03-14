namespace AIScrumMasterAgent.Models;

public record RepoContext(
    string RepoName,
    string SolutionFolder,
    string FolderTree,
    string? AgentContextContent);
