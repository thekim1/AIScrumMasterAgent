using AIScrumMasterAgent.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIScrumMasterAgent.Services;

public class AzureDevOpsService(IHttpClientFactory httpClientFactory, AppConfig config) : IAzureDevOpsService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly AppConfig _config = config;

    private string BaseUrl => $"{_config.AzureDevOps.OrgUrl}/{Uri.EscapeDataString(_config.AzureDevOps.Project)}";
    private string TeamBaseUrl => $"{_config.AzureDevOps.OrgUrl}/{Uri.EscapeDataString(_config.AzureDevOps.Project)}/{Uri.EscapeDataString(_config.AzureDevOps.Team)}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient CreateClient()
    {
        HttpClient client = _httpClientFactory.CreateClient("AzureDevOps");
        return client;
    }

    public async Task<WorkItem> GetWorkItemAsync(int id)
    {
        HttpClient client = CreateClient();
        string url = $"{BaseUrl}/_apis/wit/workitems/{id}?$expand=all&api-version=7.1";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        WorkItemApiResponse apiItem = JsonSerializer.Deserialize<WorkItemApiResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize work item response.");

        return MapWorkItem(apiItem);
    }

    public async Task<WorkItem> CreateWorkItemAsync(string type, CreateWorkItemRequest request)
    {
        HttpClient client = CreateClient();
        string url = $"{BaseUrl}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version=7.1";

        List<object> patchOps =
        [
            new { op = "add", path = "/fields/System.Title", value = request.Title },
            new { op = "add", path = "/fields/System.Description", value = request.Description }
        ];

        if (!string.IsNullOrEmpty(request.AreaPath))
            patchOps.Add(new { op = "add", path = "/fields/System.AreaPath", value = request.AreaPath });

        if (!string.IsNullOrEmpty(request.Tags))
            patchOps.Add(new { op = "add", path = "/fields/System.Tags", value = request.Tags });

        if (request.EstimatedHours.HasValue)
            patchOps.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate", value = (object)request.EstimatedHours.Value });

        if (!string.IsNullOrEmpty(request.IterationPath))
            patchOps.Add(new { op = "add", path = "/fields/System.IterationPath", value = request.IterationPath });

        string json = JsonSerializer.Serialize(patchOps);
        StringContent content = new(json, Encoding.UTF8, "application/json-patch+json");

        HttpResponseMessage response = await client.PostAsync(url, content);
        await EnsureSuccessWithLoggingAsync(response, $"CreateWorkItem (type='{type}', url={url})");

        string responseJson = await response.Content.ReadAsStringAsync();
        WorkItemApiResponse apiItem = JsonSerializer.Deserialize<WorkItemApiResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize created work item response.");

        return MapWorkItem(apiItem);
    }

    public async Task UpdateWorkItemDescriptionAsync(int id, string newDescription)
    {
        HttpClient client = CreateClient();
        string url = $"{BaseUrl}/_apis/wit/workitems/{id}?api-version=7.1";

        var patchOps = new[]
        {
            new { op = "add", path = "/fields/System.Description", value = newDescription }
        };

        string json = JsonSerializer.Serialize(patchOps);
        StringContent content = new(json, Encoding.UTF8, "application/json-patch+json");

        HttpResponseMessage response = await client.PatchAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddRelatedLinkAsync(int sourceId, int targetId)
    {
        HttpClient client = CreateClient();
        string url = $"{BaseUrl}/_apis/wit/workitems/{sourceId}?api-version=7.1";

        var patchOps = new[]
        {
            new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "System.LinkTypes.Related",
                    url = $"{_config.AzureDevOps.OrgUrl}/_apis/wit/workitems/{targetId}"
                }
            }
        };

        string json = JsonSerializer.Serialize(patchOps);
        StringContent content = new(json, Encoding.UTF8, "application/json-patch+json");

        HttpResponseMessage response = await client.PatchAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<RepoInfo>> ListReposAsync()
    {
        HttpClient client = CreateClient();
        string url = $"{BaseUrl}/_apis/git/repositories?api-version=7.1";

        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        ListApiResponse<RepoApiItem> apiResponse = JsonSerializer.Deserialize<ListApiResponse<RepoApiItem>>(json, JsonOptions)
            ?? new ListApiResponse<RepoApiItem>();

        return [.. apiResponse.Value.Select(r => new RepoInfo { Id = r.Id, Name = r.Name })];
    }

    public async Task<List<string>> GetFolderTreeAsync(string repoId, string folderPath, int depth = 3)
    {
        HttpClient client = CreateClient();
        string encodedPath = Uri.EscapeDataString(folderPath);
        string url = $"{BaseUrl}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/items?scopePath={encodedPath}&recursionLevel=Full&api-version=7.1";

        HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return [];

        string json = await response.Content.ReadAsStringAsync();
        ListApiResponse<GitItemApi> apiResponse = JsonSerializer.Deserialize<ListApiResponse<GitItemApi>>(json, JsonOptions)
            ?? new ListApiResponse<GitItemApi>();

        string normalizedFolder = folderPath.TrimEnd('/');

        return [.. apiResponse.Value
            .Select(item => item.Path)
            .Where(path => path != normalizedFolder)
            .Where(path =>
            {
                string relative = path.StartsWith(normalizedFolder)
                    ? path[normalizedFolder.Length..].TrimStart('/')
                    : path.TrimStart('/');
                int segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
                return segments <= depth;
            })];
    }

    public async Task<string?> GetFileContentAsync(string repoId, string filePath)
    {
        HttpClient client = CreateClient();
        string encodedPath = Uri.EscapeDataString(filePath);
        string url = $"{BaseUrl}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/items?path={encodedPath}&includeContent=true&api-version=7.1";

        HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("content", out JsonElement contentEl))
            return contentEl.GetString();

        return null;
    }

    public async Task<List<WorkItem>> GetCurrentSprintItemsAsync()
    {
        HttpClient client = CreateClient();
        string iterUrl = $"{TeamBaseUrl}/_apis/work/teamsettings/iterations?$timeframe=current&api-version=7.1";

        HttpResponseMessage iterResponse = await client.GetAsync(iterUrl);
        iterResponse.EnsureSuccessStatusCode();

        string iterJson = await iterResponse.Content.ReadAsStringAsync();
        ListApiResponse<IterationApi> iterList = JsonSerializer.Deserialize<ListApiResponse<IterationApi>>(iterJson, JsonOptions)
            ?? new ListApiResponse<IterationApi>();

        IterationApi? currentIteration = iterList.Value.FirstOrDefault();
        if (currentIteration is null)
            return [];

        string wiUrl = $"{TeamBaseUrl}/_apis/work/teamsettings/iterations/{currentIteration.Id}/workitems?api-version=7.1";
        HttpResponseMessage wiResponse = await client.GetAsync(wiUrl);
        wiResponse.EnsureSuccessStatusCode();

        string wiJson = await wiResponse.Content.ReadAsStringAsync();
        IterationWorkItemsApiResponse wiRefs = JsonSerializer.Deserialize<IterationWorkItemsApiResponse>(wiJson, JsonOptions)
            ?? new IterationWorkItemsApiResponse();

        List<int> ids = [.. wiRefs.WorkItemRelations
            .Where(r => r.Target is not null)
            .Select(r => r.Target!.Id)
            .Distinct()];

        if (ids.Count == 0)
            return [];

        string batchUrl = $"{BaseUrl}/_apis/wit/workitems?ids={string.Join(',', ids)}&api-version=7.1";
        HttpResponseMessage batchResponse = await client.GetAsync(batchUrl);
        batchResponse.EnsureSuccessStatusCode();

        string batchJson = await batchResponse.Content.ReadAsStringAsync();
        ListApiResponse<WorkItemApiResponse> batchList = JsonSerializer.Deserialize<ListApiResponse<WorkItemApiResponse>>(batchJson, JsonOptions)
            ?? new ListApiResponse<WorkItemApiResponse>();

        return [.. batchList.Value.Select(MapWorkItem)];
    }

    private static async Task EnsureSuccessWithLoggingAsync(HttpResponseMessage response, string context)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"[AzureDevOps] {context} — {(int)response.StatusCode} {response.ReasonPhrase}");
            Console.Error.WriteLine($"[AzureDevOps] Response body: {body}");
            response.EnsureSuccessStatusCode();
        }
    }

    private static WorkItem MapWorkItem(WorkItemApiResponse api)
    {
        string GetField(string key)
        {
            if (api.Fields is not null && api.Fields.TryGetValue(key, out JsonElement el))
                return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();
            return "";
        }

        return new WorkItem
        {
            Id = api.Id,
            Title = GetField("System.Title"),
            Description = GetField("System.Description"),
            WorkItemType = GetField("System.WorkItemType"),
            Url = api.Url ?? "",
            IterationPath = GetField("System.IterationPath")
        };
    }

    // --- Internal API response models ---

    private class WorkItemApiResponse
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("fields")] public Dictionary<string, JsonElement>? Fields { get; set; }
    }

    private class RepoApiItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private class GitItemApi
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("isFolder")] public bool IsFolder { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    private class ListApiResponse<T>
    {
        [JsonPropertyName("value")] public List<T> Value { get; set; } = [];
        [JsonPropertyName("count")] public int Count { get; set; }
    }

    private class IterationApi
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private class IterationWorkItemsApiResponse
    {
        [JsonPropertyName("workItemRelations")]
        public List<WorkItemRelationRefApi> WorkItemRelations { get; set; } = [];
    }

    private class WorkItemRelationRefApi
    {
        [JsonPropertyName("target")] public WorkItemRefApi? Target { get; set; }
    }

    private class WorkItemRefApi
    {
        [JsonPropertyName("id")] public int Id { get; set; }
    }
}
