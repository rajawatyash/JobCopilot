using JobCopilot.Core.Llm;
using JobCopilot.Core.Matching;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace JobCopilot.Core.Services;

public interface IMatchService
{
    Task<MatchResult> MatchAsync(MatchRequest request, CancellationToken ct);
}

/// <summary>
/// Runs the cheap keyword scan first, then asks the LLM. If the LLM is not
/// configured or fails, the keyword result is still returned.
/// </summary>
public sealed class MatchService(IKeywordMatcher keywords, ILlmAssessor llm, ILogger<MatchService> logger) : IMatchService
{
    // Weighting between the deterministic scan and the model's judgement.
    private const double AiWeight = 0.7;

    public async Task<MatchResult> MatchAsync(MatchRequest request, CancellationToken ct)
    {
        var scan = keywords.Analyse(request.CvText, request.JobDescription);

        if (!llm.IsConfigured)
            return new MatchResult(scan.CoveragePercent, scan, null, llm.Name, "AI assessment skipped: provider not configured (no API key or model).");

        try
        {
            var ai = await llm.AssessAsync(request, scan, ct);
            var overall = (int)Math.Round(AiWeight * ai.Score + (1 - AiWeight) * scan.CoveragePercent);
            return new MatchResult(overall, scan, ai, llm.Name, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AI assessment with {Model} failed; returning keyword result only", llm.Name);
            return new MatchResult(scan.CoveragePercent, scan, null, llm.Name, $"AI assessment failed: {ex.Message}");
        }
    }
}
