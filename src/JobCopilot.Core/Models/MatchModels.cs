namespace JobCopilot.Core.Models;

public sealed record MatchRequest(string CvText, string JobDescription);

/// <summary>Deterministic skill coverage, computed without any AI call.</summary>
public sealed record KeywordAnalysis(
    int CoveragePercent,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing);

/// <summary>What the LLM returns, forced into this shape via tool use.</summary>
public sealed record LlmAssessment(
    int Score,
    string Summary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> SuggestedBullets);

public sealed record MatchResult(
    int OverallScore,
    KeywordAnalysis Keywords,
    LlmAssessment? Ai,
    string? AiModel,
    string? AiError);
