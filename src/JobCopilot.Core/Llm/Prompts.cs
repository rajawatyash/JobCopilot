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

    public static string User(MatchRequest request, KeywordAnalysis keywords) => $"""
        <job_description>
        {request.JobDescription}
        </job_description>

        <cv>
        {request.CvText}
        </cv>

        A keyword scan found these JD skills in the CV: {string.Join(", ", keywords.Matched)}.
        And these JD skills missing from the CV: {string.Join(", ", keywords.Missing)}.
        The scan is crude, so use it as a hint only.

        Record your assessment with the tool.
        """;
}
