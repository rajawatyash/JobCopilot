using System.Net.Http.Json;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Calls the Claude Messages API directly over HttpClient, so every part of the
/// request is visible. Structured outputs (output_config.format) constrain the
/// reply to <see cref="AssessmentSchema"/>, so it arrives as JSON instead of
/// free text. This used to force a tool call instead, but current models reject
/// tool_choice "tool" and "any" with a 400.
/// </summary>
public sealed class AnthropicAssessor(HttpClient http, IOptions<AnthropicOptions> options) : ILlmAssessor
{
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
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["schema"] = JsonNode.Parse(AssessmentSchema.Json),
                },
            },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = Prompts.User(request, keywords, Prompts.JsonInstruction),
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

        return ParseJsonOutput(text);
    }

    internal static LlmAssessment ParseJsonOutput(string responseJson)
    {
        var root = JsonNode.Parse(responseJson)
            ?? throw new InvalidOperationException("Claude response was empty.");

        // The schema only holds when the model ran to the end of its answer.
        switch (root["stop_reason"]?.GetValue<string>())
        {
            case "refusal":
                throw new InvalidOperationException("Claude declined to assess this CV and job description.");
            case "max_tokens":
                throw new InvalidOperationException("Assessment was cut off by max_tokens; raise Anthropic:MaxTokens.");
        }

        var json = root["content"]?.AsArray()
            .FirstOrDefault(block => block?["type"]?.GetValue<string>() == "text")
            ?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Claude response had no text block.");

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"Model output was not valid JSON: {ex.Message}");
        }

        return AssessmentSchema.Parse(parsed);
    }
}
