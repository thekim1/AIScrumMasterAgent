using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;
using Moq;
using Shouldly;

namespace AIScrumMasterAgent.Tests;

public class TicketEnricherTests
{
    private readonly Mock<IAzureDevOpsService> _devOpsMock = new();
    private readonly Mock<IClaudeService> _claudeMock = new();
    private readonly AppConfig _config = new()
    {
        Agent = new AgentConfig { SprintPlanTicketType = "User Story" }
    };

    private TicketEnricher CreateEnricher() =>
        new(_devOpsMock.Object, _claudeMock.Object, _config);

    private static GeneratedTicket MakeGeneratedTicket(string title = "Generated Title") =>
        new(title, "Description", ["AC1"], "4-8h", ["## Plan"], "Implementation", ["Tag1"]);

    [Fact]
    public async Task EnrichAsync_SkipsItemWithExistingTicketId()
    {
        SprintPlanItem item = new("Already has ticket", null, 12345, ItemKind.Implementation);

        WorkItemResult? result = await CreateEnricher().EnrichAsync(99, item, null);

        result.ShouldBeNull();
        _claudeMock.VerifyNoOtherCalls();
        _devOpsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrichAsync_PassesRepoContextToClaude()
    {
        SprintPlanItem item = new("Formatera all kod", "Eplattformen 2.0", null, ItemKind.Implementation);
        RepoContext context = new("my-repo", "src/MyApp", "src/MyApp/\n  Controllers/", "# Context");

        _claudeMock.Setup(c => c.GenerateTicketAsync(item, context))
            .ReturnsAsync(MakeGeneratedTicket());
        _devOpsMock.Setup(d => d.CreateWorkItemAsync("User Story", It.IsAny<CreateWorkItemRequest>()))
            .ReturnsAsync(new WorkItem { Id = 100, Title = "Generated Title", Url = "http://example.com/100" });
        _devOpsMock.Setup(d => d.GetWorkItemAsync(1))
            .ReturnsAsync(new WorkItem { Id = 1, Description = "Formatera all kod" });

        await CreateEnricher().EnrichAsync(1, item, context);

        _claudeMock.Verify(c => c.GenerateTicketAsync(item, context), Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_UpdatesSprintPlanWithNewTicketId()
    {
        SprintPlanItem item = new("Formatera all kod", null, null, ItemKind.Implementation);

        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket());
        _devOpsMock.Setup(d => d.CreateWorkItemAsync("User Story", It.IsAny<CreateWorkItemRequest>()))
            .ReturnsAsync(new WorkItem { Id = 200, Title = "Generated Title", Url = "http://example.com/200" });
        _devOpsMock.Setup(d => d.GetWorkItemAsync(10))
            .ReturnsAsync(new WorkItem { Id = 10, Description = "Sprint plan with Formatera all kod in it" });

        WorkItemResult? result = await CreateEnricher().EnrichAsync(10, item, null);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(200);
        _devOpsMock.Verify(
            d => d.UpdateWorkItemDescriptionAsync(10, It.Is<string>(s => s.Contains("#200"))),
            Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_ContinuesWithoutAgentContext()
    {
        SprintPlanItem item = new("Some task", null, null, ItemKind.Implementation);
        RepoContext contextWithoutAgentFile = new("my-repo", "src/MyApp", "src/MyApp/\n  Controllers/", null);

        _claudeMock.Setup(c => c.GenerateTicketAsync(item, contextWithoutAgentFile))
            .ReturnsAsync(MakeGeneratedTicket());
        _devOpsMock.Setup(d => d.CreateWorkItemAsync("User Story", It.IsAny<CreateWorkItemRequest>()))
            .ReturnsAsync(new WorkItem { Id = 300, Title = "Generated Title", Url = "http://example.com/300" });
        _devOpsMock.Setup(d => d.GetWorkItemAsync(5))
            .ReturnsAsync(new WorkItem { Id = 5, Description = "Some task in sprint plan" });

        WorkItemResult? result = await CreateEnricher().EnrichAsync(5, item, contextWithoutAgentFile);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(300);
    }
}
