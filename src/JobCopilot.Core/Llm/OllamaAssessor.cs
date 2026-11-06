using System.Net.Http.Json;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Calls a locally hosted model through Ollama's /api/chat. Passing the JSON
/// schema as "format" makes Ollama constrain the output to that shape, which is
/// more reliable on small models than asking them to call a tool.
/// </summary>
public sealed class OllamaAssessor(HttpClient http, IOptions<OllamaOptions> options) : ILlmAssessor
{
    private readonly OllamaOptions _options = options.Value;

    public string Name => $"ollama:{_options.Model}";

    // No key needed. If Ollama isn't running, the call fails and MatchService falls back.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Model);

    public async Task<LlmAssessment> AssessAsync(MatchRequest request, KeywordAnalysis keywords, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["format"] = JsonNode.Parse(AssessmentSchema.Json),
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = _options.ContextLength },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Prompts.System },
                new JsonObject { ["role"] = "user", ["content"] = Prompts.User(request, keywords, Prompts.JsonInstruction) }),
        };

        using var response = await http.PostAsJsonAsync("api/chat", body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {text}");

        return ParseChatResponse(text);
    }

    internal static LlmAssessment ParseChatResponse(string responseJson)
    {
        // Ollama returns the model's JSON as a string inside message.content.
        var content = JsonNode.Parse(responseJson)?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama response had no message content.");

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(content);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"Model output was not valid JSON: {ex.Message}");
        }

        return AssessmentSchema.Parse(parsed);
    }
}
