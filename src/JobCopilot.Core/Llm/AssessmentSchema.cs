using System.Text.Json;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;

namespace JobCopilot.Core.Llm;

/// <summary>
/// The JSON shape every provider must return. Claude gets it as
/// output_config.format; Ollama gets it as the structured-output "format".
/// Claude's structured outputs reject numeric constraints (minimum/maximum) and
/// require additionalProperties: false, so the score range is documented in the
/// description and enforced by <see cref="Parse"/> instead.
/// </summary>
internal static class AssessmentSchema
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public const string Json = """
    {
      "type": "object",
      "properties": {
        "score":            { "type": "integer", "description": "Overall fit for the role, from 0 to 100" },
        "summary":          { "type": "string", "description": "Two or three sentences on overall fit" },
        "strengths":        { "type": "array", "items": { "type": "string" } },
        "gaps":             { "type": "array", "items": { "type": "string" }, "description": "Requirements the CV does not evidence" },
        "suggestedBullets": { "type": "array", "items": { "type": "string" }, "description": "CV bullet rewrites that use the JD's wording, only for experience the CV already shows" }
      },
      "required": ["score", "summary", "strengths", "gaps", "suggestedBullets"],
      "additionalProperties": false
    }
    """;

    /// <summary>Deserialises and sanity-checks model output. Models can and do go out of range.</summary>
    public static LlmAssessment Parse(JsonNode? node)
    {
        var assessment = node.Deserialize<LlmAssessment>(Options)
            ?? throw new InvalidOperationException("Model returned an empty assessment.");

        return assessment with
        {
            Score = Math.Clamp(assessment.Score, 0, 100),
            Summary = assessment.Summary ?? "",
            Strengths = assessment.Strengths ?? [],
            Gaps = assessment.Gaps ?? [],
            SuggestedBullets = assessment.SuggestedBullets ?? [],
        };
    }
}
