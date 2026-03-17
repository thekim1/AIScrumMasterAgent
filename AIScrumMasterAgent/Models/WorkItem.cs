namespace AIScrumMasterAgent.Models;

public class WorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string WorkItemType { get; set; } = "";
    public string Url { get; set; } = "";
    public string IterationPath { get; set; } = "";
}
