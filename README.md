# JobCopilot

An AI assistant for job hunting. Paste a job description and your CV, and it tells you how well they match, what's missing, and how to reword your CV bullets in the JD's language, without inventing experience. Then it drafts a cover letter from the same evidence, streamed as it is written.

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

```
POST /api/cover-letter
      │
      ▼
 CoverLetterService ──► MatchService          (the same grounding pass as above)
      │                      │
      │                      ▼ strengths to lead with / gaps not to claim
      └──────────────► ILlmWriter             (chosen by Llm:Provider)
                         ├─ AnthropicWriter   "stream": true  → SSE frames
                         └─ OllamaWriter      "stream": true  → newline-delimited JSON
                               │
                               ▼  re-framed as one SSE stream
             event: delta  data: {"text":"Twelve years of .NET..."}
             event: done   data: {"overallScore":71,"aiModel":"...","writerModel":"..."}
```

- **Keyword scan first.** A cheap regex scan against a skill catalog (`SkillCatalog.cs`) with aliases (for example, `k8s` and `AKS` count as Kubernetes). It handles symbol terms like `C#` and `.NET`, where `\b` word boundaries don't work.
- **Structured LLM output.** The Claude call sets `output_config.format` to a JSON schema, so the answer comes back as JSON matching that schema rather than free text. It originally forced a `record_assessment` tool call instead; current models reject a forced `tool_choice` with a 400, and structured outputs are the supported way to get JSON back. The parser still treats `stop_reason` of `refusal` or `max_tokens` as a failure, because a schema only holds for an answer the model finished.
- **Swappable models.** Both providers share one prompt and one JSON schema (`AssessmentSchema.cs`). Claude gets it as `output_config.format`, Ollama as `format`; both constrain decoding to that shape. Claude's structured outputs reject numeric constraints, so `score` carries its 0–100 range in the description and the parser clamps it. The response includes `aiModel` (e.g. `ollama:qwen2.5:7b`), so you can compare models on the same JD.
- **Graceful degradation.** With no API key, or if the API call fails, you still get the keyword result plus an `aiError` explaining why.
- **Grounded prompt.** The system prompt tells the model to judge only on CV evidence and never invent experience.
- **The letter is grounded in the match, not just the CV.** `/api/cover-letter` runs the match first and feeds the result into the letter prompt: lead with these strengths, do not claim these gaps. That is also why the match runs server-side instead of being posted in by the client — a score the caller supplies is a score the caller can invent.
- **Streaming, and where errors can still go.** Validation and the grounding pass both finish *before* the first byte, because that is the last moment a failure can still be an HTTP status code (400 for bad input, 503 when no provider is configured). After the first frame the response is a 200 for good, so a provider that dies mid-letter is reported as an `event: error` frame instead. Two providers stream differently — Claude sends SSE, Ollama sends newline-delimited JSON — and both are normalised to one `IAsyncEnumerable<string>` behind `ILlmWriter`, so the browser only ever sees SSE.
- **Tone is an enum, not a string.** `Professional`, `Direct`, `Warm`, `Enthusiastic`. The tone is interpolated straight into the prompt, so free text there would hand the caller a pen. An unknown name is a 400.

## Run it

Prerequisite: the .NET 10 SDK.

```bash
dotnet test                                   # 33 tests, no API key or Ollama needed

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
src/JobCopilot.Core     domain: models, keyword matcher, provider clients
                        (ILlmAssessor / ILlmWriter), MatchService, CoverLetterService
src/JobCopilot.Api      minimal API, DI wiring, typed HttpClients, SSE framing
tests/JobCopilot.Tests  xUnit: matcher unit tests, response and stream parsing,
                        API tests via WebApplicationFactory
```

## Roadmap

- [x] **Slice 1:** match endpoint with keyword scan and an AI assessment from Claude or Ollama (switchable), plus tests
- [x] **Slice 2:** cover letter endpoint, streamed with Server-Sent Events, grounded in the match result, with a tone option
- [ ] **Slice 3:** RAG over your own documents (CV, project notes) with PostgreSQL + pgvector and an embeddings model, so suggestions cite real projects
- [ ] **Slice 4:** application tracker (EF Core + PostgreSQL): company, role, status, score, dates
- [ ] **Slice 5:** React + TypeScript UI with a tracker board and match view
- [ ] **Slice 6:** evaluation set of 10 real JDs with expected gaps, run in CI to catch prompt regressions. Compare Claude vs local models on quality, latency and cost
- [ ] **Slice 7:** Docker Compose (API + Postgres), deploy to Azure Container Apps, OpenTelemetry traces

GitHub Actions CI (build + test on every push) runs from `.github/workflows/ci.yml`. The whole suite runs offline, so CI needs no API key.

## What I learned / talking points

- Why to constrain output with a schema instead of asking for "JSON please", how to validate and clamp what comes back, and what to do when a provider retires the mechanism you built on (forced tool use → structured outputs).
- Why a deterministic check runs before the LLM (cost, latency, explainability, a fallback).
- Provider abstraction: frontier vs local models, and the trade-offs between them (privacy, cost, latency, quality). Structured output works differently per provider (tool use vs constrained decoding).
- How to test code that calls an LLM: a fake `ILlmAssessor` in API tests, and real response fixtures in parser tests. Streaming is tested the same way — an SSE fixture through a stub `HttpMessageHandler`, and a stub writer that fails after its first chunk to prove the error frame.
- SSE vs WebSockets vs long polling, and why a one-way token stream wants SSE: it is plain HTTP, it reconnects on its own, and it needs no second protocol.
- Why error handling changes shape once you stream. The status code is spent on the first byte, so anything that can fail with a code has to fail before then, and everything after it needs an in-band channel. `yield return` is also illegal inside a `try` with a `catch`, so `CoverLetterStream` drives the enumerator by hand to catch a mid-stream failure.
- Cost and latency control: `output_config.effort` is set to `low` for the letter, since drafting from gathered material is not a reasoning task, while the assessment call is where thinking earns its tokens. The writers also run with no `HttpClient.Timeout`, because that timeout covers the whole response — which for a stream is the whole letter — so cancellation does that job instead.
