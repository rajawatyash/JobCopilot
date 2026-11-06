using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using JobCopilot.Core.Llm;
using JobCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobCopilot.Tests;

public class CoverLetterEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient ClientWith(ILlmWriter writer) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ILlmWriter>();
            s.AddSingleton(writer);
            // The grounding pass runs for real, so the assessor is faked too.
            s.RemoveAll<ILlmAssessor>();
            s.AddSingleton<ILlmAssessor>(new StubAssessor());
        })).CreateClient();

    private static CoverLetterRequest Request(CoverLetterTone tone = CoverLetterTone.Professional) =>
        new("12 years of C# and Azure", "Senior .NET engineer with C#, Azure and Kafka", tone, "Acme", "Senior Backend Engineer");

    [Fact]
    public async Task Streams_deltas_then_a_done_frame()
    {
        var client = ClientWith(new StubWriter("Twelve years", " of .NET."));

        var response = await client.PostAsJsonAsync("/api/cover-letter", Request());
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: delta", body);
        Assert.Contains("\"text\":\"Twelve years\"", body);
        Assert.Contains("\"text\":\" of .NET.\"", body);
        Assert.Contains("event: done", body);
        // The done frame reports which models produced the match and the letter.
        Assert.Contains("\"writerModel\":\"stub-writer\"", body);
        Assert.Contains("\"overallScore\":", body);
        Assert.DoesNotContain("event: error", body);
    }

    [Fact]
    public async Task Reports_a_mid_stream_failure_as_an_error_frame()
    {
        // The provider dies after the first chunk. The status code is already 200
        // by then, so the only honest place left to report it is the stream.
        var client = ClientWith(new StubWriter("Dear hiring manager") { FailAfterFirstChunk = true });

        var response = await client.PostAsJsonAsync("/api/cover-letter", Request());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("event: delta", body);
        Assert.Contains("event: error", body);
        Assert.Contains("provider gave up", body);
        Assert.DoesNotContain("event: done", body);
    }

    [Fact]
    public async Task Accepts_a_tone_by_name()
    {
        var writer = new StubWriter("ok");
        var client = ClientWith(writer);

        var response = await client.PostAsJsonAsync("/api/cover-letter", Request(CoverLetterTone.Warm));

        response.EnsureSuccessStatusCode();
        Assert.Equal(CoverLetterTone.Warm, writer.LastContext?.Request.Tone);
    }

    [Fact]
    public async Task Rejects_an_unknown_tone_before_streaming()
    {
        var client = ClientWith(new StubWriter("ok"));

        var response = await client.PostAsync("/api/cover-letter", JsonContent.Create(
            new { cvText = "cv", jobDescription = "jd", tone = "Sarcastic" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_malformed_body()
    {
        var client = ClientWith(new StubWriter("ok"));

        var response = await client.PostAsync("/api/cover-letter",
            new StringContent("{ \"cvText\": ", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_empty_input()
    {
        var client = ClientWith(new StubWriter("ok"));

        var response = await client.PostAsJsonAsync("/api/cover-letter",
            new CoverLetterRequest("", "Senior .NET engineer"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_503_when_no_writer_is_configured()
    {
        var client = ClientWith(new StubWriter("never called") { Configured = false });

        var response = await client.PostAsJsonAsync("/api/cover-letter", Request());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class StubAssessor : ILlmAssessor
    {
        public string Name => "stub-assessor";
        public bool IsConfigured => true;

        public Task<LlmAssessment> AssessAsync(MatchRequest request, KeywordAnalysis keywords, CancellationToken ct) =>
            Task.FromResult(new LlmAssessment(70, "ok", ["Azure at scale"], ["Kafka"], []));
    }

    private sealed class StubWriter(params string[] chunks) : ILlmWriter
    {
        public string Name => "stub-writer";
        public bool Configured { get; init; } = true;
        public bool FailAfterFirstChunk { get; init; }
        public CoverLetterContext? LastContext { get; private set; }

        public bool IsConfigured => Configured;

        public async IAsyncEnumerable<string> StreamAsync(
            CoverLetterContext context,
            [EnumeratorCancellation] CancellationToken ct)
        {
            LastContext = context;

            foreach (var chunk in chunks)
            {
                yield return chunk;
                await Task.Yield();

                if (FailAfterFirstChunk)
                    throw new HttpRequestException("the provider gave up");
            }
        }
    }
}
