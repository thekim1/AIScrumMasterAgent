using AIScrumMasterAgent.Models;

namespace AIScrumMasterAgent.Services;

public interface ITicketEnricher
{
    Task<WorkItemResult?> EnrichAsync(int sprintPlanTicketId, SprintPlanItem item, RepoContext? context);
}
