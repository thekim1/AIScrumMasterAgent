namespace AIScrumMasterAgent.Models;

public class AppConfig
{
    public AzureDevOpsConfig AzureDevOps { get; set; } = new();
    public ClaudeConfig Claude { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
}

public class AzureDevOpsConfig
{
    public string OrgUrl { get; set; } = "";
    public string Project { get; set; } = "";
    public string Team { get; set; } = "";
    public string Pat { get; set; } = "";
}

public class ClaudeConfig
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 2000;
}

public class AgentConfig
{
    public string ProcessedMarker { get; set; } = "## AI Implementation Plan";
    public string SprintPlanTicketType { get; set; } = "User Story";
}
