using AIScrumMasterAgent.Models;

namespace AIScrumMasterAgent.Services;

public interface IClaudeService
{
    Task<GeneratedTicket> GenerateTicketAsync(SprintPlanItem item, RepoContext? context);
}
