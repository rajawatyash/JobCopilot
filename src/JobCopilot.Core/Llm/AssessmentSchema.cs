using System.Text.Json;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;

namespace JobCopilot.Core.Llm;

/// <summary>
/// The JSON shape every provider must return. Claude gets it as a tool's
/// input_schema; Ollama gets it as the structured-output "format".
/// </summary>
internal static class AssessmentSchema
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public const string Json = """
    {
      "type": "object",
      "properties": {
        "score":            { "type": "integer", "minimum": 0, "maximum": 100, "description": "Overall fit for the role" },
        "summary":          { "type": "string", "description": "Two or three sentences on overall fit" },
        "strengths":        { "type": "array", "items": { "type": "string" } },
        "gaps":             { "type": "array", "items": { "type": "string" }, "description": "Requirements the CV does not evidence" },
        "suggestedBullets": { "type": "array", "items": { "type": "string" }, "description": "CV bullet rewrites that use the JD's wording, only for experience the CV already shows" }
      },
      "required": ["score", "summary", "strengths", "gaps", "suggestedBullets"]
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
