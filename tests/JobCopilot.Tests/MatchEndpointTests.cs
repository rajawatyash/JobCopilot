using System.Net;
using System.Net.Http.Json;
using JobCopilot.Core.Llm;
using JobCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobCopilot.Tests;

public class MatchEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient ClientWith(ILlmAssessor llm) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ILlmAssessor>();
            s.AddSingleton(llm);
        })).CreateClient();

    [Fact]
    public async Task Combines_keyword_and_ai_scores()
    {
        var client = ClientWith(new FakeLlm(score: 80));

        var response = await client.PostAsJsonAsync("/api/match",
            new MatchRequest("C# and Azure", "C#, Azure, Kafka, Kubernetes"));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MatchResult>();
        Assert.Equal(50, result!.Keywords.CoveragePercent);
        Assert.Equal(71, result.OverallScore); // 0.7 * 80 + 0.3 * 50
    }

    [Fact]
    public async Task Falls_back_to_keywords_when_ai_fails()
    {
        var client = ClientWith(new FakeLlm(score: 0, fail: true));

        var result = await (await client.PostAsJsonAsync("/api/match",
            new MatchRequest("C#", "C# and Kafka"))).Content.ReadFromJsonAsync<MatchResult>();

        Assert.Null(result!.Ai);
        Assert.Equal(50, result.OverallScore);
        Assert.Contains("failed", result.AiError);
    }

    [Fact]
    public async Task Rejects_empty_input()
    {
        var client = ClientWith(new FakeLlm(score: 0));
        var response = await client.PostAsJsonAsync("/api/match", new MatchRequest("", "JD"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class FakeLlm(int score, bool fail = false) : ILlmAssessor
    {
        public string Name => "fake";
        public bool IsConfigured => true;

        public Task<LlmAssessment> AssessAsync(MatchRequest request, KeywordAnalysis keywords, CancellationToken ct) =>
            fail
                ? throw new HttpRequestException("boom")
                : Task.FromResult(new LlmAssessment(score, "ok", [], [], []));
    }
}

public class ProviderSwitchTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Ollama", typeof(OllamaAssessor))]
    [InlineData("Anthropic", typeof(AnthropicAssessor))]
    public void Config_selects_the_provider(string provider, Type expected)
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Llm:Provider", provider));
        using var scope = app.Services.CreateScope();

        var assessor = scope.ServiceProvider.GetRequiredService<ILlmAssessor>();

        Assert.IsType(expected, assessor);
    }
}
