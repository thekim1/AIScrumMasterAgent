using AIScrumMasterAgent.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace AIScrumMasterAgent.Services;

public class TicketEnricher(
   IAzureDevOpsService devOpsService,
   IClaudeService claudeService,
   AppConfig config) : ITicketEnricher
{
    private readonly IAzureDevOpsService _devOpsService = devOpsService;
    private readonly IClaudeService _claudeService = claudeService;
    private readonly AppConfig _config = config;

    public async Task<WorkItemResult?> EnrichAsync(int sprintPlanTicketId, SprintPlanItem item, RepoContext? context)
    {
        if (item.ExistingTicketId.HasValue)
            return null;

        GeneratedTicket generatedTicket = await _claudeService.GenerateTicketAsync(item, context);

        string description = FormatDescription(generatedTicket);
        string tags = string.Join("; ", generatedTicket.SuggestedTags);

        CreateWorkItemRequest createRequest = new()
        {
            Title = generatedTicket.Title,
            Description = description,
            Tags = tags,
            EstimatedHours = ParseEstimatedHours(generatedTicket.EstimatedHours)
        };

        WorkItem created = await _devOpsService.CreateWorkItemAsync(
            _config.Agent.SprintPlanTicketType, createRequest);

        await _devOpsService.AddChildLinkAsync(sprintPlanTicketId, created.Id);

        await UpdateSprintPlanDescriptionAsync(sprintPlanTicketId, item.Text, created.Id);

        return new WorkItemResult(created.Id, created.Title, created.Url);
    }

    internal static double? ParseEstimatedHours(string? estimatedHours)
    {
        if (string.IsNullOrWhiteSpace(estimatedHours))
            return null;

        Match match = Regex.Match(estimatedHours, @"^\s*(\d+(?:\.\d+)?)");
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            return result;

        return null;
    }

    internal static string FormatDescription(GeneratedTicket ticket)
    {
        StringBuilder sb = new();
        sb.AppendLine($"<h3>Description</h3><p>{ticket.Description}</p>");
        sb.AppendLine("<h3>Acceptance Criteria</h3><ul>");
        foreach (string ac in ticket.AcceptanceCriteria)
            sb.AppendLine($"<li>{ac}</li>");
        sb.AppendLine("</ul>");
        sb.AppendLine($"<h3>Estimated Effort</h3><p>{ticket.EstimatedHours}</p>");
        sb.AppendLine("<h3>AI Implementation Plan</h3><ul>");
        foreach (string step in ticket.ImplementationPlan)
            sb.AppendLine($"<li>{step}</li>");
        sb.AppendLine("</ul>");
        return sb.ToString();
    }

    private async Task UpdateSprintPlanDescriptionAsync(int sprintPlanTicketId, string itemText, int newTicketId)
    {
        WorkItem workItem = await _devOpsService.GetWorkItemAsync(sprintPlanTicketId);
        string currentDescription = workItem.Description;

        // Replace the plain item text with the ticket-number-prefixed version
        string updatedDescription = currentDescription.Replace(
            itemText,
            $"#{newTicketId} {itemText}");

        if (updatedDescription != currentDescription)
            await _devOpsService.UpdateWorkItemDescriptionAsync(sprintPlanTicketId, updatedDescription);
    }
}
