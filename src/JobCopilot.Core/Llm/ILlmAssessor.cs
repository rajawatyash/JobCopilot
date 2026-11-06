using JobCopilot.Core.Models;

namespace JobCopilot.Core.Llm;

public interface ILlmAssessor
{
    /// <summary>Provider and model, e.g. "ollama:qwen2.5:7b". Returned in results so you can compare.</summary>
    string Name { get; }
    bool IsConfigured { get; }
    Task<LlmAssessment> AssessAsync(MatchRequest request, KeywordAnalysis keywords, CancellationToken ct);
}
