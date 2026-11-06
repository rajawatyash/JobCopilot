using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Calls the Claude Messages API directly over HttpClient, so every part of the
/// request is visible. It forces a tool call, which makes the model return JSON
/// that matches <see cref="ToolSchema"/> instead of free text.
/// </summary>
public sealed class AnthropicAssessor(HttpClient http, IOptions<AnthropicOptions> options) : ILlmAssessor
{
    private const string ToolName = "record_assessment";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AnthropicOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<LlmAssessment> AssessAsync(MatchRequest request, KeywordAnalysis keywords, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Anthropic:ApiKey is not set.");

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            ["system"] = Prompts.System,
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = ToolName,
                ["description"] = "Record the CV-to-job assessment.",
                ["input_schema"] = JsonNode.Parse(ToolSchema),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = ToolName },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = Prompts.User(request, keywords),
            }),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-api-key", _options.ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {text}");

        return ParseToolInput(text);
    }

    internal static LlmAssessment ParseToolInput(string responseJson)
    {
        var root = JsonNode.Parse(responseJson)!;
        var toolUse = root["content"]!.AsArray()
            .FirstOrDefault(c => c?["type"]?.GetValue<string>() == "tool_use"
                              && c["name"]?.GetValue<string>() == ToolName)
            ?? throw new InvalidOperationException("Model did not return a tool_use block.");

        var assessment = toolUse["input"].Deserialize<LlmAssessment>(Json)
            ?? throw new InvalidOperationException("Tool input was empty.");

        return assessment with { Score = Math.Clamp(assessment.Score, 0, 100) };
    }

    private const string ToolSchema = """
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
}
