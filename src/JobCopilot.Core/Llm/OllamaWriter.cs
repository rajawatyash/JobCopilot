using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Streams a cover letter from a local model through Ollama's /api/chat. Ollama
/// streams newline-delimited JSON rather than SSE, which is the reason the
/// transport sits behind <see cref="ILlmWriter"/>: the API endpoint re-frames
/// whichever provider is in use as one SSE stream for the browser.
/// </summary>
public sealed class OllamaWriter(HttpClient http, IOptions<OllamaOptions> options) : ILlmWriter
{
    private readonly OllamaOptions _options = options.Value;

    public string Name => $"ollama:{_options.Model}";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Model);

    public async IAsyncEnumerable<string> StreamAsync(
        CoverLetterContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = true,
            ["options"] = new JsonObject { ["num_ctx"] = _options.ContextLength },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Prompts.CoverLetterSystem },
                new JsonObject { ["role"] = "user", ["content"] = Prompts.CoverLetterUser(context) }),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(body),
        };

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {error}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var chunk in ReadContentChunks(stream, ct))
            yield return chunk;
    }

    internal static async IAsyncEnumerable<string> ReadContentChunks(
        Stream ndjson,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(ndjson);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var payload = JsonNode.Parse(line);

            // Ollama reports failures in-band: a 200 response with an error field.
            if (payload?["error"] is { } error)
                throw new InvalidOperationException($"Ollama streamed an error: {error.GetValue<string>()}");

            var text = payload?["message"]?["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
                yield return text;

            if (payload?["done"]?.GetValue<bool>() == true)
                break;
        }
    }
}
