using AIScrumMasterAgent.Models;
using System.Text;

namespace AIScrumMasterAgent.Services;

public class RepoContextFetcher(IAzureDevOpsService devOpsService) : IRepoContextFetcher
{
    private readonly IAzureDevOpsService _devOpsService = devOpsService;

    public async Task<RepoContext> FetchAsync(string repoId, string repoName, string solutionFolder)
    {
        string normalizedFolder = (solutionFolder ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedFolder))
        {
            normalizedFolder = "/";
        }

        List<string> paths = await _devOpsService.GetFolderTreeAsync(repoId, normalizedFolder, depth: 3);
        string folderTree = FormatFolderTree(paths, normalizedFolder);

        string agentContextPath = normalizedFolder == "/" 
            ? "/AGENT_CONTEXT.md" 
            : $"{normalizedFolder}/AGENT_CONTEXT.md";
        string? agentContextContent = await _devOpsService.GetFileContentAsync(repoId, agentContextPath);

        return new RepoContext(repoName, normalizedFolder, folderTree, agentContextContent);
    }

    internal static string FormatFolderTree(IEnumerable<string> paths, string rootFolder)
    {
        StringBuilder sb = new();
        string normalizedRoot = (rootFolder ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedRoot))
        {
            normalizedRoot = "/";
        }

        sb.AppendLine(normalizedRoot == "/" ? "/" : $"{normalizedRoot}/");

        foreach (string? path in paths.OrderBy(p => p))
        {
            string relative = path.StartsWith(normalizedRoot)
                ? path[normalizedRoot.Length..].TrimStart('/')
                : path.TrimStart('/');

            if (string.IsNullOrEmpty(relative))
                continue;

            string[] segments = relative.Split('/');
            int depth = segments.Length - 1;
            string indent = new(' ', depth * 2);
            string name = segments[^1];

            // Determine if it's a folder by checking if there are deeper paths
            bool isFolder = paths.Any(p =>
                p != path && p.StartsWith(path.TrimEnd('/') + "/"));

            sb.AppendLine($"{indent}{name}{(isFolder ? "/" : "")}");
        }

        return sb.ToString().TrimEnd();
    }
}
