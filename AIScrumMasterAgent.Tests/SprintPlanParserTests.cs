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

    [Fact]
    public void Parse_RealWorldAzureDevOpsHtml_ReturnsExpectedItems()
    {
        const string html = """
            <p style="box-sizing:border-box;margin:0cm 0cm 8pt;font-size:12pt;font-family:Aptos, sans-serif;"> </p><div><b>Retropoints</b>:&nbsp; </div><div>(How do we improve) </div><div><ul><li>&lt;@someone&gt; &lt;<span style="display:inline !important;">Actionpoint&gt;</span> </li> </ul> </div><div><br> </div><div><b>Burning</b>!!&#128293;: </div><div>(Needs fixing; drop all else) </div><div><ul><li>Gallring Multi-leg </li> </ul> </div><div><br> </div><div><b>Current Epics</b>:&nbsp; </div><div>(The bigger picture) </div><div><ul><li>&lt;Epic 1&gt; </li> </ul> </div><div><br> </div><div><b>Sprintgoal</b>:&nbsp; </div><div><ul><li>Fastighetssök till prod </li> </ul> </div><div><br> </div><div><b>Features/Stories and Todos</b>:<br> </div><div>(plan and refine here for overview, then create tickets; Also plan relevant meetings and investigations): </div><div><b>Planned:</b> </div><div><ul><li style="box-sizing:border-box;">Skolmat </li><ul style="box-sizing:border-box;padding:0px 0px 0px 40px;"><li style="box-sizing:border-box;">Ai genereringarna visar alltid ca 20-30% högre kolhyratvärde än väntat<br> </li><li style="box-sizing:border-box;">Skapa jobb som körs och uppdaterar så alla åldrar har beräkningar för minst 3 veckor framåt om det inte finns i databasen redan<br> </li><li style="box-sizing:border-box;">Städning </li><ul style="box-sizing:border-box;padding:0px 0px 0px 40px;"><li style="box-sizing:border-box;">kolla så det inte finns massa varningar </li><li style="box-sizing:border-box;">Se så det inte finns onödiga ai dokument i rooten (implementationsplaner) </li><li style="box-sizing:border-box;">Uppdatera readme.md </li> </ul> </ul><ul> </ul><li>Kimvallen.se </li><ul><li>Uppdatera mailkit så säkerhetshål blir ordnat </li><li>Kolla så vi kör react senaste version och inte blazor som frontend </li> </ul><li>SparvagenCal </li><ul><li style="box-sizing:border-box;">Ordna admindel som kan slå på/av registrering istället för att behöva deploya om ändringar i appsettings. </li> </ul><li style="box-sizing:border-box;">.Net 10 </li><ul><li style="box-sizing:border-box;">Uppdatera Repos som ej kör .net 10 till .net10 </li> </ul><ul> </ul> </ul><ul><ul> </ul> </ul> </div><div><b></b> </div><div><b>Side:</b> </div><div><br><ul><li style="box-sizing:border-box;">Undersök ifall det är möjligt att ta reda på vilken källkod som är deployad på legacy-system<span style="box-sizing:border-box;">&nbsp;</span> </li> </ul> </div><div><b>Further:</b> </div><div><ul><ul> </ul> </ul> </div><div><br> </div><div><b>Known Risks</b>, Open Questions and Known Unknowns:<br> </div><div>(things to keep an eye on) </div><div><br> </div><div><br> </div><div><b>Other:</b> </div><div><ul><li>Semesterplanering senast 31/3 - (Vi gör en &quot;support-dashboard&quot;?)<br> </li> </ul> </div><div><br> </div><div><br> </div><div><br> </div><div>Progress-Icons </div><div><span style="display:inline !important;">✅ done</span> </div><div><span style="display:inline !important;"><span style="display:inline !important;">☑️ implementation&nbsp;done<br><span style="display:inline !important;">&#128284; soon</span></span><br></span> </div><div>&#127939;‍➡️ implementation started<br> </div><div>⏭️ deferred<br> </div><div>&#128125; ??? </div><br> 
            """;

        List<SprintPlanItem> items = _parser.Parse(html);

        // 14 items from Planned + 1 from Side + 0 from Further (empty) — stops at Known Risks
        Assert.Equal(15, items.Count);

        // Top-level items have no parent
        Assert.Contains(items, i => i.Text == "Skolmat" && i.ParentFeature == null);
        Assert.Contains(items, i => i.Text == "Kimvallen.se" && i.ParentFeature == null);
        Assert.Contains(items, i => i.Text == "SparvagenCal" && i.ParentFeature == null);
        Assert.Contains(items, i => i.Text == ".Net 10" && i.ParentFeature == null);

        // Nested items (depth 2) have correct parents
        Assert.Contains(items, i =>
            i.Text == "Ai genereringarna visar alltid ca 20-30% högre kolhyratvärde än väntat" &&
            i.ParentFeature == "Skolmat");
        Assert.Contains(items, i =>
            i.Text == "Städning" && i.ParentFeature == "Skolmat");
        Assert.Contains(items, i =>
            i.Text == "Uppdatera mailkit så säkerhetshål blir ordnat" &&
            i.ParentFeature == "Kimvallen.se");
        Assert.Contains(items, i =>
            i.Text == "Uppdatera Repos som ej kör .net 10 till .net10" &&
            i.ParentFeature == ".Net 10");

        // Deep items (depth 3) have correct parents
        Assert.Contains(items, i =>
            i.Text == "kolla så det inte finns massa varningar" &&
            i.ParentFeature == "Städning");
        Assert.Contains(items, i =>
            i.Text == "Uppdatera readme.md" && i.ParentFeature == "Städning");

        // Investigation detection
        Assert.Contains(items, i =>
            i.Text == "kolla så det inte finns massa varningar" &&
            i.Kind == ItemKind.Investigation);
        Assert.Contains(items, i =>
            i.Text.StartsWith("Undersök ifall") &&
            i.Kind == ItemKind.Investigation);

        // Items after Known Risks are excluded
        Assert.DoesNotContain(items, i => i.Text.Contains("Semesterplanering"));
        Assert.DoesNotContain(items, i => i.Text.Contains("Risk"));
    }
}
