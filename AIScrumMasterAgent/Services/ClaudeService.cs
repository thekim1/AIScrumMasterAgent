using AIScrumMasterAgent.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIScrumMasterAgent.Services;

public class ClaudeService(IHttpClientFactory httpClientFactory, AppConfig config) : IClaudeService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ClaudeConfig _config = config.Claude;

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    private const string SystemPrompt =
        "You are an AI Scrum Master agent for a Swedish municipality development team. " +
        "The team builds .NET 10 and Vue 3 applications. " +
        "Your job is to enrich Azure DevOps work items with useful implementation details. " +
        "The team uses AI coding assistants (GitHub Copilot, Claude Code) so implementation plans must be structured to serve as AI prompts. " +
        "Laws and compliance constraints are important — highlight them if relevant. " +
        "Always respond with valid JSON only. No preamble, no markdown fences.";

    public async Task<GeneratedTicket> GenerateTicketAsync(SprintPlanItem item, RepoContext? context)
    {
        string userPrompt = BuildUserPrompt(item, context);
        HttpClient client = _httpClientFactory.CreateClient("Claude");

        ClaudeRequest requestBody = new()
        {
            Model = _config.Model,
            MaxTokens = _config.MaxTokens,
            System = SystemPrompt,
            Messages = [new ClaudeMessage { Role = "user", Content = userPrompt }]
        };

        string json = JsonSerializer.Serialize(requestBody);
        StringContent httpContent = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(ApiUrl, httpContent);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync();
        ClaudeResponse claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseJson)
            ?? throw new InvalidOperationException("Claude returned an empty response.");

        string text = claudeResponse.Content.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Claude response contained no text content.");

        return ParseTicketJson(text);
    }

    internal static GeneratedTicket ParseTicketJson(string text)
    {
        string clean = text.Trim();

        // Strip accidental markdown fences
        if (clean.StartsWith("```"))
        {
            int firstNewline = clean.IndexOf('\n');
            if (firstNewline >= 0)
                clean = clean[(firstNewline + 1)..];

            int lastFence = clean.LastIndexOf("```");
            if (lastFence >= 0)
                clean = clean[..lastFence];
        }

        clean = clean.Trim();

        GeneratedTicket? ticket = JsonSerializer.Deserialize<GeneratedTicket>(clean, CaseInsensitiveOptions);

        return ticket ?? throw new InvalidOperationException("Failed to deserialize Claude's JSON response.");
    }

    private static string BuildUserPrompt(SprintPlanItem item, RepoContext? context)
    {
        return $$"""
            Create an Azure DevOps work item for the following task.

            ## Task
            Title: {{item.Text}}
            Parent Feature: {{item.ParentFeature ?? "None"}}
            Detected Type: {{item.Kind}}

            ## Repository Context
            Repo: {{context?.RepoName ?? "Not specified"}}
            Solution folder: {{context?.SolutionFolder ?? "Not specified"}}

            ### Folder Structure
            {{context?.FolderTree ?? "Not available"}}

            ### Project Context (AGENT_CONTEXT.md)
            {{context?.AgentContextContent ?? "Not available"}}

            ## Required Output (JSON)
            Respond with exactly this JSON structure:
            {
              "title": "short descriptive title",
              "description": "2-3 sentence description of what and why",
              "acceptanceCriteria": ["criterion 1", "criterion 2"],
              "estimatedHours": "X-Yh",
              "implementationPlan": "markdown formatted plan with steps, relevant file paths, and patterns to follow",
              "detectedType": "Implementation|Investigation|Bug",
              "suggestedTags": ["tag1", "tag2"]
            }
            """;
    }

    // --- Internal request/response models ---

    private class ClaudeRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("system")] public string System { get; set; } = "";
        [JsonPropertyName("messages")] public List<ClaudeMessage> Messages { get; set; } = [];
    }

    private class ClaudeMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class ClaudeResponse
    {
        [JsonPropertyName("content")] public List<ClaudeContentBlock> Content { get; set; } = [];
    }

    private class ClaudeContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }
}
