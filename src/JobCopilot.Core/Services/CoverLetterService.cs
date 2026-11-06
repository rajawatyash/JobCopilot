using JobCopilot.Core.Llm;
using JobCopilot.Core.Models;

namespace JobCopilot.Core.Services;

public interface ICoverLetterService
{
    /// <summary>
    /// Runs the grounding pass and hands back a stream that has not started yet.
    /// Everything that can fail with a status code fails inside this call.
    /// </summary>
    Task<CoverLetterDraft> StartAsync(CoverLetterRequest request, CancellationToken ct);
}

/// <summary>
/// Matches the CV against the JD first, then asks the writer for a letter that
/// leans on the strengths it found and stays off the gaps. Reusing
/// <see cref="IMatchService"/> means the letter is grounded in the same
/// assessment a caller sees from /api/match.
/// </summary>
public sealed class CoverLetterService(IMatchService matcher, ILlmWriter writer) : ICoverLetterService
{
    public async Task<CoverLetterDraft> StartAsync(CoverLetterRequest request, CancellationToken ct)
    {
        if (!writer.IsConfigured)
            throw new InvalidOperationException(
                $"Cannot write a letter: {writer.Name} is not configured (no API key or model).");

        var match = await matcher.MatchAsync(new MatchRequest(request.CvText, request.JobDescription), ct);
        var context = new CoverLetterContext(request, match);

        // Deliberately not enumerated here: the endpoint pulls it frame by frame.
        return new CoverLetterDraft(match, writer.Name, writer.StreamAsync(context, ct));
    }
}
