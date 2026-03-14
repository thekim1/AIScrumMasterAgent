using AIScrumMasterAgent.Models;

namespace AIScrumMasterAgent.Services;

public interface IRepoContextFetcher
{
    Task<RepoContext> FetchAsync(string repoId, string repoName, string solutionFolder);
}
