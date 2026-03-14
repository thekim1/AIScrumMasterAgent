using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;

namespace AIScrumMasterAgent.Tests;

public class SprintPlanParserTests
{
    private readonly SprintPlanParser _parser = new();

    [Fact]
    public void Parse_SkipsItemsWithExistingTicketNumbers()
    {
        string description = """
            Features/Stories and Todos:
            Planned
            * #16962 Already has ticket
            * No ticket yet
            """;

        List<SprintPlanItem> items = _parser.Parse(description);

        Assert.Single(items);
        Assert.Equal("No ticket yet", items[0].Text);
    }

    [Fact]
    public void Parse_DetectsMeetingItems()
    {
        string description = """
            Features/Stories and Todos:
            Planned
            * Boka möte med David
            """;

        List<SprintPlanItem> items = _parser.Parse(description);

        Assert.Single(items);
        Assert.Equal(ItemKind.Meeting, items[0].Kind);
    }

    [Fact]
    public void Parse_DetectsInvestigationItems()
    {
        string description = """
            Features/Stories and Todos:
            Planned
            * Undersök hur API fungerar
            """;

        List<SprintPlanItem> items = _parser.Parse(description);

        Assert.Single(items);
        Assert.Equal(ItemKind.Investigation, items[0].Kind);
    }

    [Fact]
    public void Parse_CapturesParentFeature()
    {
        string description = """
            Features/Stories and Todos:
            Planned
            * Eplattformen 2.0
               * Formatera all kod
            """;

        List<SprintPlanItem> items = _parser.Parse(description);

        SprintPlanItem child = items.First(i => i.Text == "Formatera all kod");
        Assert.Equal("Eplattformen 2.0", child.ParentFeature);
    }

    [Fact]
    public void Parse_StopsAtNextSection()
    {
        string description = """
            Features/Stories and Todos:
            Planned
            * Todo item 1
            Another Section:
            * Should be ignored
            """;

        List<SprintPlanItem> items = _parser.Parse(description);

        Assert.Single(items);
        Assert.Equal("Todo item 1", items[0].Text);
    }

    [Fact]
    public void Parse_IgnoresItemsOutsideSection()
    {
        string description = """
            Some Other Section:
            * Should be ignored
            Features/Stories and Todos:
            * Valid item
            """;

        List<SprintPlanItem> items = _parser.Parse(description);

        Assert.Single(items);
        Assert.Equal("Valid item", items[0].Text);
    }
}
