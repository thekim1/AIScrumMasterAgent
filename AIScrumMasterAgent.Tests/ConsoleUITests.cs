using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;
using AIScrumMasterAgent.UI;
using Moq;
using Shouldly;
using System.Text.Json;

namespace AIScrumMasterAgent.Tests;

public class ConsoleUITests : IDisposable
{
    private readonly Mock<IAzureDevOpsService> _devOpsMock = new();
    private readonly Mock<ISprintPlanParser> _parserMock = new();
    private readonly Mock<IRepoContextFetcher> _contextFetcherMock = new();
    private readonly Mock<ITicketEnricher> _enricherMock = new();
    private readonly Mock<IClaudeService> _claudeMock = new();

    private readonly TextReader _originalIn = Console.In;
    private readonly TextWriter _originalOut = Console.Out;

    public void Dispose()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
        GC.SuppressFinalize(this);
    }

    private ConsoleUI CreateUI() =>
        new(_devOpsMock.Object, _parserMock.Object, _contextFetcherMock.Object,
            _enricherMock.Object, _claudeMock.Object);

    private static GeneratedTicket MakeGeneratedTicket(string title = "My Generated Ticket") =>
        new(title, "A description", ["AC1", "AC2"], "4-8h", "Step 1\nStep 2", "Implementation", ["tag1"]);

    /// <summary>
    /// Sets up the mocks needed for the basic RunAsync flow:
    /// ticket fetch, empty repo list, and parser returning the supplied items.
    /// </summary>
    private void SetupBasicMocks(List<SprintPlanItem> items)
    {
        _devOpsMock.Setup(d => d.GetWorkItemAsync(42))
            .ReturnsAsync(new WorkItem { Id = 42, Title = "Sprint Plan", Description = "sprint body" });
        _devOpsMock.Setup(d => d.ListReposAsync())
            .ReturnsAsync([]);
        _parserMock.Setup(p => p.Parse(It.IsAny<string>()))
            .Returns(items);
    }

    /// <summary>
    /// Feeds the given lines as simulated user input to Console.In.
    /// With an empty repo list, RunAsync only reads: ticket ID, then one action per item.
    /// </summary>
    private static void SetInput(params string[] lines) =>
        Console.SetIn(new StringReader(string.Join(Environment.NewLine, lines)));

    /// <summary>Redirects Console.Out so tests can inspect printed output.</summary>
    private static StringWriter CaptureOutput()
    {
        StringWriter writer = new();
        Console.SetOut(writer);
        return writer;
    }

    // -------------------------------------------------------------------------
    // Dry-run mode tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_DryRun_PrintsDryRunBanner()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        SetInput("42", "S");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        output.ToString().ShouldContain("[DRY RUN]");
    }

    [Fact]
    public async Task RunAsync_DryRun_CallsClaudeServiceDirectly()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket());
        SetInput("42", "C");
        CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        _claudeMock.Verify(c => c.GenerateTicketAsync(item, null), Times.Once);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotCallEnricher()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket());
        SetInput("42", "C");
        CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        _enricherMock.Verify(
            e => e.EnrichAsync(It.IsAny<int>(), It.IsAny<SprintPlanItem>(), It.IsAny<RepoContext>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_DryRun_PrintsGeneratedTicketTitle()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket("My Generated Ticket"));
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        output.ToString().ShouldContain("My Generated Ticket");
    }

    [Fact]
    public async Task RunAsync_DryRun_PrintsAllGeneratedTicketFields()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket("Feature X Title"));
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        string printed = output.ToString();
        printed.ShouldContain("Feature X Title");
        printed.ShouldContain("A description");
        printed.ShouldContain("AC1");
        printed.ShouldContain("4-8h");
        printed.ShouldContain("tag1");
    }

    [Fact]
    public async Task RunAsync_DryRun_SummaryShowsGeneratedCount()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket());
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        output.ToString().ShouldContain("Generated (dry run): 1 ticket(s)");
    }

    [Fact]
    public async Task RunAsync_DryRun_SummaryListsGeneratedTitles()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket("The Generated Title"));
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true);

        output.ToString().ShouldContain("The Generated Title");
    }

    // -------------------------------------------------------------------------
    // Normal-mode tests (verifying existing behaviour is unchanged)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NormalMode_DoesNotPrintDryRunBanner()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _enricherMock.Setup(e => e.EnrichAsync(42, item, null))
            .ReturnsAsync(new WorkItemResult(100, "Implement feature X", "http://example.com/100"));
        SetInput("42", "S");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        output.ToString().ShouldNotContain("[DRY RUN]");
    }

    [Fact]
    public async Task RunAsync_NormalMode_CallsEnricher()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _enricherMock.Setup(e => e.EnrichAsync(42, item, null))
            .ReturnsAsync(new WorkItemResult(100, "Generated Title", "http://example.com/100"));
        SetInput("42", "C");
        CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        _enricherMock.Verify(e => e.EnrichAsync(42, item, null), Times.Once);
    }

    [Fact]
    public async Task RunAsync_NormalMode_DoesNotCallClaudeServiceDirectly()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _enricherMock.Setup(e => e.EnrichAsync(42, item, null))
            .ReturnsAsync(new WorkItemResult(100, "Generated Title", "http://example.com/100"));
        SetInput("42", "C");
        CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        _claudeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_NormalMode_SummaryShowsCreatedCount()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _enricherMock.Setup(e => e.EnrichAsync(42, item, null))
            .ReturnsAsync(new WorkItemResult(100, "Generated Title", "http://example.com/100"));
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        output.ToString().ShouldContain("Created: 1 ticket(s)");
    }

    [Fact]
    public async Task RunAsync_NormalMode_SummaryListsNewTicketIds()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _enricherMock.Setup(e => e.EnrichAsync(42, item, null))
            .ReturnsAsync(new WorkItemResult(100, "Generated Title", "http://example.com/100"));
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        output.ToString().ShouldContain("#100");
    }

    [Fact]
    public async Task RunAsync_SkipAction_IncrementsSkippedCount()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        SetInput("42", "S");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        output.ToString().ShouldContain("Skipped: 1 item(s)");
    }

    [Fact]
    public async Task RunAsync_ExcludeAction_IncrementsExcludedCount()
    {
        SprintPlanItem item = new("Boka möte med David", null, null, ItemKind.Meeting);
        SetupBasicMocks([item]);
        SetInput("42", "E");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        output.ToString().ShouldContain("Excluded: 1 item(s)");
    }

    [Fact]
    public async Task RunAsync_NoItems_PrintsNoItemsMessage()
    {
        SetupBasicMocks([]);
        SetInput("42");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false);

        output.ToString().ShouldContain("No unlinked todo items found");
    }

    // -------------------------------------------------------------------------
    // Dry-run output file tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_CreatesJsonFile()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket());
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C");
        CaptureOutput();

        try
        {
            await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

            File.Exists(tempFile).ShouldBeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_JsonContainsItemTextAndTitle()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket("Expected Title"));
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C");
        CaptureOutput();

        try
        {
            await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

            using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));
            JsonElement root = doc.RootElement;
            root.GetArrayLength().ShouldBe(1);
            root[0].GetProperty("itemText").GetString().ShouldBe("Implement feature X");
            root[0].GetProperty("ticket").GetProperty("title").GetString().ShouldBe("Expected Title");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_JsonContainsAllTicketFields()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket("Title"));
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C");
        CaptureOutput();

        try
        {
            await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

            using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));
            JsonElement ticket = doc.RootElement[0].GetProperty("ticket");
            ticket.GetProperty("title").GetString().ShouldBe("Title");
            ticket.GetProperty("description").GetString().ShouldBe("A description");
            ticket.GetProperty("estimatedHours").GetString().ShouldBe("4-8h");
            ticket.GetProperty("detectedType").GetString().ShouldBe("Implementation");
            ticket.GetProperty("acceptanceCriteria")[0].GetString().ShouldBe("AC1");
            ticket.GetProperty("suggestedTags")[0].GetString().ShouldBe("tag1");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_MultipleItemsAllSavedToFile()
    {
        SprintPlanItem item1 = new("Task one", null, null, ItemKind.Implementation);
        SprintPlanItem item2 = new("Task two", null, null, ItemKind.Implementation);
        SetupBasicMocks([item1, item2]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item1, null)).ReturnsAsync(MakeGeneratedTicket("Title One"));
        _claudeMock.Setup(c => c.GenerateTicketAsync(item2, null)).ReturnsAsync(MakeGeneratedTicket("Title Two"));
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C", "C");
        CaptureOutput();

        try
        {
            await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

            using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));
            doc.RootElement.GetArrayLength().ShouldBe(2);
            doc.RootElement[0].GetProperty("itemText").GetString().ShouldBe("Task one");
            doc.RootElement[1].GetProperty("itemText").GetString().ShouldBe("Task two");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_SkippedItemsNotIncludedInFile()
    {
        SprintPlanItem item1 = new("Task one", null, null, ItemKind.Implementation);
        SprintPlanItem item2 = new("Task two", null, null, ItemKind.Implementation);
        SetupBasicMocks([item1, item2]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item1, null)).ReturnsAsync(MakeGeneratedTicket("Title One"));
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C", "S"); // first: create, second: skip
        CaptureOutput();

        try
        {
            await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

            using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));
            doc.RootElement.GetArrayLength().ShouldBe(1);
            doc.RootElement[0].GetProperty("itemText").GetString().ShouldBe("Task one");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_PrintsSaveConfirmation()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _claudeMock.Setup(c => c.GenerateTicketAsync(item, null))
            .ReturnsAsync(MakeGeneratedTicket());
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C");
        StringWriter output = CaptureOutput();

        try
        {
            await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

            output.ToString().ShouldContain($"Saved 1 generated ticket(s) to: {tempFile}");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_WithOutput_OutputPathShownInBanner()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "S");
        StringWriter output = CaptureOutput();

        await CreateUI().RunAsync(isDryRun: true, dryRunOutputPath: tempFile);

        output.ToString().ShouldContain(tempFile);
    }

    [Fact]
    public async Task RunAsync_NormalMode_WithOutputPath_DoesNotWriteFile()
    {
        SprintPlanItem item = new("Implement feature X", null, null, ItemKind.Implementation);
        SetupBasicMocks([item]);
        _enricherMock.Setup(e => e.EnrichAsync(42, item, null))
            .ReturnsAsync(new WorkItemResult(100, "Title", "http://example.com/100"));
        string tempFile = Path.Combine(Path.GetTempPath(), $"dryrun-{Guid.NewGuid()}.json");
        SetInput("42", "C");
        CaptureOutput();

        await CreateUI().RunAsync(isDryRun: false, dryRunOutputPath: tempFile);

        File.Exists(tempFile).ShouldBeFalse();
    }
}
