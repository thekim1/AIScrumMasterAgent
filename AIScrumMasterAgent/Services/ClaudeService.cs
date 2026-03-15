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

        // Extract the JSON object in case Claude added preamble or trailing text
        int jsonStart = clean.IndexOf('{');
        int jsonEnd = clean.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
            clean = clean[jsonStart..(jsonEnd + 1)];

        GeneratedTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<GeneratedTicket>(clean, CaseInsensitiveOptions);
        }
        catch (JsonException)
        {
            string repaired = RepairTruncatedJson(clean);
            ticket = JsonSerializer.Deserialize<GeneratedTicket>(repaired, CaseInsensitiveOptions);
        }

        return ticket ?? throw new InvalidOperationException("Failed to deserialize Claude's JSON response.");
    }

    /// <summary>
    /// Closes any unclosed strings, arrays, and objects in a truncated JSON payload.
    /// </summary>
    private static string RepairTruncatedJson(string json)
    {
        StringBuilder sb = new System.Text.StringBuilder(json.TrimEnd().TrimEnd(','));
        bool inString = false;
        bool escaped = false;
        int braceDepth = 0;
        int bracketDepth = 0;

        foreach (char c in sb.ToString())
        {
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') braceDepth++;
            else if (c == '}') braceDepth--;
            else if (c == '[') bracketDepth++;
            else if (c == ']') bracketDepth--;
        }

        if (inString) sb.Append('"');
        for (int i = 0; i < bracketDepth; i++) sb.Append(']');
        for (int i = 0; i < braceDepth; i++) sb.Append('}');

        return sb.ToString();
    }

    private const int MaxFolderTreeLines = 50;
    private const int MaxAgentContextChars = 1200;

    private static string BuildUserPrompt(SprintPlanItem item, RepoContext? context)
    {
        string folderTree = TruncateLines(context?.FolderTree, MaxFolderTreeLines) ?? "Not available";
        string agentContext = TruncateChars(context?.AgentContextContent, MaxAgentContextChars) ?? "Not available";

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
            {{folderTree}}

            ### Project Context (AGENT_CONTEXT.md)
            {{agentContext}}

            ## Required Output (JSON)
            Respond with exactly this JSON structure. Be concise — stay within the stated limits.
            {
              "title": "short descriptive title (max 80 chars)",
              "description": "1-2 sentences, what and why (max 200 chars)",
              "acceptanceCriteria": ["up to 4 criteria, 1 sentence each"],
              "estimatedHours": "X-Yh",
              "implementationPlan": "up to 5 bullet points with relevant file paths",
              "detectedType": "Implementation|Investigation|Bug",
              "suggestedTags": ["up to 3 tags"]
            }
            """;
    }

    private static string? TruncateLines(string? text, int maxLines)
    {
        if (text is null) return null;
        string[] lines = text.Split('\n');
        if (lines.Length <= maxLines) return text;
        return string.Join('\n', lines.Take(maxLines)) + $"\n... ({lines.Length - maxLines} lines omitted)";
    }

    private static string? TruncateChars(string? text, int maxChars)
    {
        if (text is null) return null;
        return text.Length <= maxChars ? text : text[..maxChars] + "... (truncated)";
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
