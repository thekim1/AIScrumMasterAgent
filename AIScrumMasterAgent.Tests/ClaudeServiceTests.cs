using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;
using Moq;
using Shouldly;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AIScrumMasterAgent.Tests;

public class ClaudeServiceTests
{
    private static AppConfig CreateConfig() => new()
    {
        Claude = new ClaudeConfig { ApiKey = "test-key", Model = "claude-test", MaxTokens = 100 }
    };

    private static IHttpClientFactory CreateFactory(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        FakeHttpMessageHandler handler = new(responseBody, statusCode);
        HttpClient client = new(handler);
        Mock<IHttpClientFactory> factory = new();
        factory.Setup(f => f.CreateClient("Claude")).Returns(client);
        return factory.Object;
    }

    private static string WrapInClaudeResponse(string ticketJson)
    {
        string escaped = JsonSerializer.Serialize(ticketJson);
        return $"{{\"content\":[{{\"type\":\"text\",\"text\":{escaped}}}]}}";
    }

    private const string ValidTicketJson = """
        {
          "title": "Test title",
          "description": "Test description",
          "acceptanceCriteria": ["AC1", "AC2"],
          "estimatedHours": "4-8h",
          "implementationPlan": "## Steps\n- Step 1",
          "detectedType": "Implementation",
          "suggestedTags": ["EPlatform"]
        }
        """;

    [Fact]
    public async Task GenerateTicketAsync_DeserializesValidResponse()
    {
        IHttpClientFactory factory = CreateFactory(WrapInClaudeResponse(ValidTicketJson));
        ClaudeService service = new(factory, CreateConfig());
        SprintPlanItem item = new("Formatera all kod", "Eplattformen 2.0", null, ItemKind.Implementation);

        GeneratedTicket result = await service.GenerateTicketAsync(item, null);

        result.Title.ShouldBe("Test title");
        result.Description.ShouldBe("Test description");
        result.AcceptanceCriteria.Count.ShouldBe(2);
        result.EstimatedHours.ShouldBe("4-8h");
        result.SuggestedTags.ShouldContain("EPlatform");
    }

    [Fact]
    public async Task GenerateTicketAsync_HandlesMarkdownFencedJson()
    {
        string fencedJson = $"```json\n{ValidTicketJson}\n```";
        IHttpClientFactory factory = CreateFactory(WrapInClaudeResponse(fencedJson));
        ClaudeService service = new(factory, CreateConfig());
        SprintPlanItem item = new("Some task", null, null, ItemKind.Implementation);

        GeneratedTicket result = await service.GenerateTicketAsync(item, null);

        result.Title.ShouldBe("Test title");
    }

    [Fact]
    public async Task GenerateTicketAsync_ThrowsOnApiError()
    {
        IHttpClientFactory factory = CreateFactory("Unauthorized", HttpStatusCode.Unauthorized);
        ClaudeService service = new(factory, CreateConfig());
        SprintPlanItem item = new("Some task", null, null, ItemKind.Implementation);

        await Should.ThrowAsync<HttpRequestException>(
            () => service.GenerateTicketAsync(item, null));
    }
}

internal class FakeHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
{
    private readonly string _responseBody = responseBody;
    private readonly HttpStatusCode _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = new(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
