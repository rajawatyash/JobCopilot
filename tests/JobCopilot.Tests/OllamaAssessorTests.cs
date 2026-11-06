using System.Net;
using System.Text;
using JobCopilot.Core.Llm;
using JobCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace JobCopilot.Tests;

public class OllamaAssessorTests
{
    [Fact]
    public void Parses_json_string_inside_message_content()
    {
        const string response = """
        {
          "model": "qwen2.5:7b",
          "message": { "role": "assistant", "content": "{\"score\": -5, \"summary\": \"Partial fit\", \"strengths\": [\"C#\"], \"gaps\": [\"Kafka\"], \"suggestedBullets\": []}" },
          "done": true
        }
        """;

        var result = OllamaAssessor.ParseChatResponse(response);

        Assert.Equal(0, result.Score);
        Assert.Equal(["Kafka"], result.Gaps);
    }

    [Fact]
    public void Throws_on_non_json_content()
    {
        const string response = """{ "message": { "role": "assistant", "content": "Sure! Here is my answer" } }""";
        Assert.Throws<InvalidOperationException>(() => OllamaAssessor.ParseChatResponse(response));
    }

    [Fact]
    public async Task Sends_schema_as_format_and_disables_streaming()
    {
        var handler = new CapturingHandler("""
            { "message": { "content": "{\"score\": 70, \"summary\": \"ok\", \"strengths\": [], \"gaps\": [], \"suggestedBullets\": []}" } }
            """);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var sut = new OllamaAssessor(http, Options.Create(new OllamaOptions { Model = "llama3.1:8b" }));

        var result = await sut.AssessAsync(new MatchRequest("cv", "jd"), new KeywordAnalysis(0, [], []), CancellationToken.None);

        Assert.Equal(70, result.Score);
        Assert.Equal("http://localhost:11434/api/chat", handler.Uri);
        Assert.Contains("\"stream\":false", handler.Body);
        Assert.Contains("\"format\":{\"type\":\"object\"", handler.Body);
        Assert.Contains("\"model\":\"llama3.1:8b\"", handler.Body);
        Assert.Contains("\"num_ctx\":8192", handler.Body);
        Assert.Equal("ollama:llama3.1:8b", sut.Name);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
