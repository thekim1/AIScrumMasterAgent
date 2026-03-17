namespace AIScrumMasterAgent.Models;

public record SprintPlanItem(
    string Text,
    string? ParentFeature,
    int? ExistingTicketId,
    ItemKind Kind);

public enum ItemKind
{
    Implementation,
    Investigation,
    Meeting,
    Feature,
    Unknown
}
