using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIScrumMasterAgent.UI;

public class ConsoleUI(
    IAzureDevOpsService devOpsService,
    ISprintPlanParser parser,
    IRepoContextFetcher contextFetcher,
    ITicketEnricher enricher,
    IClaudeService claudeService)
{
    private readonly IAzureDevOpsService _devOpsService = devOpsService;
    private readonly ISprintPlanParser _parser = parser;
    private readonly IRepoContextFetcher _contextFetcher = contextFetcher;
    private readonly ITicketEnricher _enricher = enricher;
    private readonly IClaudeService _claudeService = claudeService;

    public async Task RunAsync(bool isDryRun = false, string? dryRunOutputPath = null)
    {
        Console.WriteLine("=== AI Scrum Master Agent POC ===");
        if (isDryRun)
        {
            Console.WriteLine();
            Console.WriteLine("*** [DRY RUN] — Claude will be called but no tickets will be written to Azure DevOps ***");
            if (dryRunOutputPath is not null)
                Console.WriteLine($"    Responses will be saved to: {dryRunOutputPath}");
        }
        Console.WriteLine();

        // Step 1: Get sprint plan ticket ID
        int ticketId = PromptInt("Enter sprint plan ticket ID: ");

        Console.Write($"Fetching sprint plan ticket #{ticketId}...");
        WorkItem sprintPlan;
        try
        {
            sprintPlan = await _devOpsService.GetWorkItemAsync(ticketId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError fetching ticket: {ex.Message}");
            return;
        }
        Console.WriteLine($"\nTitle: {sprintPlan.Title}");
        Console.WriteLine();

        // Step 2: Select repos
        Console.WriteLine("Fetching available repositories...");
        List<RepoInfo> repos;
        try
        {
            repos = await _devOpsService.ListReposAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching repositories: {ex.Message}");
            return;
        }

        if (repos.Count == 0)
        {
            Console.WriteLine("No repositories found. Proceeding without repo context.");
        }
        else
        {
            Console.WriteLine("Available repositories:");
            for (int i = 0; i < repos.Count; i++)
                Console.WriteLine($"  [{i + 1}] {repos[i].Name}");
            Console.WriteLine();
        }

        List<RepoInfo> selectedRepos = [];
        Dictionary<string, string> repoPaths = []; // repoId -> solutionFolder

        if (repos.Count > 0)
        {
            Console.Write("Select relevant repos for this sprint (comma-separated, e.g. 1,3), or press Enter to skip: ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (!string.IsNullOrEmpty(input))
            {
                foreach (string part in input.Split(','))
                {
                    if (int.TryParse(part.Trim(), out int idx) && idx >= 1 && idx <= repos.Count)
                        selectedRepos.Add(repos[idx - 1]);
                }
            }
        }

        // Step 3: Get solution folder for each selected repo
        foreach (RepoInfo repo in selectedRepos)
        {
            Console.Write($"Repo: {repo.Name}\nEnter solution folder path (e.g. src/MyApp), or press Enter to skip repo context: ");
            string folder = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            if (folder == "/")
            {
                Console.Write("Fetching context from the repo root can be expensive for large repositories.\nType '/' again to confirm, or press Enter to cancel: ");
                string confirm = Console.ReadLine()?.Trim() ?? "";
                if (confirm != "/")
                {
                    // User did not confirm root fetch; skip context for this repo
                    continue;
                }
            }

            repoPaths[repo.Id] = folder;
        }

        // Step 4: Fetch repo contexts
        List<RepoContext> repoContexts = [];
        if (selectedRepos.Count > 0)
        {
            Console.WriteLine("\nFetching repo context(s)...");
            foreach (RepoInfo repo in selectedRepos)
            {
                if (!repoPaths.TryGetValue(repo.Id, out string? folder))
                    continue;
                try
                {
                    string displayFolder = folder == "/" ? "" : folder;
                    string displayPath = string.IsNullOrEmpty(displayFolder)
                        ? repo.Name
                        : $"{repo.Name}/{displayFolder}";
                    Console.Write($"  {displayPath}...");
                    RepoContext ctx = await _contextFetcher.FetchAsync(repo.Id, repo.Name, folder);
                    repoContexts.Add(ctx);
                    Console.WriteLine(ctx.AgentContextContent is not null ? " ✓ (AGENT_CONTEXT.md found)" : " ✓ (no AGENT_CONTEXT.md)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" ✗ Failed: {ex.Message}");
                }
            }
        }

        // Step 5: Parse sprint plan items
        List<SprintPlanItem> items = _parser.Parse(sprintPlan.Description);
        if (items.Count == 0)
        {
            Console.WriteLine("\nNo unlinked todo items found in the sprint plan.");
            return;
        }

        Console.WriteLine($"\nFound {items.Count} unlinked todo item(s). Review each:\n");

        // Step 6: Review each item
        List<WorkItemResult> created = [];
        List<DryRunEntry> dryRunEntries = [];
        int skipped = 0;
        int excluded = 0;

        for (int i = 0; i < items.Count; i++)
        {
            SprintPlanItem item = items[i];
            string kindLabel = item.Kind switch
            {
                ItemKind.Meeting => "Meeting ⚠",
                ItemKind.Investigation => "Investigation",
                _ => "Implementation"
            };

            Console.WriteLine($"[{i + 1}/{items.Count}] \"{item.Text}\"");
            if (item.ParentFeature is not null)
                Console.WriteLine($"      Parent: {item.ParentFeature}");
            Console.WriteLine($"      Detected: {kindLabel}");
            Console.Write("      → (C)reate ticket / (S)kip / (E)xclude as meeting: ");

            string action = (Console.ReadLine() ?? "s").Trim().ToUpperInvariant();

            if (action == "C")
            {
                RepoContext? primaryContext = repoContexts.Count > 0 ? repoContexts[0] : null;
                Console.WriteLine($"Generating ticket for: \"{item.Text}\"");

                try
                {
                    if (primaryContext is not null)
                        Console.WriteLine($"  Fetching repo context from {primaryContext.RepoName}/{primaryContext.SolutionFolder}...");

                    Console.Write("  Calling Claude API...");

                    if (isDryRun)
                    {
                        GeneratedTicket ticket = await _claudeService.GenerateTicketAsync(item, primaryContext);
                        Console.WriteLine();
                        PrintDryRunTicket(ticket);
                        created.Add(new WorkItemResult(0, ticket.Title, string.Empty));
                        dryRunEntries.Add(new DryRunEntry(item.Text, ticket));
                    }
                    else
                    {
                        Console.Write(" Creating ticket in Azure DevOps...");

                        WorkItemResult? result = await _enricher.EnrichAsync(ticketId, item, primaryContext);

                        if (result is not null)
                        {
                            Console.WriteLine($"\n  ✓ Created #{result.Id} — \"{result.Title}\"");
                            Console.WriteLine("  ✓ Updated sprint plan ticket");
                            created.Add(result);
                        }
                        else
                        {
                            Console.WriteLine("\n  (skipped — already has a ticket number)");
                            skipped++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n  ✗ Error: {ex.Message}");
                    skipped++;
                }
            }
            else if (action == "E")
            {
                Console.WriteLine("  → Excluded (meeting/admin)");
                excluded++;
            }
            else
            {
                Console.WriteLine("  → Skipped");
                skipped++;
            }

            Console.WriteLine();
        }

        // Step 7: Summary
        Console.WriteLine("=== Done ===");
        Console.WriteLine($"{(isDryRun ? "Generated (dry run)" : "Created")}: {created.Count} ticket(s)");
        Console.WriteLine($"Skipped: {skipped} item(s)");
        Console.WriteLine($"Excluded: {excluded} item(s) (meeting/admin)");

        if (created.Count > 0)
        {
            if (isDryRun)
            {
                string titles = string.Join(", ", created.Select(r => $"\"{r.Title}\""));
                Console.WriteLine($"\nGenerated tickets: {titles}");
            }
            else
            {
                string ids = string.Join(", ", created.Select(r => $"#{r.Id}"));
                Console.WriteLine($"\nNew tickets: {ids}");
            }
        }

        if (isDryRun && dryRunOutputPath is not null && dryRunEntries.Count > 0)
        {
            string json = JsonSerializer.Serialize(dryRunEntries, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dryRunOutputPath, json);
            Console.WriteLine($"\nSaved {dryRunEntries.Count} generated ticket(s) to: {dryRunOutputPath}");
        }
    }

    private static void PrintDryRunTicket(GeneratedTicket ticket)
    {
        Console.WriteLine("  --- Generated Ticket (dry run) ---");
        Console.WriteLine($"  Title:       {ticket.Title}");
        Console.WriteLine($"  Type:        {ticket.DetectedType}");
        Console.WriteLine($"  Effort:      {ticket.EstimatedHours}");
        Console.WriteLine($"  Tags:        {string.Join(", ", ticket.SuggestedTags)}");
        Console.WriteLine($"  Description: {ticket.Description}");
        Console.WriteLine("  Acceptance Criteria:");
        foreach (string ac in ticket.AcceptanceCriteria)
            Console.WriteLine($"    - {ac}");
        Console.WriteLine("  Implementation Plan:");
        foreach (string step in ticket.ImplementationPlan)
            Console.WriteLine($"    - {step}");
        Console.WriteLine("  ----------------------------------");
    }

    private static int PromptInt(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string input = Console.ReadLine()?.Trim() ?? "";
            if (int.TryParse(input, out int value))
                return value;
            Console.WriteLine("Please enter a valid number.");
        }
    }

    private record DryRunEntry(
        [property: JsonPropertyName("itemText")] string ItemText,
        [property: JsonPropertyName("ticket")] GeneratedTicket Ticket);
}
