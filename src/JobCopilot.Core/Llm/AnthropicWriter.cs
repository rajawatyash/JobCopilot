using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Streams a cover letter from the Claude Messages API ("stream": true) and
/// turns the Server-Sent Events into plain text chunks. Only text deltas are
/// yielded, so keep-alive pings and thinking blocks fall away here.
/// </summary>
public sealed class AnthropicWriter(HttpClient http, IOptions<AnthropicOptions> options) : ILlmWriter
{
    private readonly AnthropicOptions _options = options.Value;

    public string Name => $"anthropic:{_options.Model}";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async IAsyncEnumerable<string> StreamAsync(
        CoverLetterContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Anthropic:ApiKey is not set.");

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            ["stream"] = true,
            ["system"] = Prompts.CoverLetterSystem,
            // Writing a letter from material already gathered is a drafting job,
            // not a reasoning one. Low effort keeps the first token quick and the
            // bill small; the assessment call is where thinking earns its cost.
            ["output_config"] = new JsonObject { ["effort"] = "low" },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = Prompts.CoverLetterUser(context),
            }),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-api-key", _options.ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");

        // ResponseHeadersRead returns as soon as the headers land, so the body is
        // read as it arrives. Without it HttpClient buffers the whole response and
        // "streaming" would only begin once the letter was already finished.
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {error}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var chunk in ReadTextDeltas(stream, ct))
            yield return chunk;
    }

    internal static async IAsyncEnumerable<string> ReadTextDeltas(
        Stream sse,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(sse);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            // An SSE frame is an "event:" line, a "data:" line and a blank line.
            // The event name is repeated inside the payload, so the data line is
            // all this needs to read.
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = JsonNode.Parse(line["data:".Length..].Trim());
            var type = payload?["type"]?.GetValue<string>();
            var delta = payload?["delta"];

            if (type == "error")
                throw new InvalidOperationException(
                    $"Claude streamed an error: {payload?["error"]?["message"]?.GetValue<string>()}");

            // A refusal arrives mid-stream, long after the 200 OK went out.
            if (type == "message_delta" && delta?["stop_reason"]?.GetValue<string>() == "refusal")
                throw new InvalidOperationException("Claude declined to write this letter.");

            // Everything else - message_start, content_block_start, pings, and
            // thinking deltas - is framing this does not need.
            if (type != "content_block_delta" || delta?["type"]?.GetValue<string>() != "text_delta")
                continue;

            var text = delta?["text"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}
