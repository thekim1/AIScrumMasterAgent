using AIScrumMasterAgent.Models;
using AIScrumMasterAgent.Services;
using AIScrumMasterAgent.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text;

IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .Build();

AppConfig appConfig = new();
configuration.Bind(appConfig);

if (string.IsNullOrWhiteSpace(appConfig.AzureDevOps.Pat))
{
    Console.Error.WriteLine("ERROR: AzureDevOps.Pat is not configured. Add it to appsettings.local.json.");
    return 1;
}

if (string.IsNullOrWhiteSpace(appConfig.Claude.ApiKey))
{
    Console.Error.WriteLine("ERROR: Claude.ApiKey is not configured. Add it to appsettings.local.json.");
    return 1;
}

ServiceCollection services = new();

services.AddSingleton(appConfig);

services.AddHttpClient("AzureDevOps", client =>
{
    string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{appConfig.AzureDevOps.Pat}"));
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", credentials);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

services.AddHttpClient("Claude", client =>
{
    client.DefaultRequestHeaders.Add("x-api-key", appConfig.Claude.ApiKey);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});

services.AddSingleton<IAzureDevOpsService, AzureDevOpsService>();
services.AddSingleton<ISprintPlanParser, SprintPlanParser>();
services.AddSingleton<IRepoContextFetcher, RepoContextFetcher>();
services.AddSingleton<IClaudeService, ClaudeService>();
services.AddSingleton<ITicketEnricher, TicketEnricher>();
services.AddSingleton<ConsoleUI>();

ServiceProvider provider = services.BuildServiceProvider();
ConsoleUI ui = provider.GetRequiredService<ConsoleUI>();

await ui.RunAsync();

return 0;

