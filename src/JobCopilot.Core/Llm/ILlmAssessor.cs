using JobCopilot.Core.Models;

namespace JobCopilot.Core.Llm;

public interface ILlmAssessor
{
    bool IsConfigured { get; }
    Task<LlmAssessment> AssessAsync(MatchRequest request, KeywordAnalysis keywords, CancellationToken ct);
}
