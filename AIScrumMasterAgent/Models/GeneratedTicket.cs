using System.Text.Json.Serialization;

namespace AIScrumMasterAgent.Models;

public record GeneratedTicket(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("acceptanceCriteria")] List<string> AcceptanceCriteria,
    [property: JsonPropertyName("estimatedHours")] string EstimatedHours,
    [property: JsonPropertyName("implementationPlan")] string ImplementationPlan,
    [property: JsonPropertyName("detectedType")] string DetectedType,
    [property: JsonPropertyName("suggestedTags")] List<string> SuggestedTags);
