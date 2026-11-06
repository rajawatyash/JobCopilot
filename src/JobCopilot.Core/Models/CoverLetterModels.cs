namespace JobCopilot.Core.Models;

/// <summary>
/// A closed set, deliberately: the tone goes straight into the prompt, so a
/// free-text tone would let the caller write part of the prompt.
/// </summary>
public enum CoverLetterTone
{
    Professional,
    Direct,
    Warm,
    Enthusiastic,
}

public sealed record CoverLetterRequest(
    string CvText,
    string JobDescription,
    CoverLetterTone Tone = CoverLetterTone.Professional,
    string? CompanyName = null,
    string? RoleTitle = null);

/// <summary>The request plus the grounding pass the letter must stay inside.</summary>
public sealed record CoverLetterContext(CoverLetterRequest Request, MatchResult Match);

/// <summary>
/// A letter that is ready to stream: the match has already run, so anything that
/// could still fail with a real status code has failed by now.
/// </summary>
public sealed record CoverLetterDraft(MatchResult Match, string WriterModel, IAsyncEnumerable<string> Chunks);
