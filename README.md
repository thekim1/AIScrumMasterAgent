# AI Scrum Master Agent

A **.NET 10 console application** that automates Azure DevOps ticket creation from sprint plan work items using the **Anthropic Claude API**. It reads a sprint plan ticket, identifies unlinked todo items, generates rich ticket content with Claude, creates the tickets in Azure DevOps, and writes the new ticket numbers back into the sprint plan.

---

## Features

- Fetches a sprint plan work item from Azure DevOps by ticket ID
- Parses the `Features/Stories and Todos` section to find todo items that don't yet have a ticket number
- Presents each item interactively — create, skip, or exclude (meetings/admin)
- Fetches repo folder structure and `AGENT_CONTEXT.md` from the Azure DevOps Git API to provide code context
- Calls the Claude API to generate: title, description, acceptance criteria, time estimate, and an AI-ready implementation plan
- Creates the new work item in Azure DevOps
- Writes the new ticket number back into the sprint plan ticket description

Authentication is via **Personal Access Token (PAT)**. No infrastructure required — runs from a developer machine.

---

## Project Structure

```
AIScrumMasterAgent.sln
├── AIScrumMasterAgent/               # Main console application
│   ├── Program.cs
│   ├── appsettings.json              # Non-secret config (checked in)
│   ├── appsettings.local.json        # Secret config — PAT, API keys (gitignored)
│   ├── Models/
│   │   ├── AppConfig.cs
│   │   ├── SprintPlanItem.cs
│   │   ├── RepoContext.cs
│   │   ├── GeneratedTicket.cs
│   │   ├── WorkItem.cs
│   │   ├── WorkItemResult.cs
│   │   ├── RepoInfo.cs
│   │   └── CreateWorkItemRequest.cs
│   ├── Services/
│   │   ├── AzureDevOpsService.cs
│   │   ├── SprintPlanParser.cs
│   │   ├── RepoContextFetcher.cs
│   │   ├── ClaudeService.cs
│   │   └── TicketEnricher.cs
│   └── UI/
│       └── ConsoleUI.cs
└── AIScrumMasterAgent.Tests/         # Unit tests (xUnit)
    ├── SprintPlanParserTests.cs
    ├── RepoContextFetcherTests.cs
    ├── ClaudeServiceTests.cs
    └── TicketEnricherTests.cs
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An **Azure DevOps** organisation with a project and at least one sprint plan work item
- An **Anthropic API key** ([console.anthropic.com](https://console.anthropic.com))

---

## Setup

### 1. Clone the repo

```bash
git clone <repo-url>
cd AIScrumMasterAgent
```

### 2. Create an Azure DevOps PAT

1. Go to `https://dev.azure.com/YOUR_ORG`
2. Click your profile icon → **Personal access tokens** → **+ New Token**
3. Set the following scopes:
   - **Work Items:** Read & Write
   - **Code:** Read
4. Copy the token — it is only shown once

### 3. Get an Anthropic API Key

1. Go to [console.anthropic.com](https://console.anthropic.com)
2. Navigate to **API Keys** → **Create Key**
3. Copy the key

### 4. Configure secrets

Create `AIScrumMasterAgent/appsettings.local.json` (this file is gitignored):

```json
{
  "AzureDevOps": {
    "Pat": "YOUR_PAT_HERE"
  },
  "Claude": {
    "ApiKey": "YOUR_CLAUDE_API_KEY_HERE"
  }
}
```

### 5. Update `appsettings.json`

Edit `AIScrumMasterAgent/appsettings.json` to match your Azure DevOps organisation:

```json
{
  "AzureDevOps": {
    "OrgUrl": "https://dev.azure.com/YOUR_ORG",
    "Project": "YOUR_PROJECT",
    "Team": "Your Team"
  },
  "Claude": {
    "Model": "claude-sonnet-4-20250514",
    "MaxTokens": 2000
  },
  "Agent": {
    "ProcessedMarker": "## AI Implementation Plan",
    "SprintPlanTicketType": "User Story"
  }
}
```

---

## Running the App

```bash
cd AIScrumMasterAgent
dotnet run
```

### Dry-run mode

Use `--dry-run` to preview what Claude would generate **without writing anything to Azure DevOps**. Azure DevOps is still contacted to fetch the sprint plan and repo context; only ticket creation, child links, and description updates are skipped.

```bash
dotnet run -- --dry-run
```

When active, a prominent `[DRY RUN]` banner is printed at startup, and any item you choose to create (`C`) will display the generated ticket content in the console instead of creating it.

#### Saving responses to a file

Add `--output <path>` to save every generated Claude response as a JSON file. This lets you inspect and validate the JSON before committing to creating real tickets. Passing `--output` automatically enables dry-run mode.

```bash
dotnet run -- --dry-run --output responses.json
# or shorthand (--output implies --dry-run):
dotnet run -- --output responses.json
```

The file is a JSON array — one entry per item you chose to generate:

```json
[
  {
    "itemText": "Implement feature X",
    "ticket": {
      "title": "...",
      "description": "...",
      "acceptanceCriteria": ["..."],
      "estimatedHours": "4-8h",
      "implementationPlan": "...",
      "detectedType": "Implementation",
      "suggestedTags": ["..."]
    }
  }
]
```

The app will prompt you step-by-step:

1. **Enter sprint plan ticket ID** — the Azure DevOps work item number for your sprint plan
2. **Select repositories** — choose which repos to include as context for Claude
3. **Enter solution folder** — the path inside the repo where your solution lives (e.g. `src/MyApp`)
4. **Review each todo item** — for each unlinked item choose:
   - `C` — generate and create a ticket
   - `F` — create as a feature ticket
   - `S` — skip
   - `E` — exclude as meeting/admin task
5. **Select repo context for item** — the chosen repository is automatically pre-selected based on the item text, but you can override it or change the solution path if necessary.
6. A summary is printed at the end with all created ticket numbers

### Repo context (`AGENT_CONTEXT.md`)

If a file named `AGENT_CONTEXT.md` exists at the root of the specified solution folder in your repo, its contents will be included in the Claude prompt. Use this file to describe architectural decisions, conventions, and constraints that Claude should be aware of.

---

## Running Tests

```bash
dotnet test
```

---

## Configuration Reference

| Key | Description | Default |
|-----|-------------|---------|
| `AzureDevOps.OrgUrl` | Azure DevOps organisation URL | *(required)* |
| `AzureDevOps.Project` | Azure DevOps project name | *(required)* |
| `AzureDevOps.Team` | Team name | *(required)* |
| `AzureDevOps.Pat` | Personal Access Token — put in `appsettings.local.json` | *(required)* |
| `Claude.ApiKey` | Anthropic API key — put in `appsettings.local.json` | *(required)* |
| `Claude.Model` | Claude model to use | `claude-sonnet-4-20250514` |
| `Claude.MaxTokens` | Max tokens per Claude response | `2000` |
| `Agent.ProcessedMarker` | Heading injected into sprint plan after processing | `## AI Implementation Plan` |
| `Agent.SprintPlanTicketType` | Work item type used when creating tickets | `User Story` |

---

## Security Notes

- **Never commit `appsettings.local.json`** — it is listed in `.gitignore`
- The PAT should be scoped to the minimum required permissions (Work Items: Read & Write, Code: Read)
- Rotate the PAT and Claude API key regularly
