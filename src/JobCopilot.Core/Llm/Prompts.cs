using JobCopilot.Core.Models;

namespace JobCopilot.Core.Llm;

internal static class Prompts
{
    public const string System = """
        You are a strict technical recruiter screening a CV against a job description.
        Judge only on evidence in the CV. Never invent experience the CV does not show.
        Suggested bullets must rephrase real CV experience using the job description's wording.
        Be honest about gaps: a missing must-have lowers the score a lot, a missing nice-to-have a little.
        """;

    public static string User(MatchRequest request, KeywordAnalysis keywords, string closing) => $"""
        <job_description>
        {request.JobDescription}
        </job_description>

        <cv>
        {request.CvText}
        </cv>

        A keyword scan found these JD skills in the CV: {string.Join(", ", keywords.Matched)}.
        And these JD skills missing from the CV: {string.Join(", ", keywords.Missing)}.
        The scan is crude, so use it as a hint only.

        {closing}
        """;

    // Both providers also receive the schema itself (Claude as output_config.format,
    // Ollama as format), but naming the fields helps small local models comply.
    public const string JsonInstruction =
        "Reply with only a JSON object with the fields score, summary, strengths, gaps and suggestedBullets.";

    public const string CoverLetterSystem = """
        You write cover letters in the candidate's own voice, from their CV.
        Use only experience the CV evidences. Never invent an employer, a date, a number or a technology.
        Where the job asks for something the CV does not show, do not claim it: leave it out, or name the
        nearest real experience and be straight about the difference.
        Write three or four short paragraphs, under 300 words. No greeting line, no sign-off, no subject line.
        Do not open with "I am writing to apply" or "I am excited to apply".
        Write plain prose: no bullet points, no headings, no markdown.
        """;

    /// <summary>
    /// The letter prompt carries the match result, so the model leads with what
    /// the CV actually evidences and knows which requirements it must not claim.
    /// </summary>
    public static string CoverLetterUser(CoverLetterContext context) => $"""
        <job_description>
        {context.Request.JobDescription}
        </job_description>

        <cv>
        {context.Request.CvText}
        </cv>

        {Target(context.Request)}
        Lead with these strengths, which the CV evidences: {Join(context.Match.Ai?.Strengths, context.Match.Keywords.Matched)}.
        Do not claim these, which the CV does not evidence: {Join(context.Match.Ai?.Gaps, context.Match.Keywords.Missing)}.

        Tone: {Describe(context.Request.Tone)}.

        Write the letter now, and nothing else.
        """;

    private static string Target(CoverLetterRequest request) => (request.RoleTitle, request.CompanyName) switch
    {
        ({ } role, { } company) => $"The role is {role} at {company}.",
        ({ } role, null) => $"The role is {role}.",
        (null, { } company) => $"The company is {company}.",
        _ => "The job title and company are not given, so refer to the role in general terms.",
    };

    // The AI assessment is the better source, but it is optional: with no
    // provider configured, or after a failed call, the keyword scan still holds.
    private static string Join(IReadOnlyList<string>? preferred, IReadOnlyList<string> fallback) =>
        preferred is { Count: > 0 }
            ? string.Join("; ", preferred)
            : fallback.Count > 0 ? string.Join("; ", fallback) : "none identified";

    private static string Describe(CoverLetterTone tone) => tone switch
    {
        CoverLetterTone.Direct => "direct and plain. Short sentences, few adjectives, straight to the evidence",
        CoverLetterTone.Warm => "warm and personable, while staying professional",
        CoverLetterTone.Enthusiastic => "genuinely keen about this role, without hyperbole or flattery",
        _ => "professional and measured, the register of a senior engineer writing to a peer",
    };
}
