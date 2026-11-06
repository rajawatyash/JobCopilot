# JobCopilot

An AI assistant for job hunting. Paste a job description and your CV, and it tells you how well they match, what's missing, and how to reword your CV bullets in the JD's language, without inventing experience.

Built with .NET 10 and ASP.NET Core minimal APIs. The AI part runs on either a frontier model (Claude API) or a local model (Ollama), switched by config.

## How it works

```
POST /api/match
      │
      ▼
 MatchService ──► KeywordMatcher      (deterministic, free, always runs)
      │                │
      │                ▼ matched / missing skills
      └──────► ILlmAssessor          (chosen by Llm:Provider)
                 ├─ AnthropicAssessor  Claude API, structured outputs → typed JSON
                 └─ OllamaAssessor     local model, JSON-schema "format" → typed JSON
                       │
                       ▼
 MatchResult = 0.7 × AI score + 0.3 × keyword coverage
```

- **Keyword scan first.** A cheap regex scan against a skill catalog (`SkillCatalog.cs`) with aliases (for example, `k8s` and `AKS` count as Kubernetes). It handles symbol terms like `C#` and `.NET`, where `\b` word boundaries don't work.
- **Structured LLM output.** The Claude call sets `output_config.format` to a JSON schema, so the answer comes back as JSON matching that schema rather than free text. It originally forced a `record_assessment` tool call instead; current models reject a forced `tool_choice` with a 400, and structured outputs are the supported way to get JSON back. The parser still treats `stop_reason` of `refusal` or `max_tokens` as a failure, because a schema only holds for an answer the model finished.
- **Swappable models.** Both providers share one prompt and one JSON schema (`AssessmentSchema.cs`). Claude gets it as `output_config.format`, Ollama as `format`; both constrain decoding to that shape. Claude's structured outputs reject numeric constraints, so `score` carries its 0–100 range in the description and the parser clamps it. The response includes `aiModel` (e.g. `ollama:qwen2.5:7b`), so you can compare models on the same JD.
- **Graceful degradation.** With no API key, or if the API call fails, you still get the keyword result plus an `aiError` explaining why.
- **Grounded prompt.** The system prompt tells the model to judge only on CV evidence and never invent experience.

## Run it

Prerequisite: the .NET 10 SDK.

```bash
dotnet test                                   # 19 tests, no API key or Ollama needed

cd src/JobCopilot.Api
dotnet user-secrets init
dotnet user-secrets set "Anthropic:ApiKey" "<your key from console.anthropic.com>"
dotnet run
```

### Using a local model (Ollama) instead

```bash
ollama pull qwen2.5:7b          # or llama3.1:8b, mistral, etc.
ollama serve                    # if it isn't already running
cd src/JobCopilot.Api
dotnet run --Llm:Provider=Ollama --Ollama:Model=qwen2.5:7b
```

You don't need an API key. The app sends `num_ctx` (`Ollama:ContextLength`, 8192 by default), because Ollama's default context window is small and silently drops the start of long prompts. You can also set `"Llm": { "Provider": "Ollama" }` in `appsettings.Development.json`. 7–8B models work, but the score and gap quality is noticeably weaker than a frontier model, which is worth measuring in the eval slice.

Then send the requests in `src/JobCopilot.Api/JobCopilot.Api.http` from VS Code (REST Client) or Visual Studio. The OpenAPI document is at `/openapi/v1.json` in Development.

The key is stored with user-secrets, so it never goes into the repo. To change the model, set `Anthropic:Model`.

## Project layout

```
src/JobCopilot.Core     domain: models, keyword matcher, Claude client, MatchService
src/JobCopilot.Api      minimal API, DI wiring, typed HttpClient
tests/JobCopilot.Tests  xUnit: matcher unit tests, response parsing, API tests via WebApplicationFactory
```

## Roadmap

- [x] **Slice 1:** match endpoint with keyword scan and an AI assessment from Claude or Ollama (switchable), plus tests
- [ ] **Slice 2:** cover letter endpoint, streaming the response with Server-Sent Events
- [ ] **Slice 3:** RAG over your own documents (CV, project notes) with PostgreSQL + pgvector and an embeddings model, so suggestions cite real projects
- [ ] **Slice 4:** application tracker (EF Core + PostgreSQL): company, role, status, score, dates
- [ ] **Slice 5:** React + TypeScript UI with a tracker board and match view
- [ ] **Slice 6:** evaluation set of 10 real JDs with expected gaps, run in CI to catch prompt regressions. Compare Claude vs local models on quality, latency and cost
- [ ] **Slice 7:** Docker Compose (API + Postgres), GitHub Actions CI, deploy to Azure Container Apps

## What I learned / talking points

- Why to constrain output with a schema instead of asking for "JSON please", how to validate and clamp what comes back, and what to do when a provider retires the mechanism you built on (forced tool use → structured outputs).
- Why a deterministic check runs before the LLM (cost, latency, explainability, a fallback).
- Provider abstraction: frontier vs local models, and the trade-offs between them (privacy, cost, latency, quality). Structured output works differently per provider (tool use vs constrained decoding).
- How to test code that calls an LLM: a fake `ILlmAssessor` in API tests, and real response fixtures in parser tests.
