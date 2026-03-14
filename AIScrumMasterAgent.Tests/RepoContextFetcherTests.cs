using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;
using Moq;
using Shouldly;

namespace AIScrumMasterAgent.Tests;

public class RepoContextFetcherTests
{
    private readonly Mock<IAzureDevOpsService> _devOpsMock = new();

    [Fact]
    public async Task FetchAsync_ReturnsBothFolderTreeAndAgentContext()
    {
        string repoId = "repo-1";
        string repoName = "ume-rg-eplatform";
        string solutionFolder = "src/Ume-App-eService";
        string agentContextContent = "# Agent Context\n## Stack\n- Backend: .NET 10";
        List<string> paths =
        [
            "src/Ume-App-eService/Controllers",
            "src/Ume-App-eService/Services"
        ];

        _devOpsMock.Setup(s => s.GetFolderTreeAsync(repoId, solutionFolder, 3))
            .ReturnsAsync(paths);
        _devOpsMock.Setup(s => s.GetFileContentAsync(repoId, $"{solutionFolder}/AGENT_CONTEXT.md"))
            .ReturnsAsync(agentContextContent);

        RepoContextFetcher fetcher = new(_devOpsMock.Object);
        RepoContext context = await fetcher.FetchAsync(repoId, repoName, solutionFolder);

        context.RepoName.ShouldBe(repoName);
        context.SolutionFolder.ShouldBe(solutionFolder);
        context.AgentContextContent.ShouldBe(agentContextContent);
        context.FolderTree.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FetchAsync_HandlesAbsentAgentContext()
    {
        string repoId = "repo-1";
        string repoName = "ume-rg-eplatform";
        string solutionFolder = "src/Ume-App-eService";

        _devOpsMock.Setup(s => s.GetFolderTreeAsync(repoId, solutionFolder, 3))
            .ReturnsAsync([]);
        _devOpsMock.Setup(s => s.GetFileContentAsync(repoId, $"{solutionFolder}/AGENT_CONTEXT.md"))
            .ReturnsAsync((string?)null);

        RepoContextFetcher fetcher = new(_devOpsMock.Object);
        RepoContext context = await fetcher.FetchAsync(repoId, repoName, solutionFolder);

        context.AgentContextContent.ShouldBeNull();
    }

    [Fact]
    public async Task FetchAsync_FormatsFolderTreeCorrectly()
    {
        string repoId = "repo-1";
        string solutionFolder = "src/MyApp";
        List<string> paths =
        [
            "src/MyApp/Controllers",
            "src/MyApp/Controllers/HomeController.cs",
            "src/MyApp/Services",
            "src/MyApp/Services/MyService.cs"
        ];

        _devOpsMock.Setup(s => s.GetFolderTreeAsync(repoId, solutionFolder, 3))
            .ReturnsAsync(paths);
        _devOpsMock.Setup(s => s.GetFileContentAsync(repoId, $"{solutionFolder}/AGENT_CONTEXT.md"))
            .ReturnsAsync((string?)null);

        RepoContextFetcher fetcher = new(_devOpsMock.Object);
        RepoContext context = await fetcher.FetchAsync(repoId, "my-repo", solutionFolder);

        context.FolderTree.ShouldContain("src/MyApp/");
        context.FolderTree.ShouldContain("Controllers/");
        context.FolderTree.ShouldContain("  HomeController.cs");
        context.FolderTree.ShouldContain("Services/");
        context.FolderTree.ShouldContain("  MyService.cs");
    }
}
