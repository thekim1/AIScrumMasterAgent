using AIScrumMasterAgent.Models;

namespace AIScrumMasterAgent.Services;

public interface ISprintPlanParser
{
    List<SprintPlanItem> Parse(string ticketDescription);
}
