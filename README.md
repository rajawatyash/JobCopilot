# JobCopilot

An AI assistant for job hunting. Paste a job description and your CV, and it tells you how well they match, what's missing, and how to reword your CV bullets in the JD's language, without inventing experience.

Built with .NET 10, ASP.NET Core minimal APIs and the Claude API.

## How it works

```
POST /api/match
      │
      ▼
 MatchService ──► KeywordMatcher      (deterministic, free, always runs)
      │                │
      │                ▼ matched / missing skills
      └──────► AnthropicAssessor     (Claude, forced tool call → typed JSON)
                       │
                       ▼
 MatchResult = 0.7 × AI score + 0.3 × keyword coverage
```

- **Keyword scan first.** A cheap regex scan against a skill catalog (`SkillCatalog.cs`) with aliases (for example, `k8s` and `AKS` count as Kubernetes). It handles symbol terms like `C#` and `.NET`, where `\b` word boundaries don't work.
- **Structured LLM output.** The Claude call uses `tool_choice` to force a `record_assessment` tool call, so the answer always comes back as JSON matching a schema rather than free text.
- **Graceful degradation.** With no API key, or if the API call fails, you still get the keyword result plus an `aiError` explaining why.
- **Grounded prompt.** The system prompt tells the model to judge only on CV evidence and never invent experience.

## Run it

Prerequisite: the .NET 10 SDK.

```bash
dotnet test                                   # 11 tests, no API key needed

cd src/JobCopilot.Api
dotnet user-secrets init
dotnet user-secrets set "Anthropic:ApiKey" "<your key from console.anthropic.com>"
dotnet run
```

Then send the requests in `src/JobCopilot.Api/JobCopilot.Api.http` from VS Code (REST Client) or Visual Studio. The OpenAPI document is at `/openapi/v1.json` in Development.

The key is stored with user-secrets, so it never goes into the repo. To change the model, set `Anthropic:Model`.

## Project layout

```
src/JobCopilot.Core     domain: models, keyword matcher, Claude client, MatchService
src/JobCopilot.Api      minimal API, DI wiring, typed HttpClient
tests/JobCopilot.Tests  xUnit: matcher unit tests, response parsing, API tests via WebApplicationFactory
```

## Roadmap

- [x] **Slice 1:** match endpoint with keyword scan and Claude assessment, plus tests
- [ ] **Slice 2:** cover letter endpoint, streaming the response with Server-Sent Events
- [ ] **Slice 3:** RAG over your own documents (CV, project notes) with PostgreSQL + pgvector and an embeddings model, so suggestions cite real projects
- [ ] **Slice 4:** application tracker (EF Core + PostgreSQL): company, role, status, score, dates
- [ ] **Slice 5:** React + TypeScript UI with a tracker board and match view
- [ ] **Slice 6:** evaluation set of 10 real JDs with expected gaps, run in CI to catch prompt regressions
- [ ] **Slice 7:** Docker Compose (API + Postgres), GitHub Actions CI, deploy to Azure Container Apps

## What I learned / talking points

- Why to force tool use instead of asking for "JSON please", and how to validate and clamp model output.
- Why a deterministic check runs before the LLM (cost, latency, explainability, a fallback).
- How to test code that calls an LLM: a fake `ILlmAssessor` in API tests, and real response fixtures in parser tests.
