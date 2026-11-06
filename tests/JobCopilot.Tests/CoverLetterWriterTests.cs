using System.Net;
using System.Text;
using JobCopilot.Core.Llm;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Tests;

public class AnthropicWriterTests
{
    // A real stream, trimmed: the framing lines, a thinking delta and a ping all
    // have to be skipped, and only the text deltas kept.
    private const string SseBody = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_01","model":"claude-sonnet-5-5"}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: ping
        data: {"type":"ping"}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Twelve years"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" of .NET."}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":12}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    [Fact]
    public async Task Keeps_text_deltas_and_skips_the_framing()
    {
        var chunks = await Collect(AnthropicWriter.ReadTextDeltas(Utf8(SseBody), CancellationToken.None));

        Assert.Equal(["Twelve years", " of .NET."], chunks);
    }

    [Fact]
    public async Task Throws_on_a_mid_stream_error_event()
    {
        const string body = """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Dear"}}

            event: error
            data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}

            """;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Collect(AnthropicWriter.ReadTextDeltas(Utf8(body), CancellationToken.None)));

        Assert.Contains("Overloaded", ex.Message);
    }

    [Fact]
    public async Task Throws_when_the_model_refuses_part_way_through()
    {
        const string body = """
            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"refusal"}}

            """;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Collect(AnthropicWriter.ReadTextDeltas(Utf8(body), CancellationToken.None)));
    }

    [Fact]
    public async Task Asks_for_a_stream_and_keeps_effort_low()
    {
        var handler = new CapturingSseHandler(SseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") };
        var writer = new AnthropicWriter(http, Options.Create(new AnthropicOptions { ApiKey = "test-key" }));

        var chunks = await Collect(writer.StreamAsync(Context(), CancellationToken.None));

        Assert.Equal("Twelve years of .NET.", string.Concat(chunks));
        Assert.Contains("\"stream\":true", handler.Body);
        Assert.Contains("\"effort\":\"low\"", handler.Body);
        Assert.DoesNotContain("tool_choice", handler.Body);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Uri);
    }

    [Fact]
    public async Task Fails_fast_without_an_api_key()
    {
        var writer = new AnthropicWriter(
            new HttpClient { BaseAddress = new Uri("https://api.anthropic.com/") },
            Options.Create(new AnthropicOptions()));

        Assert.False(writer.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Collect(writer.StreamAsync(Context(), CancellationToken.None)));
    }

    internal static CoverLetterContext Context(CoverLetterTone tone = CoverLetterTone.Professional) =>
        new(new CoverLetterRequest("12 years of C# and Azure", "Senior .NET engineer", tone, "Acme", "Senior Backend Engineer"),
            new MatchResult(70, new KeywordAnalysis(50, ["C#"], ["Kafka"]), null, null, null));

    internal static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    internal static async Task<List<string>> Collect(IAsyncEnumerable<string> chunks)
    {
        var collected = new List<string>();
        await foreach (var chunk in chunks)
            collected.Add(chunk);
        return collected;
    }

    private sealed class CapturingSseHandler(string responseBody) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream"),
            };
        }
    }
}

public class OllamaWriterTests
{
    [Fact]
    public async Task Joins_ndjson_chunks_and_stops_at_done()
    {
        const string body = """
            {"message":{"role":"assistant","content":"Twelve years"},"done":false}
            {"message":{"role":"assistant","content":" of .NET."},"done":false}
            {"message":{"role":"assistant","content":""},"done":true}
            {"message":{"role":"assistant","content":"never read"},"done":false}
            """;

        var chunks = await AnthropicWriterTests.Collect(
            OllamaWriter.ReadContentChunks(AnthropicWriterTests.Utf8(body), CancellationToken.None));

        Assert.Equal("Twelve years of .NET.", string.Concat(chunks));
    }

    [Fact]
    public async Task Throws_on_an_in_band_error()
    {
        const string body = """{"error":"model requires more system memory"}""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnthropicWriterTests.Collect(
                OllamaWriter.ReadContentChunks(AnthropicWriterTests.Utf8(body), CancellationToken.None)));

        Assert.Contains("system memory", ex.Message);
    }
}
