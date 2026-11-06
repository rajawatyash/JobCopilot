using System.Net.Http.Json;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Calls the Claude Messages API directly over HttpClient, so every part of the
/// request is visible. It forces a tool call, which makes the model return JSON
/// that matches <see cref="AssessmentSchema"/> instead of free text.
/// </summary>
public sealed class AnthropicAssessor(HttpClient http, IOptions<AnthropicOptions> options) : ILlmAssessor
{
    private const string ToolName = "record_assessment";
    private readonly AnthropicOptions _options = options.Value;

    public string Name => $"anthropic:{_options.Model}";

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
                ["input_schema"] = JsonNode.Parse(AssessmentSchema.Json),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = ToolName },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = Prompts.User(request, keywords, Prompts.ToolInstruction),
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

        return AssessmentSchema.Parse(toolUse["input"]);
    }
}
