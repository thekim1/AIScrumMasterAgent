namespace AIScrumMasterAgent.Models;

public class CreateWorkItemRequest
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? AreaPath { get; set; }
    public string? Tags { get; set; }
    public int? ParentId { get; set; }
}
